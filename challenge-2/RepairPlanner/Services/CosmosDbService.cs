using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

public sealed class CosmosDbService
{
    private readonly CosmosClient _client;
    private readonly Container _techniciansContainer;
    private readonly Container _partsContainer;
    private readonly Container _workOrdersContainer;
    private readonly ILogger<CosmosDbService> _logger;

    public CosmosDbService(
        CosmosDbOptions options,
        ILogger<CosmosDbService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(logger);

        if (string.IsNullOrWhiteSpace(options.Endpoint))
            throw new ArgumentException("Cosmos DB endpoint is required", nameof(options.Endpoint));
        if (string.IsNullOrWhiteSpace(options.Key))
            throw new ArgumentException("Cosmos DB key is required", nameof(options.Key));
        if (string.IsNullOrWhiteSpace(options.DatabaseName))
            throw new ArgumentException("Database name is required", nameof(options.DatabaseName));

        _logger = logger;

        try
        {
            _client = new CosmosClient(options.Endpoint, options.Key);
            var database = _client.GetDatabase(options.DatabaseName);

            _techniciansContainer = database.GetContainer("Technicians");
            _partsContainer = database.GetContainer("PartsInventory");
            _workOrdersContainer = database.GetContainer("WorkOrders");

            _logger.LogInformation("CosmosDbService initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize CosmosDbService");
            throw;
        }
    }

    /// <summary>
    /// Gets available technicians that have all required skills.
    /// </summary>
    public async Task<List<Technician>> GetAvailableTechniciansWithSkillsAsync(
        List<string> requiredSkills,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (requiredSkills == null || requiredSkills.Count == 0)
            {
                _logger.LogWarning("No required skills specified for technician query");
                return new List<Technician>();
            }

            // Query for all available technicians
            var query = @"
                SELECT * FROM Technicians t 
                WHERE t.isAvailable = true
            ";

            var iterator = _techniciansContainer.GetItemQueryIterator<Technician>(query);
            var technicians = new List<Technician>();

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                technicians.AddRange(response);
            }

            // Filter in-memory: keep technicians that have ALL required skills
            var matchingTechnicians = technicians
                .Where(t => t.Skills != null && requiredSkills.All(skill =>
                    t.Skills.Any(s => s.Equals(skill, StringComparison.OrdinalIgnoreCase))))
                .OrderByDescending(t => t.YearsOfExperience) // Prefer more experienced technicians
                .ToList();

            _logger.LogInformation(
                "Found {Count} available technicians matching skills {Skills}",
                matchingTechnicians.Count,
                string.Join(", ", requiredSkills));

            return matchingTechnicians;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error querying available technicians with skills {Skills}",
                string.Join(", ", requiredSkills ?? new List<string>()));
            throw;
        }
    }

    /// <summary>
    /// Gets parts inventory by part numbers.
    /// </summary>
    public async Task<List<Part>> GetPartsInventoryAsync(
        List<string> partNumbers,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (partNumbers == null || partNumbers.Count == 0)
            {
                _logger.LogWarning("No part numbers specified for inventory query");
                return new List<Part>();
            }

            var parts = new List<Part>();

            // Query parts in batches to avoid query complexity limits
            foreach (var partNumber in partNumbers)
            {
                var query = @"
                    SELECT * FROM PartsInventory p 
                    WHERE p.partNumber = @partNumber
                ";

                var queryDef = new QueryDefinition(query)
                    .WithParameter("@partNumber", partNumber);

                var iterator = _partsContainer.GetItemQueryIterator<Part>(queryDef);

                while (iterator.HasMoreResults)
                {
                    var response = await iterator.ReadNextAsync(cancellationToken);
                    parts.AddRange(response);
                }
            }

            _logger.LogInformation(
                "Fetched {Count} parts from inventory for {RequestedCount} part numbers",
                parts.Count,
                partNumbers.Count);

            return parts;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error querying parts inventory for part numbers {PartNumbers}",
                string.Join(", ", partNumbers ?? new List<string>()));
            throw;
        }
    }

    /// <summary>
    /// Creates a new work order in the database.
    /// </summary>
    public async Task<string> CreateWorkOrderAsync(
        WorkOrder workOrder,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(workOrder);

            if (string.IsNullOrWhiteSpace(workOrder.Id))
                workOrder.Id = Guid.NewGuid().ToString();

            if (string.IsNullOrWhiteSpace(workOrder.WorkOrderNumber))
                workOrder.WorkOrderNumber = GenerateWorkOrderNumber();

            workOrder.CreatedAt = DateTime.UtcNow;
            workOrder.UpdatedAt = DateTime.UtcNow;

            // Use partition key "status" for the work order
            var response = await _workOrdersContainer.CreateItemAsync(
                workOrder,
                new PartitionKey(workOrder.Status),
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Created work order {WorkOrderNumber} (id={Id}) with status={Status} and RU cost={RuCharges}",
                workOrder.WorkOrderNumber,
                workOrder.Id,
                workOrder.Status,
                response.RequestCharge);

            return workOrder.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error creating work order {WorkOrderNumber}",
                workOrder?.WorkOrderNumber ?? "unknown");
            throw;
        }
    }

    /// <summary>
    /// Updates an existing work order.
    /// </summary>
    public async Task<string> UpdateWorkOrderAsync(
        WorkOrder workOrder,
        CancellationToken cancellationToken = default)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(workOrder);

            if (string.IsNullOrWhiteSpace(workOrder.Id))
                throw new ArgumentException("WorkOrder Id is required for update", nameof(workOrder.Id));

            workOrder.UpdatedAt = DateTime.UtcNow;

            var response = await _workOrdersContainer.UpsertItemAsync(
                workOrder,
                new PartitionKey(workOrder.Status),
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Updated work order {WorkOrderNumber} (id={Id}) with RU cost={RuCharges}",
                workOrder.WorkOrderNumber,
                workOrder.Id,
                response.RequestCharge);

            return workOrder.Id;
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Error updating work order {WorkOrderNumber}",
                workOrder?.WorkOrderNumber ?? "unknown");
            throw;
        }
    }

    /// <summary>
    /// Gets a work order by ID.
    /// </summary>
    public async Task<WorkOrder?> GetWorkOrderAsync(
        string workOrderId,
        string status,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(workOrderId))
                throw new ArgumentException("WorkOrder ID is required", nameof(workOrderId));

            if (string.IsNullOrWhiteSpace(status))
                throw new ArgumentException("Status partition key is required", nameof(status));

            var response = await _workOrdersContainer.ReadItemAsync<WorkOrder>(
                workOrderId,
                new PartitionKey(status),
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Retrieved work order {WorkOrderId} with RU cost={RuCharges}",
                workOrderId,
                response.RequestCharge);

            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            _logger.LogWarning("Work order {WorkOrderId} not found", workOrderId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving work order {WorkOrderId}", workOrderId);
            throw;
        }
    }

    /// <summary>
    /// Disposes the Cosmos DB client.
    /// </summary>
    public void Dispose()
    {
        _client?.Dispose();
        _logger.LogInformation("CosmosDbService disposed");
    }

    private static string GenerateWorkOrderNumber()
    {
        var year = DateTime.UtcNow.Year;
        var sequence = Random.Shared.Next(1, 10000);
        return $"WO-{year}-{sequence:D4}";
    }
}

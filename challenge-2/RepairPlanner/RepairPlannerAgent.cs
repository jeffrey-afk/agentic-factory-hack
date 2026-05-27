using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Logging;
using RepairPlanner.Models;

namespace RepairPlanner;

public sealed class RepairPlannerAgent(
    AIProjectClient projectClient,
    Services.CosmosDbService cosmosDb,
    Services.IFaultMappingService faultMapping,
    string modelDeploymentName,
    ILogger<RepairPlannerAgent> logger)
{
    private const string AgentName = "RepairPlannerAgent";

    private const string AgentInstructions = """
        You are a Repair Planner Agent for tire manufacturing equipment.
        
        Your job is to generate a comprehensive repair plan based on a diagnosed fault.
        You will receive:
        - The fault type and description
        - A list of available technicians with their skills
        - A list of required parts from inventory
        
        Generate a repair work order with:
        1. A descriptive title and detailed description
        2. An appropriate work order type: "corrective", "preventive", or "emergency"
        3. Priority level: "critical", "high", "medium", or "low" (based on fault severity)
        4. Assign the most qualified available technician (prefer higher experience)
        5. Create detailed repair tasks in sequence
        6. Include all required parts with quantities
        7. Estimate time for each task and total duration in MINUTES (as integers, not strings)
        8. Add relevant safety notes
        
        CRITICAL REQUIREMENTS:
        - All duration fields must be integers representing minutes (e.g., 60 not "60 minutes")
        - Tasks must have prerequisites properly defined
        - Return ONLY valid JSON matching the WorkOrder schema
        - The JSON must be parseable and complete
        
        Return the response as a valid JSON object with these fields:
        {
          "workOrderNumber": "string",
          "machineId": "string",
          "title": "string",
          "description": "string",
          "type": "corrective|preventive|emergency",
          "priority": "critical|high|medium|low",
          "status": "new",
          "assignedTo": "technician_id or null",
          "estimatedDuration": integer,
          "tasks": [
            {
              "sequence": integer,
              "title": "string",
              "description": "string",
              "estimatedDurationMinutes": integer,
              "requiredSkills": ["skill1", "skill2"],
              "safetyNotes": "string",
              "prerequisites": [sequence_numbers],
              "status": "pending"
            }
          ],
          "partsUsed": [
            {
              "partId": "string",
              "partNumber": "string",
              "quantity": integer,
              "unitCost": decimal,
              "totalCost": decimal,
              "status": "allocated"
            }
          ],
          "notes": "string",
          "relatedFaultType": "string"
        }
        """;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    /// <summary>
    /// Ensures the agent version is registered with Foundry.
    /// </summary>
    public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
    {
        try
        {
            var definition = new PromptAgentDefinition(model: modelDeploymentName)
            {
                Instructions = AgentInstructions
            };

            await projectClient.Agents.CreateAgentVersionAsync(
                AgentName,
                new AgentVersionCreationOptions(definition),
                ct);

            logger.LogInformation("Agent '{AgentName}' registered/updated with model '{Model}'", AgentName, modelDeploymentName);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to ensure agent version for '{AgentName}'", AgentName);
            throw;
        }
    }

    /// <summary>
    /// Plans a repair and creates a work order for a diagnosed fault.
    /// </summary>
    public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(
        DiagnosedFault fault,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fault);

        try
        {
            logger.LogInformation(
                "Planning repair for machine '{MachineId}', fault='{FaultType}', severity='{Severity}'",
                fault.MachineId,
                fault.FaultType,
                fault.Severity);

            // 1. Get required skills and parts from mapping
            var requiredSkills = faultMapping.GetRequiredSkills(fault.FaultType);
            var requiredPartNumbers = faultMapping.GetRequiredParts(fault.FaultType);

            logger.LogInformation(
                "Fault '{FaultType}' requires skills: {Skills}",
                fault.FaultType,
                string.Join(", ", requiredSkills));

            // 2. Query Cosmos DB for available technicians and parts
            var availableTechnicians = await cosmosDb.GetAvailableTechniciansWithSkillsAsync(
                requiredSkills.ToList(),
                ct);

            var availableParts = new List<Part>();
            if (requiredPartNumbers.Count > 0)
            {
                availableParts = await cosmosDb.GetPartsInventoryAsync(
                    requiredPartNumbers.ToList(),
                    ct);
            }

            logger.LogInformation(
                "Found {TechnicianCount} technicians and {PartCount} parts",
                availableTechnicians.Count,
                availableParts.Count);

            // 3. Build context for the agent
            var technicianContext = FormatTechnicianContext(availableTechnicians);
            var partsContext = FormatPartsContext(availableParts, requiredPartNumbers);

            var prompt = BuildPrompt(fault, technicianContext, partsContext);

            // 4. Invoke the Foundry agent
            logger.LogInformation("Invoking agent '{AgentName}'", AgentName);

            var agent = projectClient.GetAIAgent(name: AgentName);
            var response = await agent.RunAsync(prompt, thread: null, options: null, cancellationToken: ct);

            var responseText = response.Text ?? throw new InvalidOperationException("Agent returned empty response");

            logger.LogDebug("Agent response: {Response}", responseText);

            // 5. Parse the JSON response
            var workOrder = ParseWorkOrderResponse(responseText, fault, availableTechnicians, availableParts);

            // 6. Save to Cosmos DB
            var workOrderId = await cosmosDb.CreateWorkOrderAsync(workOrder, ct);

            logger.LogInformation(
                "Created and saved work order '{WorkOrderNumber}' (id={WorkOrderId}, assignedTo={TechnicianId})",
                workOrder.WorkOrderNumber,
                workOrderId,
                workOrder.AssignedTo ?? "none");

            return workOrder;
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Error planning repair for machine '{MachineId}', fault '{FaultType}'",
                fault.MachineId,
                fault.FaultType);
            throw;
        }
    }

    private string BuildPrompt(DiagnosedFault fault, string technicianContext, string partsContext)
    {
        return $"""
            A fault has been detected in tire manufacturing equipment.
            
            FAULT DETAILS:
            - Machine ID: {fault.MachineId}
            - Fault Type: {fault.FaultType}
            - Severity: {fault.Severity}
            - Description: {fault.Description}
            - Anomaly Score: {fault.AnomalyScore:F2}
            - Detected: {fault.DetectedAt:O}
            
            AVAILABLE TECHNICIANS:
            {technicianContext}
            
            REQUIRED PARTS:
            {partsContext}
            
            Generate a comprehensive repair work order based on this information.
            """;
    }

    private string FormatTechnicianContext(List<Technician> technicians)
    {
        if (technicians.Count == 0)
            return "No technicians available.";

        var lines = technicians.Select(t =>
            $"- {t.Name} (ID: {t.Id}, Experience: {t.YearsOfExperience}y, Skills: {string.Join(", ", t.Skills ?? [])})"
        );

        return string.Join(Environment.NewLine, lines);
    }

    private string FormatPartsContext(List<Part> parts, IReadOnlyList<string> requiredPartNumbers)
    {
        if (requiredPartNumbers.Count == 0)
            return "No parts required for this repair.";

        if (parts.Count == 0)
            return $"Required part numbers: {string.Join(", ", requiredPartNumbers)}. (None currently in stock)";

        var partLines = parts.Select(p =>
            $"- {p.PartNumber}: {p.Name} (Available: {p.QuantityAvailable}, Cost: ${p.UnitCost})"
        );

        return string.Join(Environment.NewLine, partLines);
    }

    private WorkOrder ParseWorkOrderResponse(
        string responseText,
        DiagnosedFault fault,
        List<Technician> technicians,
        List<Part> parts)
    {
        try
        {
            // Extract JSON from response (in case agent wrapped it in text)
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');

            if (jsonStart < 0 || jsonEnd < 0)
                throw new InvalidOperationException("No JSON found in agent response");

            var jsonText = responseText[jsonStart..(jsonEnd + 1)];

            logger.LogDebug("Parsing JSON: {Json}", jsonText);

            // Deserialize with lenient number handling
            using var doc = JsonDocument.Parse(jsonText);
            var root = doc.RootElement;

            var workOrder = new WorkOrder
            {
                Id = Guid.NewGuid().ToString(),
                WorkOrderNumber = GetStringProperty(root, "workOrderNumber") ?? $"WO-{DateTime.UtcNow.Year}-{Random.Shared.Next(1000, 9999)}",
                MachineId = GetStringProperty(root, "machineId") ?? fault.MachineId,
                Title = GetStringProperty(root, "title") ?? "Repair Required",
                Description = GetStringProperty(root, "description") ?? fault.Description,
                Type = GetStringProperty(root, "type") ?? "corrective",
                Priority = GetStringProperty(root, "priority") ?? MapFaultSeverityToPriority(fault.Severity),
                Status = "new",
                RelatedFaultType = fault.FaultType,
                EstimatedDuration = GetIntProperty(root, "estimatedDuration") ?? 120,
                Notes = GetStringProperty(root, "notes") ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            // Parse tasks
            if (root.TryGetProperty("tasks", out var tasksElement) && tasksElement.ValueKind == JsonValueKind.Array)
            {
                foreach (var taskElement in tasksElement.EnumerateArray())
                {
                    var task = new RepairTask
                    {
                        Sequence = GetIntProperty(taskElement, "sequence") ?? 0,
                        Title = GetStringProperty(taskElement, "title") ?? "Task",
                        Description = GetStringProperty(taskElement, "description") ?? string.Empty,
                        EstimatedDurationMinutes = GetIntProperty(taskElement, "estimatedDurationMinutes") ?? 30,
                        SafetyNotes = GetStringProperty(taskElement, "safetyNotes") ?? string.Empty,
                        Status = "pending"
                    };

                    if (taskElement.TryGetProperty("requiredSkills", out var skillsElement) && skillsElement.ValueKind == JsonValueKind.Array)
                    {
                        task.RequiredSkills = skillsElement.EnumerateArray()
                            .Where(s => s.ValueKind == JsonValueKind.String)
                            .Select(s => s.GetString() ?? string.Empty)
                            .ToList();
                    }

                    workOrder.Tasks.Add(task);
                }
            }

            // Parse parts
            if (root.TryGetProperty("partsUsed", out var partsElement) && partsElement.ValueKind == JsonValueKind.Array)
            {
                decimal totalPartsCost = 0;

                foreach (var partElement in partsElement.EnumerateArray())
                {
                    var partNumber = GetStringProperty(partElement, "partNumber") ?? string.Empty;
                    var quantity = GetIntProperty(partElement, "quantity") ?? 1;
                    var unitCost = GetDecimalProperty(partElement, "unitCost") ?? 0m;
                    var totalCost = unitCost * quantity;

                    var usage = new WorkOrderPartUsage
                    {
                        PartNumber = partNumber,
                        Quantity = quantity,
                        UnitCost = unitCost,
                        TotalCost = totalCost,
                        Status = "allocated"
                    };

                    // Try to find the part in inventory to set ID
                    var inventoryPart = parts.FirstOrDefault(p => p.PartNumber.Equals(partNumber, StringComparison.OrdinalIgnoreCase));
                    if (inventoryPart != null)
                    {
                        usage.PartId = inventoryPart.Id;
                    }

                    workOrder.PartsUsed.Add(usage);
                    totalPartsCost += totalCost;
                }

                workOrder.TotalPartsCost = totalPartsCost;
                workOrder.TotalPartsCount = workOrder.PartsUsed.Count;
            }

            // Try to assign the most qualified technician
            var assignedTechnicianId = GetStringProperty(root, "assignedTo");
            if (!string.IsNullOrEmpty(assignedTechnicianId))
            {
                var tech = technicians.FirstOrDefault(t => t.Id.Equals(assignedTechnicianId, StringComparison.OrdinalIgnoreCase));
                if (tech != null)
                {
                    workOrder.AssignedTo = tech.Id;
                    workOrder.AssignedAt = DateTime.UtcNow;
                }
            }
            else if (technicians.Count > 0)
            {
                // Assign the most experienced available technician
                var mostExperienced = technicians.OrderByDescending(t => t.YearsOfExperience).FirstOrDefault();
                if (mostExperienced != null)
                {
                    workOrder.AssignedTo = mostExperienced.Id;
                    workOrder.AssignedAt = DateTime.UtcNow;
                }
            }

            return workOrder;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error parsing agent response: {Response}", responseText);
            throw;
        }
    }

    private static string? GetStringProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop) && prop.ValueKind == JsonValueKind.String)
            return prop.GetString();
        return null;
    }

    private static int? GetIntProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var intVal))
                return intVal;
            if (prop.ValueKind == JsonValueKind.String && int.TryParse(prop.GetString(), out var stringIntVal))
                return stringIntVal;
        }
        return null;
    }

    private static decimal? GetDecimalProperty(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var prop))
        {
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDecimal(out var decVal))
                return decVal;
            if (prop.ValueKind == JsonValueKind.String && decimal.TryParse(prop.GetString(), out var stringDecVal))
                return stringDecVal;
        }
        return null;
    }

    private static string MapFaultSeverityToPriority(string severity)
    {
        return severity?.ToLowerInvariant() switch
        {
            "critical" => "critical",
            "high" => "high",
            "medium" => "medium",
            "low" => "low",
            _ => "medium"
        };
    }
}

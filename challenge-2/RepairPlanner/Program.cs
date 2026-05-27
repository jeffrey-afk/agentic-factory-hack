using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Azure.AI.Projects;
using RepairPlanner;
using RepairPlanner.Models;
using RepairPlanner.Services;

// Configure environment
var endpoint = Environment.GetEnvironmentVariable("AZURE_AI_PROJECT_ENDPOINT") 
    ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT environment variable is required");
var modelDeploymentName = Environment.GetEnvironmentVariable("MODEL_DEPLOYMENT_NAME") 
    ?? throw new InvalidOperationException("MODEL_DEPLOYMENT_NAME environment variable is required");
var cosmosEndpoint = Environment.GetEnvironmentVariable("COSMOS_ENDPOINT") 
    ?? throw new InvalidOperationException("COSMOS_ENDPOINT environment variable is required");
var cosmosKey = Environment.GetEnvironmentVariable("COSMOS_KEY") 
    ?? throw new InvalidOperationException("COSMOS_KEY environment variable is required");
var cosmosDatabase = Environment.GetEnvironmentVariable("COSMOS_DATABASE_NAME") 
    ?? throw new InvalidOperationException("COSMOS_DATABASE_NAME environment variable is required");

// Setup dependency injection
var services = new ServiceCollection();

// Logging
services.AddLogging(builder =>
{
    builder.AddConsole()
        .SetMinimumLevel(LogLevel.Information);
});

// Azure Foundry
services.AddSingleton(new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential()));

// Cosmos DB
services.AddSingleton(new CosmosDbOptions
{
    Endpoint = cosmosEndpoint,
    Key = cosmosKey,
    DatabaseName = cosmosDatabase
});
services.AddSingleton<CosmosDbService>();

// Services
services.AddSingleton<IFaultMappingService, FaultMappingService>();
// RepairPlannerAgent requires a model deployment name string; register with factory
services.AddSingleton<RepairPlannerAgent>(sp =>
{
    var projectClient = sp.GetRequiredService<AIProjectClient>();
    var cosmos = sp.GetRequiredService<CosmosDbService>();
    var mapping = sp.GetRequiredService<IFaultMappingService>();
    var logger = sp.GetRequiredService<ILogger<RepairPlannerAgent>>();
    return new RepairPlannerAgent(projectClient, cosmos, mapping, modelDeploymentName, logger);
});

var serviceProvider = services.BuildServiceProvider();
var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

try
{
    logger.LogInformation("Repair Planner Agent starting...");

    var projectClient = serviceProvider.GetRequiredService<AIProjectClient>();
    var agent = serviceProvider.GetRequiredService<RepairPlannerAgent>();

    // Register/update the agent
    logger.LogInformation("Registering agent with model '{Model}'", modelDeploymentName);
    await agent.EnsureAgentVersionAsync();

    // Create a sample diagnosed fault
    var sampleFault = new DiagnosedFault
    {
        FaultType = "curing_temperature_excessive",
        MachineId = "machine-001",
        Severity = "high",
        Description = "Curing press temperature exceeded safe threshold by 15°C for 2+ minutes",
        DetectedAt = DateTime.UtcNow,
        AnomalyScore = 0.87
    };

    logger.LogInformation(
        "Planning repair for machine '{MachineId}' with fault '{FaultType}'",
        sampleFault.MachineId,
        sampleFault.FaultType);

    // Generate work order
    var workOrder = await agent.PlanAndCreateWorkOrderAsync(sampleFault);

    // Display results
    logger.LogInformation("Successfully created work order!");
    Console.WriteLine();
    Console.WriteLine("=== WORK ORDER ===");
    Console.WriteLine($"Work Order Number: {workOrder.WorkOrderNumber}");
    Console.WriteLine($"Machine ID: {workOrder.MachineId}");
    Console.WriteLine($"Title: {workOrder.Title}");
    Console.WriteLine($"Type: {workOrder.Type}");
    Console.WriteLine($"Priority: {workOrder.Priority}");
    Console.WriteLine($"Status: {workOrder.Status}");
    Console.WriteLine($"Assigned To: {workOrder.AssignedTo ?? "Unassigned"}");
    Console.WriteLine($"Estimated Duration: {workOrder.EstimatedDuration} minutes");
    Console.WriteLine($"Tasks: {workOrder.Tasks.Count}");
    Console.WriteLine($"Parts: {workOrder.PartsUsed.Count} (Total Cost: ${workOrder.TotalPartsCost})");
    Console.WriteLine($"Notes: {workOrder.Notes}");

    if (workOrder.Tasks.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("=== REPAIR TASKS ===");
        foreach (var task in workOrder.Tasks)
        {
            Console.WriteLine($"  [{task.Sequence}] {task.Title}");
            Console.WriteLine($"      Duration: {task.EstimatedDurationMinutes} min");
            Console.WriteLine($"      Skills: {string.Join(", ", task.RequiredSkills)}");
            Console.WriteLine($"      Safety: {task.SafetyNotes}");
        }
    }

    if (workOrder.PartsUsed.Count > 0)
    {
        Console.WriteLine();
        Console.WriteLine("=== PARTS REQUIRED ===");
        foreach (var part in workOrder.PartsUsed)
        {
            Console.WriteLine($"  {part.PartNumber}: Qty {part.Quantity} @ ${part.UnitCost} = ${part.TotalCost}");
        }
    }

    logger.LogInformation("Repair Planner Agent completed successfully");
}
catch (Exception ex)
{
    logger.LogError(ex, "Error running Repair Planner Agent");
    Environment.Exit(1);
}

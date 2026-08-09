using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Discovery.Models;
using EMF.Discovery.Services;
using EMF.Inventory.Providers;
using EMF.Integrity;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;

var sourcePath = args.Length > 0
    ? args[0]
    : "/opt/emf-lab/datasets/LD-VET-001/extracted";

Console.WriteLine("======================================");
Console.WriteLine(" EMF Discovery + Inventory");
Console.WriteLine("======================================");
Console.WriteLine();
Console.WriteLine($"Source   : {sourcePath}");
Console.WriteLine();

var discovery = new FileSystemDiscoveryService();

var routing = new InventoryRoutingService(
    new[] { new SqliteInventoryProvider() });

var orchestration = new InventoryOrchestrationService(
    discovery,
    routing,
    new ArtifactFactory(),
    new GuidArtifactIdGenerator(),
new Sha256ContentFingerprintService());


var workflowDatabasePath =
    Path.Combine(
        AppContext.BaseDirectory,
        "emf-workflows.db");

var workflowRepository =
    new SqliteWorkflowRepository(
        workflowDatabasePath);

await workflowRepository.InitializeAsync();

var workflowDefinitionRepository =
    new SqliteWorkflowDefinitionRepository(
        workflowDatabasePath);

await workflowDefinitionRepository.InitializeAsync();

var workflowDefinitionService =
    new WorkflowDefinitionService(
        workflowDefinitionRepository);

var workflowService =
    new WorkflowService(
        workflowRepository);

var workflowRunner =
    new WorkflowRunner(
        workflowService);

var recoveryCoordinator =
    new WorkflowRecoveryCoordinator(
        workflowRepository,
        new WorkflowRecoveryPolicy());

var inventoryActivity =
new InventoryWorkflowActivity(
orchestration,
sourcePath,
new DiscoveryOptions());

var activityResolver =
new WorkflowActivityResolver(
new[] { inventoryActivity });

var executionCoordinator =
new WorkflowExecutionCoordinator(
workflowService,
recoveryCoordinator,
activityResolver,
workflowRunner);

var currentDefinition =
    new WorkflowDefinition
    {
        Id = "inventory-processing",
        Name = "Inventory Processing",
        Version = "1",
        ActivityIds = new[] { "inventory" }
    };

var storedCurrentDefinition =
    await workflowDefinitionService.ResolveAsync(
        currentDefinition.Id,
        currentDefinition.Version);

if (storedCurrentDefinition is null)
{
    await workflowDefinitionService.RegisterAsync(
        currentDefinition);

    storedCurrentDefinition =
        await workflowDefinitionService.ResolveAsync(
            currentDefinition.Id,
            currentDefinition.Version);
}

if (storedCurrentDefinition is null)
{
    throw new InvalidOperationException(
        $"Workflow definition '{currentDefinition.Id}' version '{currentDefinition.Version}' could not be resolved.");
}

if (args.Length > 1)
{
    var workflowId =
        new WorkflowId(args[1]);

    var execution =
        await workflowRepository.GetExecutionAsync(
            workflowId);

    if (execution is null)
    {
        throw new InvalidOperationException(
            $"Workflow execution '{workflowId}' was not found.");
    }

    var recoveryDefinition =
        await workflowDefinitionService.ResolveAsync(
            execution.DefinitionId,
            execution.DefinitionVersion);

    if (recoveryDefinition is null)
    {
        throw new InvalidOperationException(
            $"Workflow definition '{execution.DefinitionId}' version '{execution.DefinitionVersion}' was not found.");
    }

    await executionCoordinator.ExecuteRecoveryAsync(
        workflowId,
        recoveryDefinition);
}
else
{
    await executionCoordinator.ExecuteAsync(
        storedCurrentDefinition);
}

Console.WriteLine("======================================");
Console.WriteLine(" Execution Summary");
Console.WriteLine("======================================");
Console.WriteLine($"Discovered : {orchestration.Statistics.ItemsDiscovered}");
Console.WriteLine($"Handled    : {orchestration.Statistics.ItemsHandled}");
Console.WriteLine($"Skipped    : {orchestration.Statistics.ItemsSkipped}");
Console.WriteLine($"Completed  : {orchestration.Statistics.InventoriesCompleted}");
Console.WriteLine($"Failed     : {orchestration.Statistics.ItemsFailed}");
Console.WriteLine($"Elapsed    : {orchestration.Statistics.Elapsed}");

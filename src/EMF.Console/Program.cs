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

var executionCoordinator =
    new WorkflowExecutionCoordinator(
        workflowService,
        recoveryCoordinator,
        workflowRunner);

var workflowDefinition =
    new WorkflowDefinition
    {
        Id = "inventory-processing",
        Name = "Inventory Processing",
        Version = "1",
        ActivityIds = new[] { "inventory" }
    };

var inventoryActivity =
    new InventoryWorkflowActivity(
        orchestration,
        sourcePath,
        new DiscoveryOptions());

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

    var workflowContext =
        new WorkflowExecutionContext
        {
            WorkflowId = workflowId,
        };

    await executionCoordinator.ExecuteRecoveryAsync(
        workflowId,
        workflowDefinition,
        workflowContext,
        new[] { inventoryActivity });
}
else
{
    await executionCoordinator.ExecuteAsync(
        workflowDefinition,
        new[] { inventoryActivity });
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

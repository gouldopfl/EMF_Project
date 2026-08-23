using EMF.Security.Azure.Configuration;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Encryption;
using EMF.Security.Azure.Keys;
using EMF.Security.Storage;
using EMF.Persistence.Storage;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Discovery.Models;
using EMF.Discovery.Services;
using EMF.Inventory.Providers;
using EMF.Integrity;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;

namespace EMF.ConsoleApplication;

public static class InventoryConsoleCommand
{
    public static async Task<int> RunAsync(
        string[] args)
    {
        var sourcePath = args.Length > 0
            ? args[0]
            : "/opt/emf-lab/datasets/LD-VET-001/extracted";

        Console.WriteLine("======================================");
        Console.WriteLine(" EMF Discovery + Inventory");
        Console.WriteLine("======================================");
        Console.WriteLine();
        Console.WriteLine($"Source   : {sourcePath}");
        Console.WriteLine();

        var vaultUri = Environment.GetEnvironmentVariable("EMF_AZURE_KEY_VAULT_URI");
        var keyName = Environment.GetEnvironmentVariable("EMF_AZURE_KEY_NAME");
        var keyVersion = Environment.GetEnvironmentVariable("EMF_AZURE_KEY_VERSION");

        AzureEnvelopeEncryptionService? encryptionService = null;

        if (!string.IsNullOrWhiteSpace(vaultUri) &&
            !string.IsNullOrWhiteSpace(keyName))
        {
            var options = new AzureKeyVaultOptions
            {
                VaultUri = vaultUri,
                KeyName = keyName,
                KeyVersion = keyVersion
            };

            encryptionService = new AzureEnvelopeEncryptionService(
                new ConfiguredAzureKeyReferenceProvider(options),
                new AzureKeyCryptographyFactory(options));
        }

        IArtifactContentStore? contentStore = null;

        if (encryptionService is not null)
        {
            var contentPath =
                Environment.GetEnvironmentVariable("EMF_ARTIFACT_CONTENT_PATH");

            if (string.IsNullOrWhiteSpace(contentPath))
            {
                contentPath =
                    Path.Combine(
                        AppContext.BaseDirectory,
                        "artifact-content");
            }

            contentStore =
                new EncryptedArtifactContentStore(
                    new FileSystemArtifactContentStore(contentPath),
                    encryptionService);
        }

        var discovery = new FileSystemDiscoveryService();

        var routing = new InventoryRoutingService(
            new[] { new SqliteInventoryProvider() });

        var fingerprintService =
            new Sha256ContentFingerprintService();

        var orchestration = new InventoryOrchestrationService(
            discovery,
            routing,
            new ArtifactFactory(),
            new GuidArtifactIdGenerator(),
            fingerprintService,
            contentStore);


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

        var evidenceRepository =
            new SqliteEvidenceRepository(
                workflowDatabasePath);

        await evidenceRepository.InitializeAsync();

        var inventoryActivity =
            new InventoryWorkflowActivity(
                orchestration,
                new EvidencePersistenceService(evidenceRepository),
                fingerprintService,
                contentStore,
                sourcePath,
                new DiscoveryOptions());

        var inspectionActivity =
            new ArtifactInspectionWorkflowActivity(
                discovery,
                ArtifactInspectionFactory.Create(),
                evidenceRepository,
                fingerprintService,
                sourcePath,
                new DiscoveryOptions());

        var activities =
            new List<IWorkflowActivity>
            {
                inventoryActivity,
                inspectionActivity
            };

        var activityIds =
            new List<string>
            {
                "inventory",
                "artifact-inspection"
            };

        var definitionVersion = "2";

        if (contentStore is not null)
        {
            var extractionService =
                new EmailAttachmentExtractionService(
                    evidenceRepository,
                    contentStore,
                    fingerprintService,
                    new GuidArtifactIdGenerator(),
                    new ArtifactFactory());

            var processingService =
                new EmailAttachmentProcessingService(
                    new MimeKitEmailAttachmentDecoder(),
                    extractionService);

            activities.Add(
                new EmailMessageWorkflowActivity(
                    discovery,
                    evidenceRepository,
                    contentStore,
                    fingerprintService,
                    new GuidArtifactIdGenerator(),
                    new ArtifactFactory(),
                    sourcePath,
                    new DiscoveryOptions()));

            activities.Add(
                new EmailAttachmentWorkflowActivity(
                    evidenceRepository,
                    contentStore,
                    processingService));

            var outlookProcessingService =
                new OutlookAttachmentProcessingService(
                    new OutlookMessageDecoder(),
                    extractionService);

            activities.Add(
                new OutlookAttachmentWorkflowActivity(
                    evidenceRepository,
                    contentStore,
                    outlookProcessingService));

            var zipExtractionService =
                new ZipEntryExtractionService(
                    evidenceRepository,
                    contentStore,
                    fingerprintService,
                    new GuidArtifactIdGenerator(),
                    new ArtifactFactory());

            var zipProcessingService =
                new ZipArchiveProcessingService(
                    new ZipArchiveDecoder(),
                    zipExtractionService);

            activities.Add(
                new ZipArchiveWorkflowActivity(
                    evidenceRepository,
                    contentStore,
                    zipProcessingService));

            activityIds.Add("email-messages");
            activityIds.Add("email-attachments");
            activityIds.Add("outlook-attachments");
            activityIds.Add("zip-archives");
            definitionVersion = "5";
        }

        var activityResolver =
            new WorkflowActivityResolver(activities);

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
                Version = definitionVersion,
                ActivityIds = activityIds
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

        return 0;
    }
}

using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;

namespace EMF.ConsoleApplication;

public static class VeteransConsoleCommand
{
    public static async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length != 5 ||
            args[0] != "evidence" ||
            args[1] != "develop")
        {
            ShowUsage();
            return 2;
        }

        var databasePath =
            Path.GetFullPath(args[2]);

        if (!File.Exists(databasePath))
        {
            global::System.Console.Error.WriteLine(
                $"Veterans Claims database not found: {databasePath}");

            return 2;
        }

        var planId =
            new EvidenceDevelopmentPlanId(args[3]);

        var evidenceGapId =
            new EvidenceGapId(args[4]);

        var workflowRepository =
            new SqliteWorkflowRepository(databasePath);

        await workflowRepository.InitializeAsync();

        var workflowService =
            new WorkflowService(workflowRepository);

        var workflowRunner =
            new WorkflowRunner(workflowService);

        var developmentRepository =
            new SqliteEvidenceDevelopmentPlanRepository(
                databasePath);

        await developmentRepository.InitializeAsync();

        var gapRepository =
            new SqliteEvidenceGapRepository(databasePath);

        var guidanceRepository =
            new SqliteEvidenceRequirementGuidanceRepository(
                databasePath);

        var coordinator =
            new EvidenceDevelopmentWorkflowCoordinator(
                workflowService,
                developmentRepository,
                workflowRunner,
                gapRepository,
                guidanceRepository);

        try
        {
            var execution =
                await coordinator.StartAsync(
                    planId,
                    evidenceGapId);

            var result =
                await developmentRepository
                    .GetEvidenceDevelopmentResultAsync(
                        evidenceGapId);

            if (result is null)
            {
                global::System.Console.Error.WriteLine(
                    "Evidence development completed without a persisted result.");

                return 1;
            }

            global::System.Console.WriteLine(
                $"Workflow ID : {execution.WorkflowId.Value}");

            global::System.Console.WriteLine(
                $"Plan ID     : {execution.EvidenceDevelopmentPlanId.Value}");

            global::System.Console.WriteLine(
                $"Evidence Gap: {result.EvidenceGapId.Value}");

            global::System.Console.WriteLine(
                $"Requirement : {result.RequirementId.Value}");

            global::System.Console.WriteLine(
                $"Guidance    : {result.EvidenceGuidance.Count}");

            return 0;
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine(
                $"Evidence development failed: {ex.Message}");

            return 1;
        }
    }

    private static void ShowUsage()
    {
        global::System.Console.WriteLine(
            "Usage: emf veterans evidence develop " +
            "<database-path> <plan-id> <evidence-gap-id>");
    }
}

using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;

namespace EMF.ConsoleApplication;

public static class VeteransConsoleCommand
{
    public static Task<int> RunAsync(
        string[] args)
    {
        return RunAsync(
            args,
            TextSummarizationConsoleRuntimeFactory.CreateAsync);
    }

    internal static async Task<int> RunAsync(
        string[] args,
        Func<Task<TextSummarizationConsoleRuntime>> runtimeFactory)
    {
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        var summarize =
            args.Length == 6 &&
            args[0] == "evidence" &&
            args[1] == "develop" &&
            args[2] == "--summarize";

        if ((!summarize && args.Length != 5) ||
            args[0] != "evidence" ||
            args[1] != "develop")
        {
            ShowUsage();
            return 2;
        }

        var offset = summarize ? 1 : 0;

        var databasePath =
            Path.GetFullPath(args[2 + offset]);

        if (!File.Exists(databasePath))
        {
            global::System.Console.Error.WriteLine(
                $"Veterans Claims database not found: {databasePath}");

            return 2;
        }

        var planId =
            new EvidenceDevelopmentPlanId(args[3 + offset]);

        var evidenceGapId =
            new EvidenceGapId(args[4 + offset]);

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

            if (summarize)
            {
                var runtime =
                    await runtimeFactory();

                var intelligenceCoordinator =
                    new EvidenceDevelopmentIntelligenceCoordinator(
                        developmentRepository,
                        gapRepository,
                        new EvidenceDevelopmentIntelligenceService(
                            runtime.TextSummarizationCapabilityExecutor));

                var intelligenceResult =
                    await intelligenceCoordinator.SummarizeAsync(
                        planId,
                        evidenceGapId,
                        new IntelligenceExecutionContext(
                            runtime.SubjectId,
                            new IntelligenceCorrelationId(
                                $"veterans-{Guid.NewGuid():N}"),
                            runtime.ClassificationId,
                            Array.Empty<ArtifactId>()));

                if (!intelligenceResult.Success)
                {
                    global::System.Console.Error.WriteLine(
                        intelligenceResult.Message ??
                        "Evidence development summarization failed.");

                    return 1;
                }

                global::System.Console.WriteLine();
                global::System.Console.WriteLine("Summary");
                global::System.Console.WriteLine("-------");
                global::System.Console.WriteLine(
                    intelligenceResult.Output);
            }

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
            "[--summarize] <database-path> <plan-id> <evidence-gap-id>");
    }
}

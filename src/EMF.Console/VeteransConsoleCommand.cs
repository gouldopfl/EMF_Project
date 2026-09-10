using EMF.Common;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Integrity;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Services;
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
        Func<Task<TextSummarizationConsoleRuntime>> runtimeFactory,
        Func<IArtifactContentStore?>? contentStoreFactory = null)
    {
        ArgumentNullException.ThrowIfNull(runtimeFactory);

        contentStoreFactory ??=
            ArtifactContentStoreFactory.Create;

        if (args.Length == 3 &&
            args[0] == "schema" &&
            args[1] == "migrate")
        {
            var path = Path.GetFullPath(args[2]);

            if (!File.Exists(path))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {path}");
                return 2;
            }

            await new VeteransClaimsSqliteSchema(path)
                .InitializeAsync();

            global::System.Console.WriteLine(
                "Veterans Claims schema migration complete.");

            return 0;
        }

        if (args.Length == 4 &&
            args[0] == "adjudication" &&
            args[1] == "assess")
        {
            var adjudicationDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(adjudicationDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {adjudicationDatabasePath}");

                return 2;
            }

            return await RunAdjudicationAssessmentAsync(
                adjudicationDatabasePath,
                new ClaimIssueId(args[3]),
                global::System.Console.Out);
        }

        if (args.Length == 4 &&
            args[0] == "adjudication" &&
            args[1] == "claim")
        {
            var adjudicationClaimDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(adjudicationClaimDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {adjudicationClaimDatabasePath}");

                return 2;
            }

            return await RunClaimAdjudicationAssessmentAsync(
                adjudicationClaimDatabasePath,
                new ClaimId(args[3]),
                global::System.Console.Out);
        }

        if (args.Length == 4 &&
            args[0] == "decision" &&
            args[1] == "history")
        {
            var decisionHistoryDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(decisionHistoryDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {decisionHistoryDatabasePath}");

                return 2;
            }

            return await RunDecisionHistoryAsync(
                decisionHistoryDatabasePath,
                new ClaimId(args[3]),
                global::System.Console.Out);
        }

        if (args.Length == 4 &&
            args[0] == "decision" &&
            args[1] == "review")
        {
            var decisionReviewDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(decisionReviewDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {decisionReviewDatabasePath}");

                return 2;
            }

            return await RunDecisionReviewAsync(
                decisionReviewDatabasePath,
                new ClaimIssueId(args[3]),
                global::System.Console.Out);
        }

        if (args.Length == 4 &&
            args[0] == "decision" &&
            args[1] == "interpret")
        {
            var decisionDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(decisionDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {decisionDatabasePath}");

                return 2;
            }

            return await RunDecisionInterpretAsync(
                decisionDatabasePath,
                new ArtifactId(args[3]),
                runtimeFactory,
                ArtifactContentStoreFactory.Create(),
                global::System.Console.Out);
        }

        if (args.Length == 7 &&
            args[0] == "condition" &&
            args[1] == "service-connected")
        {
            var conditionDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(conditionDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {conditionDatabasePath}");

                return 2;
            }

            return await RunAddServiceConnectedConditionAsync(
                conditionDatabasePath,
                new VeteranId(args[3]),
                new ServiceConnectionBasisId(args[4]),
                new MedicalConditionId(args[5]),
                args[6],
                global::System.Console.Out);
        }

        if (args.Length == 14 &&
            args[0] == "regulatory" &&
            args[1] == "requirement")
        {
            var regulatoryDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(regulatoryDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {regulatoryDatabasePath}");

                return 2;
            }

            return await RunAddRegulatoryRequirementAsync(
                regulatoryDatabasePath,
                new ServiceConnectionBasisId(args[3]),
                new RegulatoryAuthorityId(args[4]),
                args[5],
                args[6],
                new RegulatoryProvisionId(args[7]),
                args[8],
                args[9],
                args[10],
                args[11],
                new RequirementId(args[12]),
                args[13],
                global::System.Console.Out);
        }

        if (args.Length == 4 &&
            args[0] == "evidence" &&
            args[1] == "ingest")
        {
            var ingestDatabasePath =
                Path.GetFullPath(args[2]);

            var ingestSourcePath =
                Path.GetFullPath(args[3]);

            if (!File.Exists(ingestDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {ingestDatabasePath}");

                return 2;
            }

            if (!File.Exists(ingestSourcePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Evidence file not found: {ingestSourcePath}");

                return 2;
            }

            return await RunEvidenceIngestAsync(
                ingestDatabasePath,
                ingestSourcePath,
                ArtifactContentStoreFactory.Create(),
                global::System.Console.Out);
        }

        if (args.Length == 8 &&
            args[0] == "evidence" &&
            args[1] == "clinical-note")
        {
            var clinicalNoteDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(clinicalNoteDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: " +
                    $"{clinicalNoteDatabasePath}");
                return 2;
            }

            if (!int.TryParse(args[4], out var startPage) ||
                !int.TryParse(args[5], out var endPage) ||
                startPage <= 0 ||
                endPage < startPage)
            {
                global::System.Console.Error.WriteLine(
                    "Clinical note page range is invalid.");
                return 2;
            }

            if (!DateOnly.TryParseExact(
                    args[6],
                    "yyyy-MM-dd",
                    out var noteDate))
            {
                global::System.Console.Error.WriteLine(
                    "Clinical note date must use yyyy-MM-dd.");
                return 2;
            }

            return await RunEvidenceClinicalNoteAsync(
                clinicalNoteDatabasePath,
                new ArtifactId(args[3]),
                startPage,
                endPage,
                noteDate,
                args[7],
                contentStoreFactory(),
                global::System.Console.Out);
        }

        if (args.Length == 4 &&
            args[0] == "evidence" &&
            args[1] == "claim")
        {
            var claimDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(claimDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {claimDatabasePath}");

                return 2;
            }

            return await RunClaimEvidenceAsync(
                claimDatabasePath,
                new ClaimId(args[3]));
        }

        if (args.Length == 4 &&
            args[0] == "evidence" &&
            args[1] == "checklist")
        {
            var checklistDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(checklistDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {checklistDatabasePath}");

                return 2;
            }

            return await RunChecklistAsync(
                checklistDatabasePath,
                new ClaimIssueId(args[3]));
        }

        if (args.Length == 7 &&
            args[0] == "evidence" &&
            args[1] == "guidance")
        {
            var guidanceDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(guidanceDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {guidanceDatabasePath}");

                return 2;
            }

            return await RunGuidanceAsync(
                guidanceDatabasePath,
                new RequirementId(args[3]),
                args[4],
                args[5],
                args[6]);
        }

        if (args.Length == 8 &&
            args[0] == "evidence" &&
            args[1] == "literature" &&
            args[2] == "link")
        {
            var literatureLinkDatabasePath =
                Path.GetFullPath(args[3]);

            if (!File.Exists(literatureLinkDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {literatureLinkDatabasePath}");

                return 2;
            }

            return await MedicalLiteratureConsoleCommand.RunLinkAsync(
                literatureLinkDatabasePath,
                new RequirementId(args[4]),
                new MedicalLiteratureSourceId(args[5]),
                args[6],
                args[7]);
        }

        if (args.Length == 5 &&
            args[0] == "evidence" &&
            args[1] == "literature" &&
            args[2] == "source")
        {
            var literatureDatabasePath =
                Path.GetFullPath(args[3]);

            if (!File.Exists(literatureDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {literatureDatabasePath}");

                return 2;
            }

            var sourcePath =
                Path.GetFullPath(args[4]);

            if (!File.Exists(sourcePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Medical literature source file not found: {sourcePath}");

                return 2;
            }

            return await MedicalLiteratureConsoleCommand.RunSourceAsync(
                literatureDatabasePath,
                sourcePath);
        }

        if (args.Length == 6 &&
            args[0] == "evidence" &&
            args[1] == "classify")
        {
            var classifyDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(classifyDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {classifyDatabasePath}");

                return 2;
            }

            return await RunClassifyAsync(
                classifyDatabasePath,
                new ClaimIssueId(args[3]),
                new ArtifactId(args[4]),
                args[5]);
        }

        if (args.Length == 4 &&
            args[0] == "evidence" &&
            args[1] == "package")
        {
            var packageDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(packageDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {packageDatabasePath}");

                return 2;
            }

            return await RunEvidencePackageAsync(
                packageDatabasePath,
                new EvidencePackageId(args[3]),
                global::System.Console.Out);
        }

        if (args.Length == 5 &&
            args[0] == "evidence" &&
            args[1] == "package")
        {
            var packageDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(packageDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {packageDatabasePath}");

                return 2;
            }

            return await RunEvidencePackageDocxAsync(
                packageDatabasePath,
                new EvidencePackageId(args[3]),
                args[4]);
        }

        if (args.Length == 4 &&
            args[0] == "evidence" &&
            args[1] == "plan")
        {
            var planDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(planDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {planDatabasePath}");

                return 2;
            }

            return await RunEvidenceDevelopmentPlanAsync(
                planDatabasePath,
                new EvidenceDevelopmentPlanId(args[3]),
                global::System.Console.Out);
        }

        if (args.Length == 5 &&
            args[0] == "evidence" &&
            args[1] == "prepare")
        {
            var prepareDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(prepareDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {prepareDatabasePath}");

                return 2;
            }

            return await RunPrepareAsync(
                prepareDatabasePath,
                new ClaimIssueId(args[3]),
                new EvidenceDevelopmentPlanId(args[4]));
        }

        if (args.Length == 4 &&
            args[0] == "evidence" &&
            args[1] == "execute")
        {
            var executeDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(executeDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {executeDatabasePath}");

                return 2;
            }

            return await RunExecuteAsync(
                executeDatabasePath,
                new EvidenceDevelopmentPlanId(args[3]));
        }

        if ((args.Length is 4 or 5 ||
             (args.Length is 6 or 7 && args[4] == "--basis")) &&
            args[0] == "evidence" &&
            args[1] == "reviewer")
        {
            var reviewerDatabasePath =
                Path.GetFullPath(args[2]);

            if (!File.Exists(reviewerDatabasePath))
            {
                global::System.Console.Error.WriteLine(
                    $"Veterans Claims database not found: {reviewerDatabasePath}");

                return 2;
            }

            if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    "EMF_REVIEWED_BY")))
            {
                global::System.Console.Error.WriteLine(
                    "Evidence promotion requires review. " +
                    "Set EMF_REVIEWED_BY to the reviewer identity.");

                return 1;
            }

            var reviewerBasisId =
                args.Length is 6 or 7
                    ? args[5]
                    : null;

            var reviewerOutputPath =
                args.Length == 5
                    ? Path.GetFullPath(args[4])
                    : args.Length == 7
                        ? Path.GetFullPath(args[6])
                        : null;

            return await RunReviewerPackageAsync(
                reviewerDatabasePath,
                new ClaimIssueId(args[3]),
                runtimeFactory,
                contentStoreFactory,
                reviewerOutputPath,
                reviewerBasisId);
        }

        var summarize =
            args.Length >= 6 &&
            args[0] == "evidence" &&
            args[1] == "develop" &&
            args[2] == "--summarize";

        var promote =
            summarize &&
            args.Length == 7 &&
            args[3] == "--promote";

        if ((!summarize && args.Length != 5) ||
            (summarize && !promote && args.Length != 6) ||
            (summarize && promote && args.Length != 7) ||
            args[0] != "evidence" ||
            args[1] != "develop")
        {
            ShowUsage();
            return 2;
        }

        var offset =
            summarize
                ? promote ? 2 : 1
                : 0;

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

        if (promote &&
            string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    "EMF_REVIEWED_BY")))
        {
            global::System.Console.Error.WriteLine(
                "Evidence promotion requires review. " +
                "Set EMF_REVIEWED_BY to the reviewer identity.");

            return 1;
        }

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

        var contentStore =
            ArtifactContentStoreFactory.Create();

        IEvidenceDevelopmentWorkflowCoordinator coordinator;

        if (contentStore is null)
        {
            coordinator =
                VeteransEvidenceOrchestrationFactory
                    .CreateEvidenceDevelopmentWorkflowCoordinator(
                        workflowService,
                        developmentRepository,
                        workflowRunner,
                        gapRepository,
                        guidanceRepository);
        }
        else
        {
            var evidenceRepository =
                new SqliteEvidenceRepository(databasePath);

            await evidenceRepository.InitializeAsync();

            var textExtractor =
                ArtifactTextExtractionFactory.Create(
                    evidenceRepository,
                    contentStore);

            var recognitionTerms =
                new SqliteEvidenceRecognitionTermRepository(
                    databasePath);

            var classifications =
                new SqliteEvidenceClassificationRepository(
                    databasePath);

            var classificationService =
                new EvidenceClassificationService(
                    classifications,
                    new GuidIdGenerator());

            coordinator =
                VeteransEvidenceOrchestrationFactory
                    .CreateEvidenceDevelopmentWorkflowCoordinator(
                        workflowService,
                        developmentRepository,
                        workflowRunner,
                        gapRepository,
                        guidanceRepository,
                        textExtractor,
                        recognitionTerms,
                        classificationService);
        }

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
                    VeteransEvidenceOrchestrationFactory.CreateEvidenceDevelopmentIntelligenceCoordinator(
                        developmentRepository,
                        gapRepository,
                        runtime.TextSummarizationCapabilityExecutor);

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
                        ConsoleTextSanitizer.Sanitize(
                            intelligenceResult.Message ??
                            "Evidence development summarization failed."));

                    return 1;
                }

                global::System.Console.WriteLine();
                global::System.Console.WriteLine("Summary");
                global::System.Console.WriteLine("-------");
                global::System.Console.WriteLine(
                    ConsoleTextSanitizer.Sanitize(
                        intelligenceResult.Output));

                if (promote)
                {
                    var plan =
                        await developmentRepository
                            .GetEvidenceDevelopmentPlanAsync(planId);

                    if (plan is null)
                    {
                        global::System.Console.Error.WriteLine(
                            $"Evidence development plan not found: {planId.Value}");
                        return 1;
                    }

                    var prepared =
                        await VeteransReviewerPackagePublisher.PublishAsync(
                            databasePath,
                            plan.ClaimIssueId,
                            "Physician reviewer package",
                            "MedicalProfessional",
                            $"Evidence gap {evidenceGapId.Value} summary",
                            runtime.SubjectId,
                            Environment.GetEnvironmentVariable("EMF_REVIEWED_BY")!,
                            DateTimeOffset.UtcNow,
                            evidenceGapId,
                            result.RequirementId,
                            intelligenceResult);

                    global::System.Console.WriteLine();
                    global::System.Console.WriteLine("Promoted and Packaged");
                    global::System.Console.WriteLine("---------------------");
                    global::System.Console.WriteLine(
                        $"Summary Artifact ID : {prepared.SummaryArtifact.Id.Value}");
                    global::System.Console.WriteLine(
                        $"Package ID          : {prepared.Package.Id.Value}");
                }

            }

            return 0;
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    $"Evidence development failed: {ex.Message}"));

            return 1;
        }
    }

    internal static async Task<int>
        RunClaimAdjudicationAssessmentAsync(
            string databasePath,
            ClaimId claimId,
            TextWriter output)
    {
        var claims =
            new SqliteClaimRepository(databasePath);

        var issues =
            new SqliteClaimIssueRepository(databasePath);

        var service =
            new ClaimAdjudicationAssessmentService(
                claims,
                issues,
                CreateAdjudicationAssessmentService(
                    databasePath));

        var timeline =
            new ClaimAdjudicationTimelineService(
                issues,
                new ClaimIssueAdjudicationTimelineService(
                    new ClaimIssueAdjudicationLifecycleService(
                        new SqliteVaDecisionRepository(databasePath),
                        new SqliteSubmissionRepository(databasePath)),
                    new SqliteClaimIssueCourtAppealRepository(
                        databasePath)));

        var result =
            await service.GetAsync(claimId);

        if (result is null)
        {
            global::System.Console.Error.WriteLine(
                $"Claim not found: {claimId.Value}");

            return 1;
        }

        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine(
            $"Claim       : {result.Claim.Id.Value}");

        output.WriteLine(
            $"Issues      : {result.IssueCount}");

        output.WriteLine(
            $"Ready       : {result.ReadyIssueCount}");

        output.WriteLine(
            $"Blocked     : {result.BlockedIssueCount}");

        output.WriteLine(
            $"Recommended : {result.RecommendedIssueCount}");

        output.WriteLine(
            $"Decisions   : {result.CurrentDecisionCount}");

        output.WriteLine(
            $"Denied      : {result.DeniedIssueCount}");

        output.WriteLine(
            $"Granted     : {result.GrantedIssueCount}");

        output.WriteLine(
            $"Deferred    : {result.DeferredIssueCount}");

        output.WriteLine(
            $"Partial     : {result.PartiallyGrantedIssueCount}");

        output.WriteLine(
            $"Undecided   : {result.UndecidedIssueCount}");

        output.WriteLine(
            $"Reviews     : {result.ReviewedDecisionCount}");

        output.WriteLine(
            $"Review Req  : {result.ReviewRequiredCount}");

        output.WriteLine(
            $"Attention   : {result.RequiresAttention} " +
            $"({result.AttentionIssueCount} issue(s))");

        output.WriteLine(
            $"Follow Up   : {result.ShouldConsiderFollowUp} " +
            $"({result.FollowUpIssueCount} issue(s))");

        foreach (var issue in result.Issues)
        {
            output.WriteLine(
                $"Issue       : " +
                $"{issue.Details.ClaimIssue.Id.Value}");

            output.WriteLine(
                $"  Ready     : " +
                $"{issue.Readiness.IsReadyForAdjudication}");

            output.WriteLine(
                $"  Attention : " +
                $"{issue.RequiresAttention}");

            output.WriteLine(
                $"  Follow Up : " +
                $"{issue.ShouldConsiderFollowUp}");

            output.WriteLine(
                $"  Merits    : " +
                $"{issue.Merits?.Outcome ?? "None"}");

            output.WriteLine(
                $"  Recommend : " +
                $"{issue.Recommendation?.RecommendedOutcome ?? "None"}");

            output.WriteLine(
                $"  Current   : " +
                $"{issue.CurrentDecision?.IssueDecision.Outcome ?? "None"}");

            output.WriteLine(
                $"  Reviews   : " +
                $"{issue.DecisionReviewHistory.Count}");

            output.WriteLine(
                $"  Review Req: " +
                $"{issue.DecisionReviewHistory.Count(
                    x => x.Review.RequiresReview)}");
        }

        var timelineEvents =
            await timeline.GetAsync(claimId);

        output.WriteLine(
            $"Timeline    : {timelineEvents.Count}");

        foreach (var item in timelineEvents)
        {
            output.WriteLine(
                $"- {item.OccurredAt:O} " +
                $"{item.ClaimIssueId.Value} " +
                $"{item.EventType}" +
                (item.Outcome is null
                    ? string.Empty
                    : $" [{item.Outcome}]") +
                (item.Description is null
                    ? string.Empty
                    : $": {item.Description}"));
        }

        return 0;
    }


    private static ClaimIssueDecisionReviewHistoryService
        CreateReviewHistoryService(
            string databasePath)
    {
        var repository =
            new SqliteVaDecisionRepository(databasePath);

        return new ClaimIssueDecisionReviewHistoryService(
            new ClaimIssueDecisionComparisonHistoryService(
                repository,
                new ClaimIssueDecisionComparisonService()),
            new ClaimIssueDecisionReviewService(),
            new ClaimIssueDecisionReviewAnalysisService());
    }


    private static async Task<int> RunReviewerPackageAsync(
        string databasePath,
        ClaimIssueId claimIssueId,
        Func<Task<TextSummarizationConsoleRuntime>> runtimeFactory,
        Func<IArtifactContentStore?> contentStoreFactory,
        string? outputPath,
        string? basisId = null)
    {
        var details =
            await CreateAdjudicationDetailsService(databasePath)
                .GetAsync(claimIssueId);

        if (details is null)
        {
            global::System.Console.Error.WriteLine(
                $"Claim issue not found: {claimIssueId.Value}");

            return 1;
        }

        var classifications =
            await new SqliteEvidenceClassificationRepository(
                    databasePath)
                .GetEvidenceClassificationsAsync(claimIssueId);

        var sourceArtifactIds =
            classifications
                .Select(x => x.ArtifactId)
                .Distinct()
                .ToArray();

        if (sourceArtifactIds.Length == 0)
        {
            global::System.Console.Error.WriteLine(
                $"No classified evidence found for claim issue: {claimIssueId.Value}");

            return 1;
        }

        var contentStore =
            contentStoreFactory();

        if (contentStore is null)
            throw new InvalidOperationException(
                "Artifact content store is not configured.");

        var evidenceRepository =
            new SqliteEvidenceRepository(databasePath);

        await evidenceRepository.InitializeAsync();

        var textExtractor =
            ArtifactTextExtractionFactory.Create(
                evidenceRepository,
                contentStore);

        var evidenceSources =
            new List<VeteransReviewerEvidenceSource>();

        foreach (var group in
                 classifications.GroupBy(x => x.ArtifactId))
        {
            var artifact =
                await evidenceRepository.GetArtifactAsync(group.Key)
                ?? throw new InvalidOperationException(
                    $"Reviewer evidence artifact not found: {group.Key.Value}");

            if (artifact.Id != group.Key)
                throw new InvalidOperationException(
                    "Reviewer evidence artifact identity mismatch.");

            var text =
                await textExtractor.ExtractTextAsync(group.Key);

            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException(
                    $"Unable to extract reviewer evidence: {group.Key.Value}");

            evidenceSources.Add(
                new VeteransReviewerEvidenceSource
                {
                    ArtifactId = group.Key,
                    ArtifactName = artifact.Name,
                    ArtifactType = artifact.ArtifactType,
                    ContentRole =
                        EvidencePackageContentRoles.UnderlyingEvidence,
                    Classifications =
                        group.Select(x => x.Classification)
                            .Distinct(StringComparer.Ordinal)
                            .ToArray(),
                    Text = text
                });
        }

        var developmentRepository =
            new SqliteEvidenceDevelopmentPlanRepository(
                databasePath);

        await developmentRepository.InitializeAsync();

        var gapRepository =
            new SqliteEvidenceGapRepository(databasePath);

        var developmentDetails =
            await new VeteransReviewerEvidenceDevelopmentDetailsService(
                    developmentRepository,
                    gapRepository)
                .GetAsync(claimIssueId);

        if (!string.IsNullOrWhiteSpace(basisId))
        {
            try
            {
                var scope =
                    await new VeteransReviewerPackageBasisScopeService(
                            developmentRepository,
                            gapRepository)
                        .ScopeAsync(
                            details,
                            developmentDetails,
                            new ServiceConnectionBasisId(basisId));

                details = scope.Details;
                developmentDetails = scope.DevelopmentDetails;
            }
            catch (InvalidOperationException ex)
            {
                global::System.Console.Error.WriteLine(
                    ConsoleTextSanitizer.Sanitize(ex.Message));
                return 1;
            }
        }

        var runtime =
            await runtimeFactory();

        var intelligence =
            VeteransEvidenceOrchestrationFactory
                .CreateReviewerPackageIntelligenceService(
                    runtime.TextSummarizationCapabilityExecutor);

        var result =
            await intelligence.SummarizeAsync(
                details,
                evidenceSources,
                developmentDetails,
                new IntelligenceExecutionContext(
                    runtime.SubjectId,
                    new IntelligenceCorrelationId(
                        $"veterans-{Guid.NewGuid():N}"),
                    runtime.ClassificationId,
                    sourceArtifactIds));

        if (!result.Success)
        {
            global::System.Console.Error.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    result.Message ??
                    "Reviewer package summarization failed."));

            return 1;
        }

        var prepared =
            await VeteransReviewerPackagePublisher.PublishAsync(
                databasePath,
                claimIssueId,
                "Physician reviewer package",
                "MedicalProfessional",
                $"Claim issue {claimIssueId.Value} reviewer summary",
                runtime.SubjectId,
                Environment.GetEnvironmentVariable(
                    "EMF_REVIEWED_BY")!,
                DateTimeOffset.UtcNow,
                result);

        global::System.Console.WriteLine(
            ConsoleTextSanitizer.Sanitize(
                result.Output));

        global::System.Console.WriteLine(
            $"Summary Artifact ID : {prepared.SummaryArtifact.Id.Value}");

        global::System.Console.WriteLine(
            $"Package ID          : {prepared.Package.Id.Value}");

        if (outputPath is null)
            return 0;

        var exportExitCode =
            await RunEvidencePackageDocxAsync(
                databasePath,
                prepared.Package.Id,
                outputPath,
                contentStore);

        if (exportExitCode != 0)
            return exportExitCode;

        global::System.Console.WriteLine(
            $"Package DOCX        : {outputPath}");

        return 0;
    }

    private static ClaimIssueAdjudicationDetailsService
        CreateAdjudicationDetailsService(
            string databasePath)
    {
        var issues =
            new SqliteClaimIssueRepository(databasePath);

        var serviceConnections =
            new SqliteServiceConnectionRepository(databasePath);

        var regulatory =
            new SqliteRegulatoryRepository(databasePath);

        var gaps =
            new SqliteEvidenceGapRepository(databasePath);

        var requirementEvidence =
            new RequirementEvidenceService(
                new SqliteEvidenceClassificationRepository(
                    databasePath),
                new SqliteEvidenceRequirementGuidanceRepository(
                    databasePath));

        var evidence =
            new ClaimIssueEvidenceDetailsService(
                issues,
                new ClaimIssueEvidenceChecklistService(
                    gaps,
                    requirementEvidence),
                new EvidenceDevelopmentPlanService(
                    new SqliteEvidenceDevelopmentPlanRepository(
                        databasePath),
                    gaps));

        var timeline =
            new ClaimIssueAdjudicationTimelineService(
                new ClaimIssueAdjudicationLifecycleService(
                    new SqliteVaDecisionRepository(databasePath),
                    new SqliteSubmissionRepository(databasePath)),
                new SqliteClaimIssueCourtAppealRepository(
                    databasePath));

        return new ClaimIssueAdjudicationDetailsService(
            issues,
            new SqliteConditionRepository(databasePath),
            serviceConnections,
            new SqliteServiceHistoryRepository(databasePath),
            regulatory,
            requirementEvidence,
            evidence,
            timeline,
            new SqliteMedicalOpinionRepository(databasePath),
            new SqliteMedicalLiteratureRepository(databasePath));
    }

    private static ClaimIssueAdjudicationAssessmentService
        CreateAdjudicationAssessmentService(
            string databasePath)
    {
        var serviceConnections =
            new SqliteServiceConnectionRepository(databasePath);

        var details =
            CreateAdjudicationDetailsService(databasePath);

        var merits =
            new ClaimIssueMeritsAssessmentService(
                serviceConnections,
                new SqliteFindingRepository(databasePath));

        var assessment =
            new ClaimIssueAdjudicationAssessmentService(
                details,
                new ClaimIssueAdjudicationReadinessService(),
                merits,
                new ClaimIssueDecisionRecommendationService(),
                new ClaimIssueCurrentDecisionService(
                    new SqliteVaDecisionRepository(databasePath)),
                CreateReviewHistoryService(databasePath),
                new ClaimIssueAdjudicationAgingStatusService(
                    new ClaimIssueAdjudicationAgingService(),
                    new ClaimIssueAdjudicationAgingPolicyService()),
                ClaimIssueAdjudicationAgingPolicies.Default,
                TimeProvider.System);


        return assessment;
    }


    internal static async Task<int> RunAdjudicationAssessmentAsync(
        string databasePath,
        ClaimIssueId claimIssueId,
        TextWriter output)
    {
        var assessment =
            CreateAdjudicationAssessmentService(databasePath);

        var result =
            await assessment.GetAsync(claimIssueId);

        if (result is null)
        {
            global::System.Console.Error.WriteLine(
                $"Claim issue not found: {claimIssueId.Value}");

            return 1;
        }

        ArgumentNullException.ThrowIfNull(output);

        output.WriteLine(
            $"Claim Issue : {result.Details.ClaimIssue.Id.Value}");

        foreach (var claimedCondition in result.Details.ClaimedConditions)
        {
            output.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    $"Claimed Condition: {claimedCondition.Id.Value} ({claimedCondition.Name})"));
        }

        output.WriteLine(
            $"Ready       : {result.Readiness.IsReadyForAdjudication}");

        output.WriteLine(
            $"Attention   : {result.RequiresAttention}");

        output.WriteLine(
            $"Follow Up   : {result.ShouldConsiderFollowUp}");

        output.WriteLine(
            $"Merits      : {result.Merits?.Outcome ?? "None"}");

        output.WriteLine(
            $"Recommend   : " +
            $"{result.Recommendation?.RecommendedOutcome ?? "None"}");

        output.WriteLine(
            $"Current     : " +
            $"{result.CurrentDecision?.IssueDecision.Outcome ?? "None"}");

        output.WriteLine(
            $"Review History: {result.DecisionReviewHistory.Count}");

        output.WriteLine(
            $"Evidence Checklists: " +
            $"{result.Details.Evidence.Checklist.RequirementChecklists.Count}");

        output.WriteLine(
            $"Evidence Outstanding: " +
            $"{result.Details.Evidence.Checklist.HasOutstandingItems}");

        output.WriteLine(
            $"Development Plans: " +
            $"{result.Details.Evidence.DevelopmentPlans.Count}");

        foreach (var review in result.DecisionReviewHistory)
        {
            output.WriteLine(
                $"- {review.Review.Comparison.ComparisonOutcome} " +
                $"Review={review.Review.RequiresReview}");
        }

        foreach (var theory in result.Details.ServiceConnectionTheories)
        {
            output.WriteLine(
                $"Theory      : {theory.TheoryType}");

            output.WriteLine(
                $"Theory ID   : {theory.Id.Value}");

            var theoryOutcome =
                result.Merits?.TheoryOutcomes.SingleOrDefault(
                    x => x.Theory.Id == theory.Id);

            if (theoryOutcome is not null)
            {
                output.WriteLine(
                    $"Outcome     : {theoryOutcome.Outcome}");
            }

            foreach (var basis in
                result.Details.ServiceConnectionBases.Where(
                    x => x.ServiceConnectionTheoryId == theory.Id))
            {
                output.WriteLine(
                    $"Basis       : {basis.Id.Value}");

                var basisOutcome =
                    theoryOutcome?.BasisOutcomes.SingleOrDefault(
                        x => x.Basis.Id == basis.Id);

                if (basisOutcome is not null)
                {
                    output.WriteLine(
                        $"Basis Result: {basisOutcome.Outcome}");
                }

                foreach (var condition in
                    result.Details.ServiceConnectedConditions.Where(
                        x => x.Basis.Id == basis.Id))
                {
                    output.WriteLine(
                        ConsoleTextSanitizer.Sanitize(
                            $"Service Connected: " +
                            $"{condition.ServiceConnectedCondition.Id.Value} " +
                            $"({condition.ServiceConnectedCondition.Name})"));
                }

                foreach (var serviceEvent in
                    result.Details.ServiceEvents.Where(
                        x => x.Basis.Id == basis.Id))
                {
                    output.WriteLine(
                        ConsoleTextSanitizer.Sanitize(
                            $"Service Event: " +
                            $"{serviceEvent.ServiceEvent.Id.Value} " +
                            $"({serviceEvent.ServiceEvent.Description})"));
                }

                foreach (var requirement in
                    result.Details.Requirements.Where(
                        x => x.Basis.Id == basis.Id))
                {
                    output.WriteLine(
                        $"Requirement : {requirement.Requirement.Id.Value}");

                    var requirementOutcome =
                        basisOutcome?.RequirementOutcomes.SingleOrDefault(
                            x =>
                                x.RequirementId ==
                                requirement.Requirement.Id);

                    if (requirementOutcome is not null)
                    {
                        output.WriteLine(
                            $"Req Result  : {requirementOutcome.Outcome}");
                    }

                    output.WriteLine(
                        ConsoleTextSanitizer.Sanitize(
                            $"Description : {requirement.Requirement.Description}"));

                    output.WriteLine(
                        $"Provision   : " +
                        $"{requirement.Requirement.RegulatoryProvisionId.Value}");

                    output.WriteLine(
                        ConsoleTextSanitizer.Sanitize(
                            $"Citation    : " +
                            $"{requirement.RegulatoryProvision.Citation}"));

                    output.WriteLine(
                        $"Evidence Matched : " +
                        $"{requirement.Responsiveness.MatchingItemCount}");

                    output.WriteLine(
                        $"Evidence Missing : " +
                        $"{requirement.Responsiveness.MissingItemCount}");

                    output.WriteLine(
                        $"Development Items: " +
                        $"{requirement.DevelopmentChecklist.Items.Count}");
                }
            }
        }

        output.WriteLine(
            $"Outstanding : {result.Readiness.OutstandingRequirementCount}");

        foreach (var blocking in
            result.Readiness.BlockingRequirements)
        {
            output.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    $"- {blocking.Requirement.Id.Value}: " +
                    blocking.Requirement.Description));
        }

        output.WriteLine(
            $"Outstanding Items: {result.Readiness.OutstandingItemCount}");

        foreach (var item in result.Readiness.BlockingItems)
        {
            output.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    $"  - {item.EvidenceClassification} / " +
                    $"{item.GuidanceRole}: {item.Description}"));
        }

        if (result.Aging is not null)
        {
            output.WriteLine(
                $"Pending Since: {result.Aging.Aging.PendingSince:O}");

            output.WriteLine(
                $"Age (Days)   : {result.Aging.Aging.AgeInDays}");

            output.WriteLine(
                $"Last Activity: " +
                $"{result.Aging.Aging.LastActivityAt:O}");

            output.WriteLine(
                $"Inactive Days: " +
                $"{result.Aging.Aging.DaysSinceLastActivity}");

            output.WriteLine(
                $"Aging Status : {result.Aging.AlertLevel}");
        }

        output.WriteLine(
            $"Timeline    : {result.Details.Timeline.Count}");

        foreach (var item in result.Details.Timeline)
        {
            output.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    $"- {item.OccurredAt:O} " +
                    $"{item.EventType}" +
                    (item.Outcome is null
                        ? string.Empty
                        : $" [{item.Outcome}]") +
                    (item.Description is null
                        ? string.Empty
                        : $": {item.Description}")));
        }

        return 0;
    }


    internal static async Task<int> RunEvidenceIngestAsync(
        string databasePath,
        string sourcePath,
        IArtifactContentStore? contentStore,
        TextWriter output)
    {
        if (contentStore is null)
        {
            global::System.Console.Error.WriteLine(
                "Artifact content store is not configured.");

            return 2;
        }

        var repository =
            new SqliteEvidenceRepository(databasePath);

        await repository.InitializeAsync();

        var service =
            new EvidenceFileIngestionService(
                repository,
                contentStore,
                new Sha256ContentFingerprintService(),
                new GuidArtifactIdGenerator(),
                new ArtifactFactory());

        try
        {
            var result =
                await service.IngestAsync(sourcePath);

            await output.WriteLineAsync(
                $"Artifact ID : {result.Artifact.Id.Value}");

            await output.WriteLineAsync(
                $"Status      : {(result.AlreadyExisted ? "Existing" : "Persisted")}");

            return 0;
        }
        catch (Exception ex)
        {
            global::System.Console.Error.WriteLine(
                $"Evidence ingestion failed: {ex.Message}");

            return 1;
        }
    }

    internal static async Task<int> RunEvidenceClinicalNoteAsync(
        string databasePath,
        ArtifactId parentArtifactId,
        int startPage,
        int endPage,
        DateOnly noteDate,
        string noteTitle,
        IArtifactContentStore? contentStore,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        if (contentStore is null)
        {
            global::System.Console.Error.WriteLine(
                "Artifact content store is not configured.");
            return 2;
        }

        if (string.IsNullOrWhiteSpace(noteTitle))
        {
            global::System.Console.Error.WriteLine(
                "Clinical note title must not be empty.");
            return 2;
        }

        var repository =
            new SqliteEvidenceRepository(databasePath);

        await repository.InitializeAsync();

        try
        {
#pragma warning disable CA1416
            var extractor =
                new PdfArtifactTextExtractionProvider(
                    contentStore,
                    new PdfToImagePageRenderer(),
                    new PaddleImageOcrService());
#pragma warning restore CA1416

            var text =
                await extractor.ExtractPageRangeTextAsync(
                    parentArtifactId,
                    startPage,
                    endPage);

            if (string.IsNullOrWhiteSpace(text))
            {
                global::System.Console.Error.WriteLine(
                    "Clinical note page range contains no extractable text.");
                return 1;
            }

            var bytes =
                global::System.Text.Encoding.UTF8.GetBytes(text);

            try
            {
                var service =
                    new VeteransClinicalNoteDerivationService(
                        repository,
                        contentStore,
                        new Sha256ContentFingerprintService(),
                        new GuidArtifactIdGenerator(),
                        new ArtifactFactory());

                var result =
                    await service.DeriveAsync(
                        parentArtifactId,
                        $"clinical-note-{noteDate:yyyy-MM-dd}-" +
                            $"{startPage}-{endPage}.txt",
                        startPage,
                        endPage,
                        noteDate,
                        noteTitle,
                        bytes);

                await output.WriteLineAsync(
                    $"Artifact ID : {result.Artifact.Id.Value}");
                await output.WriteLineAsync(
                    $"Parent ID   : {parentArtifactId.Value}");
                await output.WriteLineAsync(
                    $"Source Pages: {startPage}-{endPage}");
                await output.WriteLineAsync(
                    $"Note Date   : {noteDate:yyyy-MM-dd}");
                await output.WriteLineAsync(
                    $"Note Title  : {noteTitle}");

                return 0;
            }
            finally
            {
                global::System.Security.Cryptography
                    .CryptographicOperations.ZeroMemory(bytes);
            }
        }
        catch (Exception ex)
            when (ex is not OperationCanceledException)
        {
            global::System.Console.Error.WriteLine(
                $"Clinical note derivation failed: {ex.Message}");
            return 1;
        }
    }


    private static async Task<int> RunClaimEvidenceAsync(
        string databasePath,
        ClaimId claimId)
    {
        var claims =
            new SqliteClaimRepository(databasePath);

        var issues =
            new SqliteClaimIssueRepository(databasePath);

        var gaps =
            new SqliteEvidenceGapRepository(databasePath);

        var guidance =
            new SqliteEvidenceRequirementGuidanceRepository(
                databasePath);

        var classifications =
            new SqliteEvidenceClassificationRepository(
                databasePath);

        var requirements =
            new RequirementEvidenceService(
                classifications,
                guidance);

        var checklist =
            new ClaimIssueEvidenceChecklistService(
                gaps,
                requirements);

        var plans =
            new EvidenceDevelopmentPlanService(
                new SqliteEvidenceDevelopmentPlanRepository(
                    databasePath));

        var issueEvidence =
            new ClaimIssueEvidenceDetailsService(
                issues,
                checklist,
                plans);

        var service =
            new ClaimEvidenceDetailsService(
                claims,
                issues,
                issueEvidence);

        var details =
            await service.GetAsync(claimId);

        if (details is null)
        {
            global::System.Console.Error.WriteLine(
                $"Claim not found: {claimId.Value}");

            return 1;
        }

        foreach (var line in
            VeteransClaimEvidenceDetailsFormatter.Format(details))
        {
            global::System.Console.WriteLine(line);
        }

        return 0;
    }


    internal static async Task<int>
        RunAddRegulatoryRequirementAsync(
            string databasePath,
            ServiceConnectionBasisId basisId,
            RegulatoryAuthorityId authorityId,
            string authorityCitation,
            string authorityTitle,
            RegulatoryProvisionId provisionId,
            string provisionCitation,
            string version,
            string sourceUri,
            string sourceHash,
            RequirementId requirementId,
            string description,
            TextWriter output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorityCitation);
        ArgumentException.ThrowIfNullOrWhiteSpace(authorityTitle);
        ArgumentException.ThrowIfNullOrWhiteSpace(provisionCitation);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        ArgumentNullException.ThrowIfNull(output);

        var connections =
            new SqliteServiceConnectionRepository(databasePath);

        var basis =
            await connections.GetServiceConnectionBasisAsync(basisId);

        if (basis is null)
        {
            output.WriteLine(
                $"Service connection basis not found: {basisId.Value}");
            return 1;
        }

        var regulatory =
            new SqliteRegulatoryRepository(databasePath);

        var authority =
            await regulatory.GetRegulatoryAuthorityAsync(authorityId);

        var provision =
            await regulatory.GetRegulatoryProvisionAsync(provisionId);

        var requirement =
            await regulatory.GetRequirementAsync(requirementId);

        if (authority is not null &&
            (authority.AuthorityType != "Regulation" ||
             authority.Citation != authorityCitation ||
             authority.Title != authorityTitle))
        {
            output.WriteLine(
                "Regulatory authority ID already exists with different metadata.");
            return 1;
        }

        if (provision is not null &&
            (provision.RegulatoryAuthorityId.Value != authorityId.Value ||
             provision.ProvisionType !=
                 RegulatoryProvisionTypes.Requirement ||
             provision.Citation != provisionCitation ||
             provision.Version != version ||
             provision.SourceUri != sourceUri ||
             provision.SourceHash != sourceHash))
        {
            output.WriteLine(
                "Regulatory provision ID already exists with different metadata.");
            return 1;
        }

        if (requirement is not null &&
            (requirement.RegulatoryProvisionId.Value != provisionId.Value ||
             requirement.Description != description))
        {
            output.WriteLine(
                "Requirement ID already exists with different metadata.");
            return 1;
        }

        if (authority is null)
        {
            await regulatory.AddRegulatoryAuthorityAsync(
                new RegulatoryAuthority
                {
                    Id = authorityId,
                    AuthorityType = "Regulation",
                    Citation = authorityCitation,
                    Title = authorityTitle
                });
        }

        if (provision is null)
        {
            await regulatory.AddRegulatoryProvisionAsync(
                new RegulatoryProvision
                {
                    Id = provisionId,
                    RegulatoryAuthorityId = authorityId,
                    ProvisionType =
                        RegulatoryProvisionTypes.Requirement,
                    Citation = provisionCitation,
                    Version = version,
                    SourceUri = sourceUri,
                    SourceHash = sourceHash,
                    RetrievedUtc = DateTimeOffset.UtcNow
                });
        }

        if (requirement is null)
        {
            await regulatory.AddRequirementAsync(
                new Requirement
                {
                    Id = requirementId,
                    RegulatoryProvisionId = provisionId,
                    Description = description
                });
        }

        var requirementIds =
            await connections.GetRequirementIdsAsync(basisId);

        if (!requirementIds.Any(
                x => x.Value == requirementId.Value))
        {
            await connections.AddBasisRequirementAsync(
                new ServiceConnectionBasisRequirement
                {
                    ServiceConnectionBasisId = basisId,
                    RequirementId = requirementId
                });
        }

        output.WriteLine(
            $"Regulatory Provision : {provisionCitation}");
        output.WriteLine(
            $"Requirement          : {requirementId.Value}");
        output.WriteLine(
            $"Basis                : {basisId.Value}");
        output.WriteLine(
            "Status               : Associated");

        return 0;
    }

    internal static async Task<int>
        RunAddServiceConnectedConditionAsync(
            string databasePath,
            VeteranId veteranId,
            ServiceConnectionBasisId basisId,
            MedicalConditionId medicalConditionId,
            string conditionName,
            TextWriter output)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(conditionName);
        ArgumentNullException.ThrowIfNull(output);

        var connections =
            new SqliteServiceConnectionRepository(databasePath);

        var basis =
            await connections.GetServiceConnectionBasisAsync(basisId);

        if (basis is null)
        {
            output.WriteLine($"Service connection basis not found: {basisId.Value}");
            return 1;
        }

        var issue =
            await new SqliteClaimIssueRepository(databasePath)
                .GetClaimIssueAsync(basis.ClaimIssueId);

        var claim =
            issue is null
                ? null
                : await new SqliteClaimRepository(databasePath)
                    .GetClaimAsync(issue.ClaimId);

        if (claim is null ||
            claim.VeteranId.Value != veteranId.Value)
        {
            output.WriteLine(
                "Service connection basis does not belong to the veteran.");
            return 1;
        }

        var conditions =
            new SqliteConditionRepository(databasePath);

        var existingCondition =
            await conditions.GetMedicalConditionAsync(
                medicalConditionId);

        if (existingCondition is null)
        {
            await conditions.AddMedicalConditionAsync(
                new MedicalCondition
                {
                    Id = medicalConditionId,
                    Name = conditionName
                });
        }
        else if (!string.Equals(
                     existingCondition.Name,
                     conditionName,
                     StringComparison.Ordinal))
        {
            output.WriteLine(
                "Medical condition ID already exists with a different name.");
            return 1;
        }

        var veteranConditionIds =
            await conditions.GetMedicalConditionIdsAsync(
                veteranId);

        if (!veteranConditionIds.Any(
                x => x.Value == medicalConditionId.Value))
        {
            await conditions.AddVeteranMedicalConditionAsync(
                new VeteranMedicalCondition
                {
                    VeteranId = veteranId,
                    MedicalConditionId = medicalConditionId
                });
        }

        var basisConditionIds =
            await connections.GetServiceConnectedConditionIdsAsync(
                basisId);

        if (!basisConditionIds.Any(
                x => x.Value == medicalConditionId.Value))
        {
            await connections.AddBasisServiceConnectedConditionAsync(
                new ServiceConnectionBasisServiceConnectedCondition
                {
                    ServiceConnectionBasisId = basisId,
                    ServiceConnectedConditionId = medicalConditionId
                });
        }

        output.WriteLine(
            $"Medical Condition : {medicalConditionId.Value}");
        output.WriteLine(
            $"Veteran           : {veteranId.Value}");
        output.WriteLine(
            $"Basis             : {basisId.Value}");
        output.WriteLine(
            $"Status            : Service connected");

        return 0;
    }


    internal static async Task<int> RunDecisionReviewAsync(
        string databasePath,
        ClaimIssueId claimIssueId,
        TextWriter output)
    {
        var service =
            CreateAdjudicationAssessmentService(databasePath);

        var assessment =
            await service.GetAsync(claimIssueId);

        if (assessment is null)
        {
            output.WriteLine(
                $"Claim issue not found: {claimIssueId.Value}");

            return 1;
        }

        output.WriteLine(
            $"Claim Issue : {claimIssueId.Value}");

        output.WriteLine(
            $"Reviews     : {assessment.DecisionReviewHistory.Count}");

        foreach (var analysis in assessment.DecisionReviewHistory)
        {
            output.WriteLine();

            output.WriteLine(
                $"Issue Decision: " +
                $"{analysis.Review.Comparison.IssueDecision.Id.Value}");

            output.WriteLine(
                $"VA Decision : " +
                $"{analysis.Review.Comparison.IssueDecision.VaDecisionId.Value}");

            if (analysis.Review.Comparison.VaDecision is not null)
            {
                output.WriteLine(
                    $"Decision Date: " +
                    $"{analysis.Review.Comparison.VaDecision.DecisionDate:yyyy-MM-dd}");
            }

            output.WriteLine(
                $"VA Outcome  : " +
                $"{analysis.Review.Comparison.IssueDecision.Outcome}");

            output.WriteLine(
                $"EMF Merits  : " +
                $"{analysis.Review.Comparison.Recommendation.MeritsOutcome}");

            output.WriteLine(
                $"Recommend   : " +
                $"{analysis.Review.Comparison.Recommendation.RecommendedOutcome ?? "None"}");

            output.WriteLine(
                $"Comparison  : " +
                $"{analysis.Review.Comparison.ComparisonOutcome}");

            output.WriteLine(
                $"Needs Review: {analysis.Review.RequiresReview}");

            output.WriteLine(
                $"Merits      : {analysis.Merits.Outcome}");

            output.WriteLine(
                $"Contributing: " +
                $"{analysis.ContributingTheoryOutcomes.Count}");

            foreach (var theory in analysis.ContributingTheoryOutcomes)
            {
                output.WriteLine(
                    $"Theory      : {theory.Theory.TheoryType}");

                output.WriteLine(
                    $"Outcome     : {theory.Outcome}");

                foreach (var basis in theory.BasisOutcomes)
                {
                    output.WriteLine(
                        $"Basis       : {basis.Basis.Id.Value}");

                    output.WriteLine(
                        $"Basis Result: {basis.Outcome}");

                    foreach (var requirement in basis.RequirementOutcomes)
                    {
                        output.WriteLine(
                            $"Requirement : {requirement.RequirementId.Value}");

                        output.WriteLine(
                            $"Req Result  : {requirement.Outcome}");

                        foreach (var finding in requirement.Findings)
                        {
                            output.WriteLine(
                                $"Finding     : {finding.Id.Value}");

                            output.WriteLine(
                                $"Find Result : {finding.Outcome}");

                            output.WriteLine(
                                $"Description : {finding.Description}");
                        }
                    }
                }
            }
        }

        return 0;
    }


    internal static async Task<int> RunDecisionHistoryAsync(
        string databasePath,
        ClaimId claimId,
        TextWriter output)
    {
        var repository =
            new SqliteVaDecisionDocumentProcessingAttemptRepository(
                databasePath);

        var service =
            new VaDecisionDocumentProcessingHistoryService(
                repository);

        var history =
            await service.GetAsync(claimId);

        foreach (var entry in history)
        {
            output.WriteLine(
                $"Artifact    : {entry.ArtifactId.Value}");

            output.WriteLine(
                $"Processed   : {entry.ProcessedAt:u}");

            output.WriteLine(
                $"Persisted   : {entry.Persisted}");

            if (entry.VaDecisionId is not null)
            {
                output.WriteLine(
                    $"VA Decision : {entry.VaDecisionId.Value}");
            }

            output.WriteLine(
                $"Matched     : {entry.MatchedIssueCount}");

            output.WriteLine(
                $"Unmatched   : {entry.UnmatchedIssueCount}");

            output.WriteLine(
                $"Ambiguous   : {entry.AmbiguousIssueCount}");

            output.WriteLine(
                $"Unresolved  : {entry.HasUnresolvedIssues}");

            output.WriteLine();
        }

        return 0;
    }


    internal static async Task<int> RunDecisionInterpretAsync(
        string databasePath,
        ArtifactId artifactId,
        Func<Task<TextSummarizationConsoleRuntime>> runtimeFactory,
        IArtifactContentStore? contentStore,
        TextWriter output)
    {
        var evidenceRepository =
            new SqliteEvidenceRepository(databasePath);

        await evidenceRepository.InitializeAsync();

        if (contentStore is null)
        {
            global::System.Console.Error.WriteLine(
                "Artifact content store is not configured.");

            return 2;
        }

        var textExtractor =
            ArtifactTextExtractionFactory.Create(
                evidenceRepository,
                contentStore);

        var text =
            await textExtractor.ExtractTextAsync(
                artifactId);

        if (string.IsNullOrWhiteSpace(text))
        {
            global::System.Console.Error.WriteLine(
                $"No text could be extracted from artifact: {artifactId.Value}");

            return 1;
        }

        var runtime =
            await runtimeFactory();

        var coordinator =
            VeteransEvidenceOrchestrationFactory
                .CreateVaDecisionDocumentInterpretationCoordinator(
                    textExtractor,
                    runtime.TextStructuredExtractionCapabilityExecutor);

        var result =
            await coordinator.InterpretAsync(
                artifactId,
                new IntelligenceExecutionContext(
                    runtime.SubjectId,
                    new IntelligenceCorrelationId(
                        $"veterans-decision-{Guid.NewGuid():N}"),
                    runtime.ClassificationId,
                    [artifactId]));

        if (!result.IntelligenceResult.Success)
        {
            global::System.Console.Error.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    result.IntelligenceResult.Message ??
                    "VA decision document interpretation failed."));

            return 1;
        }

        if (result.Interpretation is null)
        {
            global::System.Console.Error.WriteLine(
                "VA decision document interpretation produced no interpretation.");

            return 1;
        }

        output.WriteLine(
            $"Artifact    : {result.Interpretation.ArtifactId.Value}");

        output.WriteLine(
            $"Decision Date: " +
            $"{result.Interpretation.DecisionDate?.ToString("u") ?? "Not provided"}");

        output.WriteLine(
            $"Issues      : {result.Interpretation.IssueDecisions.Count}");

        output.WriteLine(
            $"Requires Review: {result.IntelligenceResult.RequiresReview}");

        foreach (var issue in result.Interpretation.IssueDecisions)
        {
            output.WriteLine();
            output.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    $"Issue       : {issue.IssueDescription}"));

            output.WriteLine(
                $"Outcome     : {issue.Outcome}");

            output.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    $"Rationale   : {issue.Rationale}"));

            foreach (var finding in issue.FavorableFindings)
            {
                output.WriteLine(
                    ConsoleTextSanitizer.Sanitize(
                        $"Favorable   : {finding}"));
            }

            foreach (var finding in issue.AdverseFindings)
            {
                output.WriteLine(
                    ConsoleTextSanitizer.Sanitize(
                        $"Adverse     : {finding}"));
            }

            foreach (var regulation in issue.CitedRegulations)
            {
                output.WriteLine(
                    ConsoleTextSanitizer.Sanitize(
                        $"Regulation  : {regulation}"));
            }

            foreach (var evidence in issue.ReferencedEvidence)
            {
                output.WriteLine(
                    ConsoleTextSanitizer.Sanitize(
                        $"Evidence    : {evidence}"));
            }

            foreach (var excerpt in issue.SourceExcerpts)
            {
                output.WriteLine(
                    ConsoleTextSanitizer.Sanitize(
                        $"Source      : {excerpt.Text}"));
            }
        }

        return 0;
    }

    public static async Task<int> RunEvidenceDevelopmentPlanAsync(
        string databasePath,
        EvidenceDevelopmentPlanId planId,
        TextWriter output)
    {
        ArgumentNullException.ThrowIfNull(output);

        void WriteSanitized(string value) =>
            output.WriteLine(
                ConsoleTextSanitizer.Sanitize(value));

        var repository =
            new SqliteEvidenceDevelopmentPlanRepository(
                databasePath);

        await repository.InitializeAsync();

        var gaps =
            new SqliteEvidenceGapRepository(databasePath);

        var plans =
            new EvidenceDevelopmentPlanService(
                repository,
                gaps);

        var result =
            await plans.GetEvidenceDevelopmentPlanAsync(
                planId);

        if (result is null)
        {
            WriteSanitized(
                $"Evidence development plan not found: {planId.Value}");

            return 1;
        }

        WriteSanitized(
            $"Plan ID     : {result.Plan.Id.Value}");

        WriteSanitized(
            $"Claim Issue : {result.Plan.ClaimIssueId.Value}");

        WriteSanitized(
            $"Description : {result.Plan.Description}");

        WriteSanitized(
            $"Status      : {result.Status?.Status ?? "Unknown"}");

        WriteSanitized(
            $"Requirements: {result.Requirements.Count}");

        foreach (var requirement in result.Requirements)
        {
            WriteSanitized(
                $"Plan Requirement: {requirement.RequirementId.Value}");
        }

        WriteSanitized(
            $"Evidence Gaps: {result.EvidenceGaps.Count}");

        foreach (var gap in result.GapDetails)
        {
            WriteSanitized(
                $"Gap         : {gap.Id.Value}");

            WriteSanitized(
                $"Requirement : {gap.RequirementId.Value}");

            WriteSanitized(
                $"Gap Status  : {gap.Status}");

            WriteSanitized(
                $"Gap Detail  : {gap.Description}");
        }

        WriteSanitized(
            $"Artifacts   : {result.Artifacts.Count}");

        foreach (var artifact in result.Artifacts)
        {
            WriteSanitized(
                $"Plan Artifact: {artifact.ArtifactId.Value} ({artifact.Role})");
        }

        WriteSanitized(
            $"Executions  : {result.Executions.Count}");

        foreach (var execution in result.Executions)
        {
            WriteSanitized(
                $"Execution   : {execution.EvidenceGapId.Value} -> {execution.WorkflowId.Value}");
        }

        WriteSanitized(
            $"Results     : {result.Results.Count}");

        foreach (var developmentResult in result.Results)
        {
            WriteSanitized(
                $"Result      : {developmentResult.EvidenceGapId.Value}");

            WriteSanitized(
                $"Result Req  : {developmentResult.RequirementId.Value}");

            WriteSanitized(
                $"Matched     : {developmentResult.MatchingGuidanceItemCount?.ToString() ?? "Unknown"}");

            WriteSanitized(
                $"Missing     : {developmentResult.MissingGuidanceItemCount?.ToString() ?? "Unknown"}");

            WriteSanitized(
                $"Result Status: {developmentResult.ResultingGapStatus ?? "Unknown"}");
        }

        return 0;
    }


    private static async Task<int> RunExecuteAsync(
        string databasePath,
        EvidenceDevelopmentPlanId planId)
    {
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
            VeteransEvidenceOrchestrationFactory
                .CreateEvidenceDevelopmentWorkflowCoordinator(
                    workflowService,
                    developmentRepository,
                    workflowRunner,
                    gapRepository,
                    guidanceRepository);

        var plans =
            new EvidenceDevelopmentPlanService(
                developmentRepository,
                gapRepository);

        var service =
            new EvidenceDevelopmentPlanExecutionService(
                plans,
                coordinator);

        var result =
            await service.ExecuteAsync(planId);

        if (result is null)
        {
            global::System.Console.Error.WriteLine(
                $"Evidence development plan not found: {planId.Value}");

            return 1;
        }

        global::System.Console.WriteLine(
            $"Executions: {result.Count}");

        return 0;
    }


    internal static async Task<int> RunEvidencePackageAsync(
        string databasePath,
        EvidencePackageId evidencePackageId,
        TextWriter output,
        Func<IArtifactContentStore?>? contentStoreFactory = null)
    {
        ArgumentNullException.ThrowIfNull(output);

        var packageService =
            new EvidencePackageService(
                new SqliteEvidencePackageRepository(
                    databasePath),
                new GuidIdGenerator());

        var evidenceRepository =
            new SqliteEvidenceRepository(databasePath);

        await evidenceRepository.InitializeAsync();

        var classifications =
            new SqliteEvidenceClassificationRepository(
                databasePath);

        contentStoreFactory ??=
            ArtifactContentStoreFactory.Create;

        var contentStore =
            contentStoreFactory();

        var service =
            contentStore is null
                ? new VeteransReviewerPackageDetailsService(
                    packageService,
                    evidenceRepository,
                    classifications)
                : new VeteransReviewerPackageDetailsService(
                    packageService,
                    evidenceRepository,
                    classifications,
                    ArtifactTextExtractionFactory.Create(
                        evidenceRepository,
                        contentStore),
                    ArtifactPrintRenderingFactory.Create(
                        evidenceRepository,
                        contentStore));

        var details =
            await service.GetAsync(
                evidencePackageId);

        if (details is null)
        {
            global::System.Console.Error.WriteLine(
                $"Evidence package not found: {evidencePackageId.Value}");

            return 1;
        }

        foreach (var line in
            VeteransEvidencePackageFormatter.Format(details))
        {
            output.WriteLine(line);
        }

        return 0;
    }


    internal static async Task<int> RunEvidencePackageDocxAsync(
        string databasePath,
        EvidencePackageId evidencePackageId,
        string outputPath,
        IArtifactContentStore? suppliedContentStore = null,
        Func<IArtifactContentStore?>? contentStoreFactory = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);

        var fullDatabasePath =
            Path.GetFullPath(databasePath);

        var fullOutputPath =
            Path.GetFullPath(outputPath);

        var pathComparison =
            OperatingSystem.IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        if (string.Equals(
            fullDatabasePath,
            fullOutputPath,
            pathComparison))
        {
            global::System.Console.Error.WriteLine(
                "Reviewer package output cannot overwrite " +
                "the Veterans Claims database.");

            return 2;
        }

        var packageService =
            new EvidencePackageService(
                new SqliteEvidencePackageRepository(
                    fullDatabasePath),
                new GuidIdGenerator());

        var evidenceRepository =
            new SqliteEvidenceRepository(fullDatabasePath);

        await evidenceRepository.InitializeAsync();

        var classifications =
            new SqliteEvidenceClassificationRepository(
                fullDatabasePath);

        contentStoreFactory ??=
            ArtifactContentStoreFactory.Create;

        var contentStore =
            suppliedContentStore ??
            contentStoreFactory();

        var service =
            contentStore is null
                ? new VeteransReviewerPackageDetailsService(
                    packageService,
                    evidenceRepository,
                    classifications)
                : new VeteransReviewerPackageDetailsService(
                    packageService,
                    evidenceRepository,
                    classifications,
                    ArtifactTextExtractionFactory.Create(
                        evidenceRepository,
                        contentStore),
                    ArtifactPrintRenderingFactory.Create(
                        evidenceRepository,
                        contentStore));

        var details =
            await service.GetAsync(evidencePackageId);

        if (details is null)
            return 1;

        if (contentStore is null &&
            details.PackageDetails.Artifacts.Any(
                artifact =>
                    string.Equals(
                        artifact.ContentRole,
                        EvidencePackageContentRoles.UnderlyingEvidence,
                        StringComparison.Ordinal)))
        {
            global::System.Console.Error.WriteLine(
                "Reviewer package contains underlying evidence, " +
                "but preserved artifact content is unavailable.");

            return 2;
        }

        var content =
            VeteransReviewerPackageDocxRenderer.Render(
                details);

        await WriteFileAtomicallyAsync(
            fullOutputPath,
            content);

        return 0;
    }

    private static async Task WriteFileAtomicallyAsync(
        string outputPath,
        byte[] content)
    {
        var directory =
            Path.GetDirectoryName(outputPath) ??
            throw new InvalidOperationException(
                "Reviewer package output directory could not be resolved.");

        var temporaryPath =
            Path.Combine(
                directory,
                $".{Path.GetFileName(outputPath)}.{Guid.NewGuid():N}.tmp");

        try
        {
            var options =
                new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    Share = FileShare.None,
                    BufferSize = 4096,
                    Options =
                        FileOptions.Asynchronous |
                        FileOptions.WriteThrough
                };

            if (!OperatingSystem.IsWindows())
            {
                options.UnixCreateMode =
                    UnixFileMode.UserRead |
                    UnixFileMode.UserWrite;
            }

            await using (var stream =
                new FileStream(
                    temporaryPath,
                    options))
            {
                await stream.WriteAsync(content);
                await stream.FlushAsync();
            }

            File.Move(
                temporaryPath,
                outputPath,
                true);
        }
        finally
        {
            if (File.Exists(temporaryPath))
                File.Delete(temporaryPath);
        }
    }


    private static async Task<int> RunClassifyAsync(
        string databasePath,
        ClaimIssueId claimIssueId,
        ArtifactId artifactId,
        string classification)
    {
        var claimIssue =
            await new SqliteClaimIssueRepository(databasePath)
                .GetClaimIssueAsync(claimIssueId);

        if (claimIssue is null)
        {
            global::System.Console.Error.WriteLine(
                $"Claim issue not found: {claimIssueId.Value}");

            return 1;
        }

        var evidenceRepository =
            new SqliteEvidenceRepository(databasePath);

        await evidenceRepository.InitializeAsync();

        var artifact =
            await evidenceRepository.GetArtifactAsync(artifactId);

        if (artifact is null)
        {
            global::System.Console.Error.WriteLine(
                $"Artifact not found: {artifactId.Value}");

            return 1;
        }

        var classifications =
            new SqliteEvidenceClassificationRepository(databasePath);

        var service =
            new EvidenceClassificationService(
                classifications,
                new GuidIdGenerator());

        var result =
            await service.ClassifyAsync(
                artifactId,
                classification,
                claimIssueId);

        global::System.Console.WriteLine(
            $"Classification ID : {result.Id.Value}");
        global::System.Console.WriteLine(
            $"Artifact ID       : {result.ArtifactId.Value}");
        global::System.Console.WriteLine(
            $"Claim Issue       : {result.ClaimIssueId?.Value ?? "<none>"}");
        global::System.Console.WriteLine(
            $"Classification    : {result.Classification}");

        return 0;
    }


    private static async Task<int> RunGuidanceAsync(
        string databasePath,
        RequirementId requirementId,
        string evidenceClassification,
        string guidanceRole,
        string description)
    {
        var guidance =
            new SqliteEvidenceRequirementGuidanceRepository(
                databasePath);

        await guidance.InitializeAsync();

        var service =
            new RegulatoryEvidenceGuidanceService(
                new SqliteRegulatoryRepository(databasePath),
                guidance,
                new GuidIdGenerator());

        try
        {
            var result =
                await service.AddEvidenceGuidanceAsync(
                    requirementId,
                    evidenceClassification,
                    guidanceRole,
                    description);

            global::System.Console.WriteLine(
                $"Guidance ID     : {result.Id.Value}");
            global::System.Console.WriteLine(
                $"Requirement     : {result.RequirementId.Value}");
            global::System.Console.WriteLine(
                $"Classification  : {result.EvidenceClassification}");
            global::System.Console.WriteLine(
                $"Guidance Role   : {result.GuidanceRole}");
            global::System.Console.WriteLine(
                $"Description     : {result.Description}");

            return 0;
        }
        catch (ArgumentException ex)
        {
            global::System.Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            global::System.Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }


    private static async Task<int> RunPrepareAsync(
        string databasePath,
        ClaimIssueId claimIssueId,
        EvidenceDevelopmentPlanId planId)
    {
        var serviceConnections =
            new SqliteServiceConnectionRepository(databasePath);

        var gapsRepository =
            new SqliteEvidenceGapRepository(databasePath);

        var guidance =
            new SqliteEvidenceRequirementGuidanceRepository(
                databasePath);

        var classifications =
            new SqliteEvidenceClassificationRepository(
                databasePath);

        var requirementEvidence =
            new RequirementEvidenceService(
                classifications,
                guidance);

        var gapService =
            new EvidenceGapService(
                gapsRepository,
                requirementEvidence,
                new GuidIdGenerator());

        var serviceConnectionGaps =
            new ServiceConnectionEvidenceGapService(
                serviceConnections,
                gapService);

        var planRepository =
            new SqliteEvidenceDevelopmentPlanRepository(
                databasePath);

        await planRepository.InitializeAsync();

        var planService =
            new EvidenceDevelopmentPlanService(
                planRepository,
                gapsRepository);

        var preparation =
            new EvidenceDevelopmentPreparationService(
                serviceConnectionGaps,
                planService);

        var result =
            await preparation.PrepareAsync(
                planId,
                claimIssueId,
                "Develop missing service-connection evidence.");

        if (result is null)
        {
            global::System.Console.WriteLine(
                "No evidence development required.");

            return 0;
        }

        global::System.Console.WriteLine(
            $"Plan ID     : {result.Plan.Id.Value}");

        global::System.Console.WriteLine(
            $"Claim Issue : {result.Plan.ClaimIssueId.Value}");

        global::System.Console.WriteLine(
            $"Evidence Gaps: {result.EvidenceGaps.Count}");

        return 0;
    }


    private static async Task<int> RunChecklistAsync(
        string databasePath,
        ClaimIssueId claimIssueId)
    {
        var gaps =
            new SqliteEvidenceGapRepository(databasePath);

        var guidance =
            new SqliteEvidenceRequirementGuidanceRepository(
                databasePath);

        var classifications =
            new SqliteEvidenceClassificationRepository(
                databasePath);

        var requirements =
            new RequirementEvidenceService(
                classifications,
                guidance);

        var service =
            new ClaimIssueEvidenceChecklistService(
                gaps,
                requirements);

        var checklist =
            await service.CreateChecklistAsync(claimIssueId);

        foreach (var line in
            VeteransEvidenceChecklistFormatter.Format(checklist))
        {
            global::System.Console.WriteLine(line);
        }

        return 0;
    }

    private static void ShowUsage()
    {
        global::System.Console.WriteLine(
            "Usage: emf veterans condition service-connected " +
            "<database-path> <veteran-id> <basis-id> " +
            "<medical-condition-id> <condition-name>");

        global::System.Console.WriteLine(
            "       emf veterans regulatory requirement " +
            "<database-path> <basis-id> <authority-id> " +
            "<authority-citation> <authority-title> <provision-id> " +
            "<provision-citation> <version> <source-uri> <source-hash> " +
            "<requirement-id> <description>");

        global::System.Console.WriteLine(
            "       emf veterans evidence develop " +
            "[--summarize [--promote]] " +
            "<database-path> <plan-id> <evidence-gap-id>");

        global::System.Console.WriteLine(
            "       emf veterans evidence ingest " +
            "<database-path> <source-path>");

        global::System.Console.WriteLine(
            "       emf veterans evidence checklist " +
            "<database-path> <claim-issue-id>");

        global::System.Console.WriteLine(
            "       emf veterans evidence reviewer " +
            "<database-path> <claim-issue-id>");

        global::System.Console.WriteLine(
            "       emf veterans evidence reviewer " +
            "<database-path> <claim-issue-id> <output.docx>");

        global::System.Console.WriteLine(
            "       emf veterans evidence reviewer " +
            "<database-path> <claim-issue-id> --basis <basis-id>");

        global::System.Console.WriteLine(
            "       emf veterans evidence reviewer " +
            "<database-path> <claim-issue-id> --basis <basis-id> <output.docx>");

        global::System.Console.WriteLine(
            "       emf veterans evidence classify " +
            "<database-path> <claim-issue-id> <artifact-id> <classification>");

        global::System.Console.WriteLine(
            "       emf veterans evidence guidance " +
            "<database-path> <requirement-id> <classification> " +
            "<role> <description>");

        global::System.Console.WriteLine(
            "       emf veterans evidence literature source " +
            "<database-path> <source-json-path>");

        global::System.Console.WriteLine(
            "       emf veterans evidence literature link " +
            "<database-path> <requirement-id> <source-id> " +
            "<role> <description>");

        global::System.Console.WriteLine(
            "       emf veterans evidence package " +
            "<database-path> <package-id>");

        global::System.Console.WriteLine(
            "       emf veterans evidence package " +
            "<database-path> <package-id> <output.docx>");

        global::System.Console.WriteLine(
            "       emf veterans evidence prepare " +
            "<database-path> <claim-issue-id> <plan-id>");

        global::System.Console.WriteLine(
            "       emf veterans evidence execute " +
            "<database-path> <plan-id>");


        global::System.Console.WriteLine(
            "       emf veterans decision interpret " +
            "<database-path> <artifact-id>");

        global::System.Console.WriteLine(
            "       emf veterans decision history " +
            "<database-path> <claim-id>");

        global::System.Console.WriteLine(
            "       emf veterans decision review " +
            "<database-path> <claim-issue-id>");

        global::System.Console.WriteLine(
            "       emf veterans adjudication assess " +
            "<database-path> <claim-issue-id>");

        global::System.Console.WriteLine(
            "       emf veterans adjudication claim " +
            "<database-path> <claim-id>");
    }
}

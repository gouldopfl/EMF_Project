using EMF.Common;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Integrity;
using EMF.Extensions.VeteransClaims.Orchestration;
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
        Func<Task<TextSummarizationConsoleRuntime>> runtimeFactory)
    {
        ArgumentNullException.ThrowIfNull(runtimeFactory);

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

        if (args.Length == 4 &&
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

            return await RunReviewerPackageAsync(
                reviewerDatabasePath,
                new ClaimIssueId(args[3]),
                runtimeFactory);
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
                        intelligenceResult.Message ??
                        "Evidence development summarization failed.");

                    return 1;
                }

                global::System.Console.WriteLine();
                global::System.Console.WriteLine("Summary");
                global::System.Console.WriteLine("-------");
                global::System.Console.WriteLine(
                    intelligenceResult.Output);

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
                $"Evidence development failed: {ex.Message}");

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
        Func<Task<TextSummarizationConsoleRuntime>> runtimeFactory)
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

        var runtime =
            await runtimeFactory();

        var intelligence =
            VeteransEvidenceOrchestrationFactory
                .CreateReviewerPackageIntelligenceService(
                    runtime.TextSummarizationCapabilityExecutor);

        var result =
            await intelligence.SummarizeAsync(
                details,
                new IntelligenceExecutionContext(
                    runtime.SubjectId,
                    new IntelligenceCorrelationId(
                        $"veterans-{Guid.NewGuid():N}"),
                    runtime.ClassificationId,
                    sourceArtifactIds));

        if (!result.Success)
        {
            global::System.Console.Error.WriteLine(
                result.Message ??
                "Reviewer package summarization failed.");

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
            result.Output);

        global::System.Console.WriteLine(
            $"Summary Artifact ID : {prepared.SummaryArtifact.Id.Value}");

        global::System.Console.WriteLine(
            $"Package ID          : {prepared.Package.Id.Value}");

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
            timeline);
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
                $"Claimed Condition: {claimedCondition.Id.Value} ({claimedCondition.Name})");
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
                        $"Service Connected: " +
                        $"{condition.ServiceConnectedCondition.Id.Value} " +
                        $"({condition.ServiceConnectedCondition.Name})");
                }

                foreach (var serviceEvent in
                    result.Details.ServiceEvents.Where(
                        x => x.Basis.Id == basis.Id))
                {
                    output.WriteLine(
                        $"Service Event: " +
                        $"{serviceEvent.ServiceEvent.Id.Value} " +
                        $"({serviceEvent.ServiceEvent.Description})");
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
                        $"Description : {requirement.Requirement.Description}");

                    output.WriteLine(
                        $"Provision   : " +
                        $"{requirement.Requirement.RegulatoryProvisionId.Value}");

                    output.WriteLine(
                        $"Citation    : " +
                        $"{requirement.RegulatoryProvision.Citation}");

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
                $"- {blocking.Requirement.Id.Value}: " +
                blocking.Requirement.Description);
        }

        output.WriteLine(
            $"Outstanding Items: {result.Readiness.OutstandingItemCount}");

        foreach (var item in result.Readiness.BlockingItems)
        {
            output.WriteLine(
                $"  - {item.EvidenceClassification} / " +
                $"{item.GuidanceRole}: {item.Description}");
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
                $"- {item.OccurredAt:O} " +
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
                result.IntelligenceResult.Message ??
                "VA decision document interpretation failed.");

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
                $"Issue       : {issue.IssueDescription}");

            output.WriteLine(
                $"Outcome     : {issue.Outcome}");

            output.WriteLine(
                $"Rationale   : {issue.Rationale}");

            foreach (var finding in issue.FavorableFindings)
                output.WriteLine($"Favorable   : {finding}");

            foreach (var finding in issue.AdverseFindings)
                output.WriteLine($"Adverse     : {finding}");

            foreach (var regulation in issue.CitedRegulations)
                output.WriteLine($"Regulation  : {regulation}");

            foreach (var evidence in issue.ReferencedEvidence)
                output.WriteLine($"Evidence    : {evidence}");

            foreach (var excerpt in issue.SourceExcerpts)
                output.WriteLine($"Source      : {excerpt.Text}");
        }

        return 0;
    }

    public static async Task<int> RunEvidenceDevelopmentPlanAsync(
        string databasePath,
        EvidenceDevelopmentPlanId planId,
        TextWriter output)
    {
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
            output.WriteLine(
                $"Evidence development plan not found: {planId.Value}");

            return 1;
        }

        output.WriteLine(
            $"Plan ID     : {result.Plan.Id.Value}");

        output.WriteLine(
            $"Claim Issue : {result.Plan.ClaimIssueId.Value}");

        output.WriteLine(
            $"Description : {result.Plan.Description}");

        output.WriteLine(
            $"Status      : {result.Status?.Status ?? "Unknown"}");

        output.WriteLine(
            $"Requirements: {result.Requirements.Count}");

        foreach (var requirement in result.Requirements)
        {
            output.WriteLine(
                $"Plan Requirement: {requirement.RequirementId.Value}");
        }

        output.WriteLine(
            $"Evidence Gaps: {result.EvidenceGaps.Count}");

        foreach (var gap in result.GapDetails)
        {
            output.WriteLine(
                $"Gap         : {gap.Id.Value}");

            output.WriteLine(
                $"Requirement : {gap.RequirementId.Value}");

            output.WriteLine(
                $"Gap Status  : {gap.Status}");

            output.WriteLine(
                $"Gap Detail  : {gap.Description}");
        }

        output.WriteLine(
            $"Artifacts   : {result.Artifacts.Count}");

        foreach (var artifact in result.Artifacts)
        {
            output.WriteLine(
                $"Plan Artifact: {artifact.ArtifactId.Value} ({artifact.Role})");
        }

        output.WriteLine(
            $"Executions  : {result.Executions.Count}");

        foreach (var execution in result.Executions)
        {
            output.WriteLine(
                $"Execution   : {execution.EvidenceGapId.Value} -> {execution.WorkflowId.Value}");
        }

        output.WriteLine(
            $"Results     : {result.Results.Count}");

        foreach (var developmentResult in result.Results)
        {
            output.WriteLine(
                $"Result      : {developmentResult.EvidenceGapId.Value}");

            output.WriteLine(
                $"Result Req  : {developmentResult.RequirementId.Value}");

            output.WriteLine(
                $"Matched     : {developmentResult.MatchingGuidanceItemCount?.ToString() ?? "Unknown"}");

            output.WriteLine(
                $"Missing     : {developmentResult.MissingGuidanceItemCount?.ToString() ?? "Unknown"}");

            output.WriteLine(
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
        TextWriter output)
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

        var contentStore =
            ArtifactContentStoreFactory.Create();

        var service =
            contentStore is null
                ? new VeteransReviewerPackageDetailsService(
                    packageService,
                    evidenceRepository)
                : new VeteransReviewerPackageDetailsService(
                    packageService,
                    evidenceRepository,
                    ArtifactTextExtractionFactory.Create(
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
                planRepository);

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
            "Usage: emf veterans evidence develop " +
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
            "       emf veterans evidence package " +
            "<database-path> <package-id>");

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

using EMF.Core.Contracts;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Orchestration.Contracts;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public static class VeteransEvidenceOrchestrationFactory
{
    public static IVeteransEvidenceSummaryPromotionService
        CreateEvidenceSummaryPromotionService(
            IIntelligenceEvidencePromotionService promotionService)
    {
        ArgumentNullException.ThrowIfNull(promotionService);

        return new VeteransEvidenceSummaryPromotionService(
            promotionService);
    }

    public static IEvidenceDevelopmentWorkflowCoordinator
        CreateEvidenceDevelopmentWorkflowCoordinator(
            IWorkflowService workflowService,
            IEvidenceDevelopmentPlanRepository developmentRepository,
            IWorkflowRunner workflowRunner,
            IEvidenceGapRepository gapRepository,
            IEvidenceRequirementGuidanceRepository guidanceRepository)
    {
        ArgumentNullException.ThrowIfNull(workflowService);
        ArgumentNullException.ThrowIfNull(developmentRepository);
        ArgumentNullException.ThrowIfNull(workflowRunner);
        ArgumentNullException.ThrowIfNull(gapRepository);
        ArgumentNullException.ThrowIfNull(guidanceRepository);

        return new EvidenceDevelopmentWorkflowCoordinator(
            workflowService,
            developmentRepository,
            workflowRunner,
            gapRepository,
            guidanceRepository,
            new EmptyEvidenceRecognitionCoordinator());
    }


    public static IEvidenceDevelopmentWorkflowCoordinator
        CreateEvidenceDevelopmentWorkflowCoordinator(
            IWorkflowService workflowService,
            IEvidenceDevelopmentPlanRepository developmentRepository,
            IWorkflowRunner workflowRunner,
            IEvidenceGapRepository gapRepository,
            IEvidenceRequirementGuidanceRepository guidanceRepository,
            IArtifactTextExtractor textExtractor,
            IEvidenceRecognitionTermRepository recognitionTermRepository)
    {
        ArgumentNullException.ThrowIfNull(workflowService);
        ArgumentNullException.ThrowIfNull(developmentRepository);
        ArgumentNullException.ThrowIfNull(workflowRunner);
        ArgumentNullException.ThrowIfNull(gapRepository);
        ArgumentNullException.ThrowIfNull(guidanceRepository);
        ArgumentNullException.ThrowIfNull(textExtractor);
        ArgumentNullException.ThrowIfNull(recognitionTermRepository);

        var recognitionCoordinator =
            new EvidenceRecognitionCoordinator(
                gapRepository,
                textExtractor,
                recognitionTermRepository);

        return new EvidenceDevelopmentWorkflowCoordinator(
            workflowService,
            developmentRepository,
            workflowRunner,
            gapRepository,
            guidanceRepository,
            recognitionCoordinator);
    }

    public static IEvidenceDevelopmentIntelligenceCoordinator
        CreateEvidenceDevelopmentIntelligenceCoordinator(
            IEvidenceDevelopmentPlanRepository developmentRepository,
            IEvidenceGapRepository gapRepository,
            IIntelligenceCapabilityExecutor<
                TextSummarizationRequest,
                string> executor)
    {
        ArgumentNullException.ThrowIfNull(developmentRepository);
        ArgumentNullException.ThrowIfNull(gapRepository);
        ArgumentNullException.ThrowIfNull(executor);

        return new EvidenceDevelopmentIntelligenceCoordinator(
            developmentRepository,
            gapRepository,
            executor);
    }
}

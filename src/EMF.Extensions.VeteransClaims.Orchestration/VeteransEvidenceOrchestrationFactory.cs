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

    public static VeteransReviewerPackagePreparationService
        CreateReviewerPackagePreparationService(
            IVeteransEvidenceSummaryPromotionService summaryPromotion,
            IEvidencePackagePreparationService packagePreparation)
    {
        ArgumentNullException.ThrowIfNull(summaryPromotion);
        ArgumentNullException.ThrowIfNull(packagePreparation);

        return new VeteransReviewerPackagePreparationService(
            summaryPromotion,
            packagePreparation);
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
            IEvidenceRecognitionTermRepository recognitionTermRepository,
            IEvidenceClassificationService classificationService)
    {
        ArgumentNullException.ThrowIfNull(workflowService);
        ArgumentNullException.ThrowIfNull(developmentRepository);
        ArgumentNullException.ThrowIfNull(workflowRunner);
        ArgumentNullException.ThrowIfNull(gapRepository);
        ArgumentNullException.ThrowIfNull(guidanceRepository);
        ArgumentNullException.ThrowIfNull(textExtractor);
        ArgumentNullException.ThrowIfNull(recognitionTermRepository);
        ArgumentNullException.ThrowIfNull(classificationService);

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
            recognitionCoordinator,
            classificationService);
    }


    public static IEvidenceDevelopmentWorkflowCoordinator
        CreateEvidenceDevelopmentWorkflowCoordinator(
            IWorkflowService workflowService,
            IEvidenceDevelopmentPlanRepository developmentRepository,
            IWorkflowRunner workflowRunner,
            IEvidenceGapRepository gapRepository,
            IEvidenceRequirementGuidanceRepository guidanceRepository,
            IArtifactTextExtractor textExtractor,
            IEvidenceRecognitionTermRepository recognitionTermRepository,
            IEvidenceClassificationService classificationService,
            IRequirementEvidenceService requirementEvidenceService)
    {
        ArgumentNullException.ThrowIfNull(workflowService);
        ArgumentNullException.ThrowIfNull(developmentRepository);
        ArgumentNullException.ThrowIfNull(workflowRunner);
        ArgumentNullException.ThrowIfNull(gapRepository);
        ArgumentNullException.ThrowIfNull(guidanceRepository);
        ArgumentNullException.ThrowIfNull(textExtractor);
        ArgumentNullException.ThrowIfNull(recognitionTermRepository);
        ArgumentNullException.ThrowIfNull(classificationService);
        ArgumentNullException.ThrowIfNull(requirementEvidenceService);

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
            recognitionCoordinator,
            classificationService,
            requirementEvidenceService);
    }

    public static IVaDecisionDocumentInterpretationCoordinator
        CreateVaDecisionDocumentInterpretationCoordinator(
            IArtifactTextExtractor textExtractor,
            IIntelligenceCapabilityExecutor<
                TextStructuredExtractionRequest,
                string> executor)
    {
        ArgumentNullException.ThrowIfNull(textExtractor);
        ArgumentNullException.ThrowIfNull(executor);

        return new VaDecisionDocumentInterpretationCoordinator(
            textExtractor,
            executor);
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

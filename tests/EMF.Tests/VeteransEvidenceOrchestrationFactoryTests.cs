using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Orchestration.Contracts;

namespace EMF.Tests;

public sealed class VeteransEvidenceOrchestrationFactoryTests
{
    [Fact]
    public void CreateEvidenceSummaryPromotionService_RequiresPromotionService()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                VeteransEvidenceOrchestrationFactory
                    .CreateEvidenceSummaryPromotionService(null!));
    }

    [Fact]
    public void CreateEvidenceDevelopmentWorkflowCoordinator_RequiresDependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                VeteransEvidenceOrchestrationFactory
                    .CreateEvidenceDevelopmentWorkflowCoordinator(
                        null!,
                        null!,
                        null!,
                        null!,
                        null!));
    }

    [Fact]
    public void CreateEvidenceDevelopmentWorkflowCoordinator_RequiresRequirementEvidenceService()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                VeteransEvidenceOrchestrationFactory
                    .CreateEvidenceDevelopmentWorkflowCoordinator(
                        null!,
                        null!,
                        null!,
                        null!,
                        null!,
                        null!,
                        null!,
                        null!,
                        null!));
    }

    [Fact]
    public void CreateVaDecisionDocumentInterpretationCoordinator_RequiresDependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                VeteransEvidenceOrchestrationFactory
                    .CreateVaDecisionDocumentInterpretationCoordinator(
                        null!,
                        null!));
    }


    [Fact]
    public void CreateEvidenceDevelopmentIntelligenceCoordinator_RequiresDependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () =>
                VeteransEvidenceOrchestrationFactory
                    .CreateEvidenceDevelopmentIntelligenceCoordinator(
                        null!,
                        null!,
                        null!));
    }
}

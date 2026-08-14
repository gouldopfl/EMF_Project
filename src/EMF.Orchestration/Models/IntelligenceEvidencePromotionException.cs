namespace EMF.Orchestration.Models;

public sealed class IntelligenceEvidencePromotionException :
    InvalidOperationException
{
    public IntelligenceEvidencePromotionException(
        string message)
        : base(message)
    {
    }
}

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerMedicationClinicalContext
{
    public required string MedicationName { get; init; }

    public required string ContextType { get; init; }

    public required string SourceLocator { get; init; }

    public required string Summary { get; init; }
}

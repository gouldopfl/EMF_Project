using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Medications;

/// <summary>An attributed indication clarification, separate from prescription status and source wording.</summary>
public sealed class MedicationIndicationReconciliation
{
    public required string Id { get; init; }
    public required VeteranId VeteranId { get; init; }
    public required string MedicationName { get; init; }
    public required DateOnly ReconciliationDate { get; init; }
    public required string Indication { get; init; }
    public required string Source { get; init; }
}

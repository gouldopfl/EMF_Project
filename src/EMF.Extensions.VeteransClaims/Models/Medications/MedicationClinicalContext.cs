using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Medications;

public sealed class MedicationClinicalContext
{
    public required MedicationClinicalContextId Id { get; init; }

    public required VeteranId VeteranId { get; init; }

    public required ArtifactId SourceArtifactId { get; init; }

    public required DateOnly EventDate { get; init; }

    public required int SourceStartPage { get; init; }

    public required int SourceEndPage { get; init; }

    public required string MedicationName { get; init; }

    public required string PrescriptionNumber { get; init; }

    public required string ContextType { get; init; }

    public string? RecordTitle { get; init; }

    public required string Summary { get; init; }
}

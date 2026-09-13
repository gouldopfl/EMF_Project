using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Medications;

public sealed class MedicationRecord
{
    public required MedicationRecordId Id { get; init; }

    public required VeteranId VeteranId { get; init; }

    public required ArtifactId SourceArtifactId { get; init; }

    public required DateOnly RecordDate { get; init; }

    public required int SourcePage { get; init; }

    public required string MedicationName { get; init; }

    public string? Strength { get; init; }

    public string? Directions { get; init; }

    public string? Indication { get; init; }

    public required string Status { get; init; }

    public string? SourceDesignation { get; init; }
}

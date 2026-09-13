using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Medications;

public sealed class MedicationHistoryEvent
{
    public required MedicationHistoryEventId Id { get; init; }

    public required VeteranId VeteranId { get; init; }

    public required ArtifactId SourceArtifactId { get; init; }

    public required DateOnly EventDate { get; init; }

    public required int SourcePage { get; init; }

    public required string MedicationName { get; init; }

    public required string EventType { get; init; }

    public string? Strength { get; init; }

    public string? Directions { get; init; }

    public string? PharmacyIndication { get; init; }

    public string? PrescriptionNumber { get; init; }
}

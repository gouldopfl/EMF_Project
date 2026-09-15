using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Medications;

public sealed class MedicationLedger
{
    public required MedicationLedgerId Id { get; init; }

    public required VeteranId VeteranId { get; init; }

    public required ArtifactId SourceArtifactId { get; init; }

    public required DateOnly ReportDate { get; init; }

    public required int SourceStartPage { get; init; }

    public required int SourceEndPage { get; init; }

    public int? ReportedEntryCount { get; init; }

    public required int ParsedEntryCount { get; init; }

    public required bool IsComplete { get; init; }
}

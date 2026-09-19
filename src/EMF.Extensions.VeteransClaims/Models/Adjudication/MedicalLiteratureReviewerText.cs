using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class MedicalLiteratureReviewerText
{
    public required MedicalLiteratureSourceId MedicalLiteratureSourceId { get; init; }

    public required ArtifactId ArtifactId { get; init; }

    public required string Text { get; init; }

    public string? SourceHash { get; init; }

    public required string ExtractionMethod { get; init; }

    public required DateTimeOffset ExtractedUtc { get; init; }
}

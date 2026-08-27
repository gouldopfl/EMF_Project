using EMF.Core.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class DecisionDocumentSourceExcerpt
{
    public required ArtifactId ArtifactId { get; init; }

    public required string Text { get; init; }

    public int? StartOffset { get; init; }

    public int? Length { get; init; }
}

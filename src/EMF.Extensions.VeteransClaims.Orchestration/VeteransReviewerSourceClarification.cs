using System.Text.Json.Serialization;
using EMF.Core.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerSourceClarification
{
    [JsonIgnore]
    public ArtifactId ReviewerArtifactId { get; init; }

    public required string SourceLocator { get; init; }

    public required string OriginalText { get; init; }

    public required string Clarification { get; init; }
}

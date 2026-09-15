using System.Text.Json.Serialization;
using EMF.Core.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerClinicalProgressionEvent
{
    [JsonIgnore]
    public ArtifactId ReviewerArtifactId { get; init; }

    public required DateOnly EventDate { get; init; }

    public required string EventType { get; init; }

    public required string SourceLocator { get; init; }

    public required string Summary { get; init; }
}

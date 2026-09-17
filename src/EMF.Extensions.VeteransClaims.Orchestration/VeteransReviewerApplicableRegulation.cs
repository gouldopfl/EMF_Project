namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerApplicableRegulation
{
    public required string Citation { get; init; }

    public required string Text { get; init; }

    public required string SourceUri { get; init; }

    public required DateOnly UpToDateAsOf { get; init; }

    public required DateTimeOffset RetrievedUtc { get; init; }

    public required string SourceSha256 { get; init; }
}

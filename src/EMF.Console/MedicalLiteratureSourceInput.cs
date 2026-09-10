namespace EMF.ConsoleApplication;

internal sealed class MedicalLiteratureSourceInput
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Authors { get; init; }
    public required string Publication { get; init; }

    public int? PublicationYear { get; init; }

    public required bool VaAffiliated { get; init; }
    public required bool VaFunded { get; init; }
    public required bool PeerReviewed { get; init; }

    public string? FundingSource { get; init; }
    public string? ResearchOrganization { get; init; }
    public string? Doi { get; init; }
    public string? Pmid { get; init; }
    public string? SourceUri { get; init; }
    public string? SourceHash { get; init; }

    public DateTimeOffset? RetrievedUtc { get; init; }
}

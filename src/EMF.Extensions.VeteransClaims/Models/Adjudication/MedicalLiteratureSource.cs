using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class MedicalLiteratureSource
{
    public required MedicalLiteratureSourceId Id { get; init; }

    public required string Title { get; init; }

    public required string Authors { get; init; }

    public required string Publication { get; init; }

    public int? PublicationYear { get; init; }

    public bool VaAffiliated { get; init; }

    public bool VaFunded { get; init; }

    public bool PeerReviewed { get; init; }

    public string? FundingSource { get; init; }

    public string? ResearchOrganization { get; init; }

    public string? Doi { get; init; }

    public string? Pmid { get; init; }

    public string? SourceUri { get; init; }

    public string? SourceHash { get; init; }

    public DateTimeOffset? RetrievedUtc { get; init; }
}

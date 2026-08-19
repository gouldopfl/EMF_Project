using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Regulatory;

public sealed class RegulatoryProvision
{
    public required RegulatoryProvisionId Id { get; init; }

    public required RegulatoryAuthorityId RegulatoryAuthorityId { get; init; }

    public required string ProvisionType { get; init; }

    public required string Citation { get; init; }

    public string? Version { get; init; }

    public DateTimeOffset? EffectiveFrom { get; init; }

    public DateTimeOffset? EffectiveTo { get; init; }

    public string? SourceUri { get; init; }

    public string? SourceHash { get; init; }

    public DateTimeOffset? RetrievedUtc { get; init; }
}

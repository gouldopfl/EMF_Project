using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class IssueDecisionRegulatoryProvision
{
    public required IssueDecisionId IssueDecisionId { get; init; }

    public required RegulatoryProvisionId RegulatoryProvisionId { get; init; }
}

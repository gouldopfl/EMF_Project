using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed class IssueDecisionSubmission
{
    public required IssueDecisionId IssueDecisionId { get; init; }

    public required SubmissionId SubmissionId { get; init; }
}

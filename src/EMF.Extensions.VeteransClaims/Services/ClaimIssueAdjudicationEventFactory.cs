using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Services;

public static class ClaimIssueAdjudicationEventFactory
{

    public static ClaimIssueAdjudicationEvent FromCourtAppeal(
        ClaimIssueCourtAppeal appeal)
    {
        ArgumentNullException.ThrowIfNull(appeal);

        return new ClaimIssueAdjudicationEvent
        {
            ClaimIssueId = appeal.ClaimIssueId,
            EventType =
                ClaimIssueAdjudicationEventTypes.CourtAppeal,
            OccurredAt = appeal.FiledAt,
            ReferenceId = appeal.DocketNumber,
            Description = appeal.Court
        };
    }


    public static ClaimIssueAdjudicationEvent FromCourtDecision(
        ClaimIssueCourtAppeal appeal)
    {
        ArgumentNullException.ThrowIfNull(appeal);

        if (appeal.DecidedAt is null)
            throw new InvalidOperationException(
                "Court appeal has no decision date.");

        return new ClaimIssueAdjudicationEvent
        {
            ClaimIssueId = appeal.ClaimIssueId,
            EventType =
                ClaimIssueAdjudicationEventTypes.CourtDecision,
            OccurredAt = appeal.DecidedAt.Value,
            ReferenceId = appeal.DocketNumber,
            Outcome = appeal.Outcome,
            Description = appeal.Court
        };
    }


    public static ClaimIssueAdjudicationEvent FromRemand(
        ClaimIssueCourtAppeal appeal)
    {
        ArgumentNullException.ThrowIfNull(appeal);

        if (appeal.DecidedAt is null)
            throw new InvalidOperationException(
                "Court appeal has no decision date.");

        if (!string.Equals(
                appeal.Outcome,
                "Remanded",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "Court appeal was not remanded.");
        }

        return new ClaimIssueAdjudicationEvent
        {
            ClaimIssueId = appeal.ClaimIssueId,
            EventType =
                ClaimIssueAdjudicationEventTypes.Remand,
            OccurredAt = appeal.DecidedAt.Value,
            ReferenceId = appeal.DocketNumber,
            Outcome = appeal.Outcome,
            Description = appeal.Court
        };
    }

    public static ClaimIssueAdjudicationEvent FromSubmitted(
        ClaimIssueAdjudicationLifecycleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Submission.SubmittedAt is null)
            throw new InvalidOperationException(
                "Submission has no submitted date.");

        return new ClaimIssueAdjudicationEvent
        {
            ClaimIssueId = entry.ClaimIssueId,
            EventType =
                ClaimIssueAdjudicationEventTypes.SubmissionSubmitted,
            OccurredAt = entry.Submission.SubmittedAt.Value,
            ReferenceId = entry.Submission.Id.Value,
            Description = entry.Submission.SubmissionType
        };
    }

    public static ClaimIssueAdjudicationEvent FromReceived(
        ClaimIssueAdjudicationLifecycleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (entry.Submission.ReceivedAt is null)
            throw new InvalidOperationException(
                "Submission has no received date.");

        return new ClaimIssueAdjudicationEvent
        {
            ClaimIssueId = entry.ClaimIssueId,
            EventType =
                ClaimIssueAdjudicationEventTypes.SubmissionReceived,
            OccurredAt = entry.Submission.ReceivedAt.Value,
            ReferenceId = entry.Submission.Id.Value,
            Description = entry.Submission.SubmissionType
        };
    }

    public static ClaimIssueAdjudicationEvent FromLifecycleEntry(
        ClaimIssueAdjudicationLifecycleEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);

        return new ClaimIssueAdjudicationEvent
        {
            ClaimIssueId = entry.ClaimIssueId,
            EventType =
                ClaimIssueAdjudicationEventTypes.VaDecision,
            OccurredAt =
                entry.VaDecision.DecisionDate,
            ReferenceId =
                entry.IssueDecision.Id.Value,
            Outcome =
                entry.IssueDecision.Outcome,
            Description =
                entry.Submission.SubmissionType
        };
    }
}

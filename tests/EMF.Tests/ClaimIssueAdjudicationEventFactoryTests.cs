using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueAdjudicationEventFactoryTests
{
    [Fact]
    public void FromLifecycleEntry_maps_va_decision()
    {
        var entry =
            new ClaimIssueAdjudicationLifecycleEntry
            {
                ClaimIssueId =
                    new ClaimIssueId("issue-001"),
                Submission =
                    new Submission
                    {
                        Id =
                            new SubmissionId("submission-1"),
                        ClaimId =
                            new ClaimId("claim-001"),
                        SubmissionType =
                            SubmissionTypes.BoardAppeal
                    },
                IssueDecision =
                    new IssueDecision
                    {
                        Id =
                            new IssueDecisionId("issue-decision-1"),
                        VaDecisionId =
                            new VaDecisionId("decision-1"),
                        ClaimIssueId =
                            new ClaimIssueId("issue-001"),
                        Outcome = "Denied"
                    },
                VaDecision =
                    new VaDecision
                    {
                        Id =
                            new VaDecisionId("decision-1"),
                        DecisionDate =
                            new DateTimeOffset(
                                2026, 8, 1, 0, 0, 0,
                                TimeSpan.Zero)
                    }
            };

        var result =
            ClaimIssueAdjudicationEventFactory
                .FromLifecycleEntry(entry);

        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.VaDecision,
            result.EventType);

        Assert.Equal("Denied", result.Outcome);
        Assert.Equal(
            SubmissionTypes.BoardAppeal,
            result.Description);

        Assert.Equal(
            "issue-decision-1",
            result.ReferenceId);
    }

    [Fact]
    public void FromCourtAppeal_maps_filing_event()
    {
        var appeal =
            new ClaimIssueCourtAppeal
            {
                ClaimIssueId =
                    new ClaimIssueId("issue-001"),
                Court = "CAVC",
                FiledAt =
                    new DateTimeOffset(
                        2026, 8, 15, 0, 0, 0,
                        TimeSpan.Zero),
                DocketNumber = "26-1234"
            };

        var result =
            ClaimIssueAdjudicationEventFactory
                .FromCourtAppeal(appeal);

        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.CourtAppeal,
            result.EventType);

        Assert.Equal("26-1234", result.ReferenceId);
        Assert.Equal("CAVC", result.Description);
        Assert.Null(result.Outcome);
    }


    [Fact]
    public void FromCourtDecision_maps_remand_outcome()
    {
        var appeal =
            new ClaimIssueCourtAppeal
            {
                ClaimIssueId =
                    new ClaimIssueId("issue-001"),
                Court = "CAVC",
                FiledAt =
                    new DateTimeOffset(
                        2026, 8, 15, 0, 0, 0,
                        TimeSpan.Zero),
                DocketNumber = "26-1234",
                Outcome = "Remanded",
                DecidedAt =
                    new DateTimeOffset(
                        2027, 2, 1, 0, 0, 0,
                        TimeSpan.Zero)
            };

        var result =
            ClaimIssueAdjudicationEventFactory
                .FromCourtDecision(appeal);

        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.CourtDecision,
            result.EventType);

        Assert.Equal("Remanded", result.Outcome);
        Assert.Equal("26-1234", result.ReferenceId);
        Assert.Equal("CAVC", result.Description);
    }


    [Fact]
    public void FromRemand_maps_explicit_remand_event()
    {
        var appeal =
            new ClaimIssueCourtAppeal
            {
                ClaimIssueId =
                    new ClaimIssueId("issue-001"),
                Court = "CAVC",
                FiledAt =
                    new DateTimeOffset(
                        2026, 8, 15, 0, 0, 0,
                        TimeSpan.Zero),
                DocketNumber = "26-1234",
                Outcome = "Remanded",
                DecidedAt =
                    new DateTimeOffset(
                        2027, 2, 1, 0, 0, 0,
                        TimeSpan.Zero)
            };

        var result =
            ClaimIssueAdjudicationEventFactory
                .FromRemand(appeal);

        Assert.Equal(
            ClaimIssueAdjudicationEventTypes.Remand,
            result.EventType);

        Assert.Equal("Remanded", result.Outcome);
        Assert.Equal("26-1234", result.ReferenceId);
        Assert.Equal("CAVC", result.Description);
    }

}

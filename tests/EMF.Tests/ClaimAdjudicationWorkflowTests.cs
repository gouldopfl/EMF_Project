using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class ClaimAdjudicationWorkflowTests
{
    [Fact]
    public async Task ClaimWorkflow_ComposesMultipleIssueProgression()
    {
        var claimId = new ClaimId("claim-workflow");

        var issue1 =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-workflow-1"),
                ClaimId = claimId,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var issue2 =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-workflow-2"),
                ClaimId = claimId,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran =
                new Veteran
                {
                    Id = new VeteranId("veteran-workflow")
                };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim =
                new Claim
                {
                    Id = claimId,
                    VeteranId = veteran.Id
                };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issues =
                new SqliteClaimIssueRepository(databasePath);

            await issues.AddClaimIssueAsync(issue1);
            await issues.AddClaimIssueAsync(issue2);

            var submissions =
                new SqliteSubmissionRepository(databasePath);

            await submissions.AddSubmissionAsync(
                new Submission
                {
                    Id = new SubmissionId("submission-workflow-1"),
                    ClaimId = claimId,
                    SubmissionType = SubmissionTypes.InitialClaim
                },
                [issue1.Id]);

            await submissions.AddSubmissionAsync(
                new Submission
                {
                    Id = new SubmissionId("submission-workflow-2"),
                    ClaimId = claimId,
                    SubmissionType = SubmissionTypes.SupplementalClaim
                },
                [issue2.Id]);


            var decisions =
                new SqliteVaDecisionRepository(databasePath);

            var decision1 =
                new VaDecision
                {
                    Id = new VaDecisionId("decision-workflow-1"),
                    DecisionDate = new DateTimeOffset(
                        2026, 1, 15, 0, 0, 0, TimeSpan.Zero)
                };

            var issueDecision1 =
                new IssueDecision
                {
                    Id = new IssueDecisionId("issue-decision-workflow-1"),
                    VaDecisionId = decision1.Id,
                    ClaimIssueId = issue1.Id,
                    Outcome = IssueDecisionOutcomes.Denied
                };

            await decisions.AddDecisionAsync(
                decision1,
                [issueDecision1],
                [
                    new IssueDecisionSubmission
                    {
                        IssueDecisionId = issueDecision1.Id,
                        SubmissionId =
                            new SubmissionId("submission-workflow-1")
                    }
                ]);

            var decision2 =
                new VaDecision
                {
                    Id = new VaDecisionId("decision-workflow-2"),
                    DecisionDate = new DateTimeOffset(
                        2026, 6, 15, 0, 0, 0, TimeSpan.Zero)
                };

            var issueDecision2 =
                new IssueDecision
                {
                    Id = new IssueDecisionId("issue-decision-workflow-2"),
                    VaDecisionId = decision2.Id,
                    ClaimIssueId = issue2.Id,
                    Outcome = IssueDecisionOutcomes.Granted
                };

            await decisions.AddDecisionAsync(
                decision2,
                [issueDecision2],
                [
                    new IssueDecisionSubmission
                    {
                        IssueDecisionId = issueDecision2.Id,
                        SubmissionId =
                            new SubmissionId("submission-workflow-2")
                    }
                ]);


            using var output = new StringWriter();

            var exitCode =
                await VeteransConsoleCommand
                    .RunClaimAdjudicationAssessmentAsync(
                        databasePath,
                        claimId,
                        output);

            Assert.Equal(0, exitCode);

            var rendered = output.ToString();

            Assert.Contains(
                "Issues      : 2",
                rendered);

            var first =
                rendered.IndexOf(
                    "2026-01-15T00:00:00.0000000+00:00 " +
                    "issue-workflow-1 VaDecision",
                    StringComparison.Ordinal);

            var second =
                rendered.IndexOf(
                    "2026-06-15T00:00:00.0000000+00:00 " +
                    "issue-workflow-2 VaDecision",
                    StringComparison.Ordinal);

            Assert.True(first >= 0);
            Assert.True(second > first);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}

using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueCurrentDecisionServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsNullWhenNoDecisionsExist()
    {
        var issueId =
            new ClaimIssueId("issue-1");

        var service =
            new ClaimIssueCurrentDecisionService(
                new DecisionRepository());

        var result =
            await service.GetAsync(issueId);

        Assert.Null(result);
    }


    [Fact]
    public async Task GetAsync_ReturnsOnlyDecision()
    {
        var issueId =
            new ClaimIssueId("issue-1");

        var decision =
            new EMF.Extensions.VeteransClaims.Models.Adjudication.IssueDecision
            {
                Id =
                    new EMF.Extensions.VeteransClaims.Models.Identities
                        .IssueDecisionId("issue-decision-1"),
                VaDecisionId =
                    new EMF.Extensions.VeteransClaims.Models.Identities
                        .VaDecisionId("va-decision-1"),
                ClaimIssueId = issueId,
                Outcome =
                    EMF.Extensions.VeteransClaims.Models.Adjudication
                        .IssueDecisionOutcomes.Denied
            };

        var service =
            new ClaimIssueCurrentDecisionService(
                new DecisionRepository(decision));

        var result =
            await service.GetAsync(issueId);

        Assert.NotNull(result);
    }


    [Fact]
    public async Task GetAsync_ReturnsLatestDecisionByDecisionDate()
    {
        var issueId =
            new ClaimIssueId("issue-1");

        var older =
            CreateDecision(
                issueId,
                "issue-decision-1",
                "va-decision-1",
                EMF.Extensions.VeteransClaims.Models.Adjudication
                    .IssueDecisionOutcomes.Denied);

        var newer =
            CreateDecision(
                issueId,
                "issue-decision-2",
                "va-decision-2",
                EMF.Extensions.VeteransClaims.Models.Adjudication
                    .IssueDecisionOutcomes.Granted);

        var service =
            new ClaimIssueCurrentDecisionService(
                new DecisionRepository(newer, older));

        var result =
            await service.GetAsync(issueId);

        Assert.NotNull(result);
        Assert.Equal(newer.Id, result!.IssueDecision.Id);
    }


    [Fact]
    public async Task GetAsync_ThrowsWhenLatestDecisionDateIsAmbiguous()
    {
        var issueId =
            new ClaimIssueId("issue-1");

        var first =
            CreateDecision(
                issueId,
                "issue-decision-1",
                "va-decision-2",
                EMF.Extensions.VeteransClaims.Models.Adjudication
                    .IssueDecisionOutcomes.Denied);

        var second =
            CreateDecision(
                issueId,
                "issue-decision-2",
                "va-decision-2",
                EMF.Extensions.VeteransClaims.Models.Adjudication
                    .IssueDecisionOutcomes.Granted);

        var service =
            new ClaimIssueCurrentDecisionService(
                new DecisionRepository(first, second));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(issueId));
    }

    [Fact]
    public async Task GetAsync_RejectsDecisionForDifferentClaimIssue()
    {
        var requestedIssueId =
            new ClaimIssueId("issue-1");

        var returnedDecision =
            CreateDecision(
                new ClaimIssueId("issue-other"),
                "issue-decision-1",
                "va-decision-1",
                EMF.Extensions.VeteransClaims.Models.Adjudication
                    .IssueDecisionOutcomes.Denied);

        var service =
            new ClaimIssueCurrentDecisionService(
                new DecisionRepository(returnedDecision));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetAsync(requestedIssueId));
    }

    private static
        EMF.Extensions.VeteransClaims.Models.Adjudication.IssueDecision
        CreateDecision(
            ClaimIssueId issueId,
            string id,
            string vaDecisionId,
            string outcome) =>
        new()
        {
            Id =
                new EMF.Extensions.VeteransClaims.Models.Identities
                    .IssueDecisionId(id),
            VaDecisionId =
                new EMF.Extensions.VeteransClaims.Models.Identities
                    .VaDecisionId(vaDecisionId),
            ClaimIssueId = issueId,
            Outcome = outcome
        };



    private sealed class DecisionRepository :
        IVaDecisionRepository
    {
        private readonly IReadOnlyList<
            EMF.Extensions.VeteransClaims.Models.Adjudication.IssueDecision>
            _decisions;

        public DecisionRepository(
            params EMF.Extensions.VeteransClaims.Models.Adjudication.IssueDecision[]
                decisions)
        {
            _decisions = decisions;
        }

        public Task<IReadOnlyList<
            EMF.Extensions.VeteransClaims.Models.Adjudication.IssueDecision>>
            GetIssueDecisionsAsync(
                ClaimIssueId claimIssueId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<
                EMF.Extensions.VeteransClaims.Models.Adjudication.IssueDecision>>(
                _decisions);

        public Task AddDecisionAsync(
            EMF.Extensions.VeteransClaims.Models.Adjudication.VaDecision d,
            IReadOnlyCollection<
                EMF.Extensions.VeteransClaims.Models.Adjudication.IssueDecision> i,
            IReadOnlyCollection<
                EMF.Extensions.VeteransClaims.Models.Adjudication.IssueDecisionSubmission> s,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<
            EMF.Extensions.VeteransClaims.Models.Adjudication.VaDecision?>
            GetDecisionAsync(
                EMF.Extensions.VeteransClaims.Models.Identities.VaDecisionId id,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<
                EMF.Extensions.VeteransClaims.Models.Adjudication.VaDecision?>(
                new EMF.Extensions.VeteransClaims.Models.Adjudication.VaDecision
                {
                    Id = id,
                    DecisionDate =
                        id.Value == "va-decision-2"
                            ? new DateTimeOffset(
                                2026, 6, 15, 0, 0, 0,
                                TimeSpan.Zero)
                            : new DateTimeOffset(
                                2026, 1, 15, 0, 0, 0,
                                TimeSpan.Zero)
                });

        public Task<IReadOnlyList<
            EMF.Extensions.VeteransClaims.Models.Adjudication.IssueDecision>>
            GetIssueDecisionsAsync(
                EMF.Extensions.VeteransClaims.Models.Identities.VaDecisionId id,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<
            EMF.Extensions.VeteransClaims.Models.Identities.SubmissionId>>
            GetSubmissionIdsAsync(
                EMF.Extensions.VeteransClaims.Models.Identities.IssueDecisionId id,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddDecisionArtifactAsync(
            EMF.Extensions.VeteransClaims.Models.Adjudication.VaDecisionArtifact a,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<
            EMF.Core.Models.Identities.ArtifactId>>
            GetArtifactIdsAsync(
                EMF.Extensions.VeteransClaims.Models.Identities.VaDecisionId id,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

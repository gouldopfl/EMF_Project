using System.Reflection;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ClaimIssueAdjudicationDetailsServiceTests
{
    [Fact]
    public async Task GetAsync_ReturnsNullWhenIssueDoesNotExist()
    {
        var service =
            new ClaimIssueAdjudicationDetailsService(
                new MissingClaimIssueRepository(),
                NeverCall<IConditionRepository>(),
                NeverCall<IServiceConnectionRepository>(),
                NeverCall<IClaimIssueEvidenceDetailsService>());

        var result =
            await service.GetAsync(
                new ClaimIssueId("missing"));

        Assert.Null(result);
    }

    private sealed class MissingClaimIssueRepository :
        IClaimIssueRepository
    {
        public Task<ClaimIssue?> GetClaimIssueAsync(
            ClaimIssueId id,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ClaimIssue?>(null);

        public Task<IReadOnlyList<ClaimIssue>> GetClaimIssuesAsync(
            ClaimId id,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task AddClaimIssueAsync(
            ClaimIssue issue,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static T NeverCall<T>()
        where T : class =>
        DispatchProxy.Create<T, NeverCallProxy>();

    private class NeverCallProxy : DispatchProxy
    {
        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            throw new InvalidOperationException(
                $"{targetMethod?.Name} should not have been called.");
    }
}

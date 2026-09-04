using System.Reflection;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class ServiceConnectionEvidenceGapServiceTests
{
    [Fact]
    public async Task EnsureGapsAsync_CollectsRequirementsAcrossBases()
    {
        var claimIssueId = new ClaimIssueId("issue-1");

        var basis1 = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-1"),
            ClaimIssueId = claimIssueId,
            ServiceConnectionTheoryId =
                new ServiceConnectionTheoryId("theory-1")
        };

        var basis2 = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-2"),
            ClaimIssueId = claimIssueId,
            ServiceConnectionTheoryId =
                new ServiceConnectionTheoryId("theory-1")
        };

        var requirement1 =
            new RequirementId("requirement-1");

        var requirement2 =
            new RequirementId("requirement-2");

        var requested = new List<RequirementId>();

        var serviceConnections =
            Proxy<IServiceConnectionRepository>(
                (method, args) =>
                    method.Name == "GetServiceConnectionBasesAsync"
                        ? Task.FromResult<
                            IReadOnlyList<ServiceConnectionBasis>>(
                                [basis1, basis2])
                        : method.Name == "GetRequirementIdsAsync"
                            ? Task.FromResult<
                                IReadOnlyList<RequirementId>>(
                                    (ServiceConnectionBasisId)args![0]! ==
                                            basis1.Id
                                        ? [requirement1]
                                        : [requirement2])
                            : throw new NotSupportedException());


        var gaps =
            Proxy<IEvidenceGapService>(
                (method, args) =>
                {
                    if (method.Name != "EnsureGapAsync")
                        throw new NotSupportedException();

                    var requirementId =
                        (RequirementId)args![1]!;

                    requested.Add(requirementId);

                    return Task.FromResult<EvidenceGap?>(
                        new EvidenceGap
                        {
                            Id =
                                new EvidenceGapId(
                                    $"gap-{requirementId.Value}"),
                            ClaimIssueId = claimIssueId,
                            RequirementId = requirementId,
                            Description = "Missing evidence."
                        });
                });

        var service =
            new ServiceConnectionEvidenceGapService(
                serviceConnections,
                gaps);

        var result =
            await service.EnsureGapsAsync(claimIssueId);

        Assert.Equal(2, result.Count);
        Assert.Contains(requirement1, requested);
        Assert.Contains(requirement2, requested);
    }

    [Fact]
    public async Task EnsureGapsAsync_RejectsBasisForDifferentClaimIssue()
    {
        var requested = new ClaimIssueId("issue-1");

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-1"),
            ClaimIssueId = new ClaimIssueId("issue-other"),
            ServiceConnectionTheoryId =
                new ServiceConnectionTheoryId("theory-1")
        };

        var service = new ServiceConnectionEvidenceGapService(
            Proxy<IServiceConnectionRepository>(
                (m, a) => m.Name == "GetServiceConnectionBasesAsync"
                    ? Task.FromResult<IReadOnlyList<ServiceConnectionBasis>>([basis])
                    : throw new NotSupportedException()),
            Proxy<IEvidenceGapService>(
                (m, a) => throw new NotSupportedException()));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.EnsureGapsAsync(requested));
    }

    [Fact]
    public async Task EnsureGapsAsync_DeduplicatesSharedRequirements()
    {
        var claimIssueId = new ClaimIssueId("issue-1");
        var requirementId =
            new RequirementId("requirement-1");

        var bases =
            new[]
            {
                new ServiceConnectionBasis
                {
                    Id = new ServiceConnectionBasisId("basis-1"),
                    ClaimIssueId = claimIssueId,
                    ServiceConnectionTheoryId =
                        new ServiceConnectionTheoryId("theory-1")
                },
                new ServiceConnectionBasis
                {
                    Id = new ServiceConnectionBasisId("basis-2"),
                    ClaimIssueId = claimIssueId,
                    ServiceConnectionTheoryId =
                        new ServiceConnectionTheoryId("theory-1")
                }
            };

        var serviceConnections =
            Proxy<IServiceConnectionRepository>(
                (method, args) =>
                    method.Name == "GetServiceConnectionBasesAsync"
                        ? Task.FromResult<
                            IReadOnlyList<ServiceConnectionBasis>>(
                                bases)
                        : method.Name == "GetRequirementIdsAsync"
                            ? Task.FromResult<
                                IReadOnlyList<RequirementId>>(
                                    [requirementId])
                            : throw new NotSupportedException());

        var callCount = 0;

        var gaps =
            Proxy<IEvidenceGapService>(
                (method, args) =>
                {
                    if (method.Name != "EnsureGapAsync")
                        throw new NotSupportedException();

                    callCount++;

                    return Task.FromResult<EvidenceGap?>(null);
                });

        var service =
            new ServiceConnectionEvidenceGapService(
                serviceConnections,
                gaps);

        await service.EnsureGapsAsync(claimIssueId);

        Assert.Equal(1, callCount);
    }

    [Fact]
    public async Task EnsureGapsAsync_ExcludesSatisfiedRequirements()
    {
        var claimIssueId = new ClaimIssueId("issue-1");
        var missing =
            new RequirementId("requirement-missing");
        var satisfied =
            new RequirementId("requirement-satisfied");

        var basis = new ServiceConnectionBasis
        {
            Id = new ServiceConnectionBasisId("basis-1"),
            ClaimIssueId = claimIssueId,
            ServiceConnectionTheoryId =
                new ServiceConnectionTheoryId("theory-1")
        };

        var serviceConnections =
            Proxy<IServiceConnectionRepository>(
                (method, args) =>
                    method.Name == "GetServiceConnectionBasesAsync"
                        ? Task.FromResult<
                            IReadOnlyList<ServiceConnectionBasis>>(
                                [basis])
                        : method.Name == "GetRequirementIdsAsync"
                            ? Task.FromResult<
                                IReadOnlyList<RequirementId>>(
                                    [missing, satisfied])
                            : throw new NotSupportedException());

        var gaps =
            Proxy<IEvidenceGapService>(
                (method, args) =>
                {
                    var requirementId =
                        (RequirementId)args![1]!;

                    return Task.FromResult<EvidenceGap?>(
                        requirementId == satisfied
                            ? null
                            : new EvidenceGap
                            {
                                Id = new EvidenceGapId("gap-1"),
                                ClaimIssueId = claimIssueId,
                                RequirementId = requirementId,
                                Description = "Missing evidence."
                            });
                });

        var service =
            new ServiceConnectionEvidenceGapService(
                serviceConnections,
                gaps);

        var result =
            await service.EnsureGapsAsync(claimIssueId);

        var gap = Assert.Single(result);
        Assert.Equal(missing, gap.RequirementId);
    }

    private static T Proxy<T>(
        Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler
            { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            Handler!(targetMethod!, args);
    }
}

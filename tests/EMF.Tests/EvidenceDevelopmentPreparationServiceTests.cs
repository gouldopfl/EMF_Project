using System.Reflection;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class EvidenceDevelopmentPreparationServiceTests
{
    [Fact]
    public async Task PrepareAsync_ReturnsNullWhenNoGapsExist()
    {
        var gaps =
            Proxy<IServiceConnectionEvidenceGapService>(
                (method, args) =>
                    Task.FromResult<IReadOnlyList<EvidenceGap>>([]));

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                    Task.FromResult<
                        EvidenceDevelopmentPlanDetails?>(null));

        var service =
            new EvidenceDevelopmentPreparationService(
                gaps,
                plans);

        var result =
            await service.PrepareAsync(
                new EvidenceDevelopmentPlanId("plan-1"),
                new ClaimIssueId("issue-1"),
                "Develop missing evidence.");

        Assert.Null(result);
    }

    [Fact]
    public async Task PrepareAsync_CreatesPlanFromDerivedGaps()
    {
        var claimIssueId = new ClaimIssueId("issue-1");
        var planId =
            new EvidenceDevelopmentPlanId("plan-1");

        var gap1 = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-1"),
            ClaimIssueId = claimIssueId,
            RequirementId = new RequirementId("requirement-1"),
            Description = "Missing evidence."
        };

        var gap2 = new EvidenceGap
        {
            Id = new EvidenceGapId("gap-2"),
            ClaimIssueId = claimIssueId,
            RequirementId = new RequirementId("requirement-2"),
            Description = "Missing evidence."
        };

        CreateEvidenceDevelopmentPlanRequest? captured = null;

        var gaps =
            Proxy<IServiceConnectionEvidenceGapService>(
                (method, args) =>
                    Task.FromResult<IReadOnlyList<EvidenceGap>>(
                        [gap1, gap2]));

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                {
                    if (method.Name ==
                        nameof(
                            IEvidenceDevelopmentPlanService
                                .GetEvidenceDevelopmentPlanAsync))
                    {
                        return Task.FromResult<
                            EvidenceDevelopmentPlanDetails?>(null);
                    }

                    captured =
                        (CreateEvidenceDevelopmentPlanRequest)
                            args![0]!;

                    return Task.FromResult(
                        new EvidenceDevelopmentPlanDetails
                        {
                            Plan = new EvidenceDevelopmentPlan
                            {
                                Id = planId,
                                ClaimIssueId = claimIssueId,
                                Description = "Develop missing evidence."
                            },
                            Requirements = [],
                            EvidenceGaps =
                            [
                                new EvidenceDevelopmentPlanEvidenceGap
                                {
                                    EvidenceDevelopmentPlanId = planId,
                                    EvidenceGapId = gap1.Id
                                },
                                new EvidenceDevelopmentPlanEvidenceGap
                                {
                                    EvidenceDevelopmentPlanId = planId,
                                    EvidenceGapId = gap2.Id
                                }
                            ],
                            Artifacts = [],
                            Executions = [],
                            Results = []
                        });
                });

        var service =
            new EvidenceDevelopmentPreparationService(
                gaps,
                plans);

        var result =
            await service.PrepareAsync(
                planId,
                claimIssueId,
                "Develop missing evidence.");

        Assert.NotNull(result);
        Assert.NotNull(captured);
        Assert.Equal(planId, captured!.PlanId);
        Assert.Equal(claimIssueId, captured.ClaimIssueId);
        Assert.Equal(
            new[] { gap1.Id, gap2.Id },
            captured.EvidenceGapIds);
    }

    [Fact]
    public async Task PrepareAsync_ReturnsExistingPlan()
    {
        var claimIssueId =
            new ClaimIssueId("issue-existing");

        var planId =
            new EvidenceDevelopmentPlanId("plan-existing");

        var existing =
            new EvidenceDevelopmentPlanDetails
            {
                Plan =
                    new EvidenceDevelopmentPlan
                    {
                        Id = planId,
                        ClaimIssueId = claimIssueId,
                        Description = "Existing development plan."
                    },
                Requirements = [],
                EvidenceGaps = [],
                Artifacts = [],
                Executions = [],
                Results = []
            };

        var gaps =
            Proxy<IServiceConnectionEvidenceGapService>(
                (method, args) =>
                    throw new InvalidOperationException(
                        $"{method.Name} should not be called."));

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                {
                    if (method.Name ==
                        nameof(
                            IEvidenceDevelopmentPlanService
                                .GetEvidenceDevelopmentPlanAsync))
                    {
                        return Task.FromResult<
                            EvidenceDevelopmentPlanDetails?>(
                                existing);
                    }

                    throw new InvalidOperationException(
                        $"{method.Name} should not be called.");
                });

        var service =
            new EvidenceDevelopmentPreparationService(
                gaps,
                plans);

        var result =
            await service.PrepareAsync(
                planId,
                claimIssueId,
                "Develop missing evidence.");

        Assert.Same(existing, result);
    }


    [Fact]
    public async Task PrepareAsync_RejectsExistingPlanForDifferentClaimIssue()
    {
        var requestedIssueId =
            new ClaimIssueId("issue-requested");

        var existing =
            new EvidenceDevelopmentPlanDetails
            {
                Plan =
                    new EvidenceDevelopmentPlan
                    {
                        Id =
                            new EvidenceDevelopmentPlanId(
                                "plan-existing"),
                        ClaimIssueId =
                            new ClaimIssueId(
                                "issue-other"),
                        Description = "Existing development plan."
                    },
                Requirements = [],
                EvidenceGaps = [],
                Artifacts = [],
                Executions = [],
                Results = []
            };

        var gaps =
            Proxy<IServiceConnectionEvidenceGapService>(
                (method, args) =>
                    Task.FromResult<IReadOnlyList<EvidenceGap>>(
                        [
                            new EvidenceGap
                            {
                                Id = new EvidenceGapId("gap-1"),
                                ClaimIssueId = requestedIssueId,
                                RequirementId =
                                    new RequirementId(
                                        "requirement-1"),
                                Description = "Missing evidence."
                            }
                        ]));

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                    Task.FromResult<
                        EvidenceDevelopmentPlanDetails?>(
                            existing));

        var service =
            new EvidenceDevelopmentPreparationService(
                gaps,
                plans);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    service.PrepareAsync(
                        existing.Plan.Id,
                        requestedIssueId,
                        "Develop missing evidence."));

        Assert.Equal(
            "Evidence development plan belongs to another claim issue.",
            ex.Message);
    }


    [Fact]
    public async Task PrepareAsync_RejectsExistingPlanWithDifferentIdentity()
    {
        var requestedPlanId =
            new EvidenceDevelopmentPlanId("plan-requested");

        var claimIssueId =
            new ClaimIssueId("issue-1");

        var existing =
            new EvidenceDevelopmentPlanDetails
            {
                Plan = new EvidenceDevelopmentPlan
                {
                    Id = new EvidenceDevelopmentPlanId("plan-returned"),
                    ClaimIssueId = claimIssueId,
                    Description = "Existing development plan."
                },
                Requirements = [],
                EvidenceGaps = [],
                Artifacts = [],
                Executions = [],
                Results = []
            };

        var gaps =
            Proxy<IServiceConnectionEvidenceGapService>(
                (method, args) =>
                    throw new InvalidOperationException(
                        $"{method.Name} should not be called."));

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                    Task.FromResult<
                        EvidenceDevelopmentPlanDetails?>(existing));

        var service =
            new EvidenceDevelopmentPreparationService(gaps, plans);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.PrepareAsync(
                    requestedPlanId,
                    claimIssueId,
                    "Develop missing evidence."));

        Assert.Equal(
            "Evidence development plan identity mismatch.",
            ex.Message);
    }

    [Fact]
    public async Task PrepareAsync_RejectsGapForDifferentClaimIssue()
    {
        var requestedIssueId =
            new ClaimIssueId("issue-requested");

        var gaps =
            Proxy<IServiceConnectionEvidenceGapService>(
                (method, args) =>
                    Task.FromResult<IReadOnlyList<EvidenceGap>>(
                        [
                            new EvidenceGap
                            {
                                Id = new EvidenceGapId("gap-1"),
                                ClaimIssueId =
                                    new ClaimIssueId("issue-other"),
                                RequirementId =
                                    new RequirementId("requirement-1"),
                                Description = "Missing evidence."
                            }
                        ]));

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                {
                    if (method.Name == nameof(
                        IEvidenceDevelopmentPlanService
                            .GetEvidenceDevelopmentPlanAsync))
                    {
                        return Task.FromResult<
                            EvidenceDevelopmentPlanDetails?>(null);
                    }

                    throw new InvalidOperationException(
                        $"{method.Name} should not be called.");
                });

        var service =
            new EvidenceDevelopmentPreparationService(gaps, plans);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.PrepareAsync(
                    new EvidenceDevelopmentPlanId("plan-1"),
                    requestedIssueId,
                    "Develop missing evidence."));

        Assert.Equal(
            "Evidence development gap belongs to another claim issue.",
            ex.Message);
    }

    [Theory]
    [InlineData(
        "plan-returned",
        "issue-requested",
        "Created evidence development plan identity mismatch.")]
    [InlineData(
        "plan-requested",
        "issue-returned",
        "Created evidence development plan belongs to another claim issue.")]
    public async Task PrepareAsync_RejectsMismatchedCreatedPlan(
        string returnedPlanValue,
        string returnedIssueValue,
        string expectedMessage)
    {
        var requestedPlanId =
            new EvidenceDevelopmentPlanId("plan-requested");
        var requestedIssueId =
            new ClaimIssueId("issue-requested");
        var gapId = new EvidenceGapId("gap-1");

        var gaps =
            Proxy<IServiceConnectionEvidenceGapService>(
                (method, args) =>
                    Task.FromResult<IReadOnlyList<EvidenceGap>>(
                        [
                            new EvidenceGap
                            {
                                Id = gapId,
                                ClaimIssueId = requestedIssueId,
                                RequirementId =
                                    new RequirementId("requirement-1"),
                                Description = "Missing evidence."
                            }
                        ]));

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                {
                    if (method.Name == nameof(
                        IEvidenceDevelopmentPlanService
                            .GetEvidenceDevelopmentPlanAsync))
                    {
                        return Task.FromResult<
                            EvidenceDevelopmentPlanDetails?>(null);
                    }

                    var returnedPlanId =
                        new EvidenceDevelopmentPlanId(
                            returnedPlanValue);

                    return Task.FromResult(
                        new EvidenceDevelopmentPlanDetails
                        {
                            Plan = new EvidenceDevelopmentPlan
                            {
                                Id = returnedPlanId,
                                ClaimIssueId =
                                    new ClaimIssueId(
                                        returnedIssueValue),
                                Description =
                                    "Develop missing evidence."
                            },
                            Requirements = [],
                            EvidenceGaps =
                            [
                                new EvidenceDevelopmentPlanEvidenceGap
                                {
                                    EvidenceDevelopmentPlanId =
                                        returnedPlanId,
                                    EvidenceGapId = gapId
                                }
                            ],
                            Artifacts = [],
                            Executions = [],
                            Results = []
                        });
                });

        var service =
            new EvidenceDevelopmentPreparationService(gaps, plans);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.PrepareAsync(
                    requestedPlanId,
                    requestedIssueId,
                    "Develop missing evidence."));

        Assert.Equal(expectedMessage, ex.Message);
    }

    [Theory]
    [InlineData(
        "plan-other",
        "gap-expected",
        "Created evidence development gap association has a different plan identity.")]
    [InlineData(
        "plan-requested",
        "gap-unexpected",
        "Created evidence development plan returned unexpected evidence gaps.")]
    public async Task PrepareAsync_RejectsMismatchedCreatedGap(
        string associationPlanValue,
        string returnedGapValue,
        string expectedMessage)
    {
        var claimIssueId = new ClaimIssueId("issue-1");
        var planId =
            new EvidenceDevelopmentPlanId("plan-requested");
        var expectedGapId =
            new EvidenceGapId("gap-expected");

        var gaps =
            Proxy<IServiceConnectionEvidenceGapService>(
                (method, args) =>
                    Task.FromResult<IReadOnlyList<EvidenceGap>>(
                        [
                            new EvidenceGap
                            {
                                Id = expectedGapId,
                                ClaimIssueId = claimIssueId,
                                RequirementId =
                                    new RequirementId("requirement-1"),
                                Description = "Missing evidence."
                            }
                        ]));

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                {
                    if (method.Name == nameof(
                        IEvidenceDevelopmentPlanService
                            .GetEvidenceDevelopmentPlanAsync))
                    {
                        return Task.FromResult<
                            EvidenceDevelopmentPlanDetails?>(null);
                    }

                    return Task.FromResult(
                        new EvidenceDevelopmentPlanDetails
                        {
                            Plan = new EvidenceDevelopmentPlan
                            {
                                Id = planId,
                                ClaimIssueId = claimIssueId,
                                Description =
                                    "Develop missing evidence."
                            },
                            Requirements = [],
                            EvidenceGaps =
                            [
                                new EvidenceDevelopmentPlanEvidenceGap
                                {
                                    EvidenceDevelopmentPlanId =
                                        new EvidenceDevelopmentPlanId(
                                            associationPlanValue),
                                    EvidenceGapId =
                                        new EvidenceGapId(
                                            returnedGapValue)
                                }
                            ],
                            Artifacts = [],
                            Executions = [],
                            Results = []
                        });
                });

        var service =
            new EvidenceDevelopmentPreparationService(gaps, plans);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.PrepareAsync(
                    planId,
                    claimIssueId,
                    "Develop missing evidence."));

        Assert.Equal(expectedMessage, ex.Message);
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

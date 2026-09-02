using System.Reflection;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class EvidenceDevelopmentPlanExecutionServiceTests
{
    [Fact]
    public async Task ExecuteAsync_ReturnsNullWhenPlanDoesNotExist()
    {
        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                    Task.FromResult<EvidenceDevelopmentPlanDetails?>(null));

        var workflow =
            Proxy<IEvidenceDevelopmentWorkflowCoordinator>(
                (method, args) =>
                    throw new InvalidOperationException(
                        $"{method.Name} should not be called."));

        var service =
            new EvidenceDevelopmentPlanExecutionService(
                plans,
                workflow);

        var result =
            await service.ExecuteAsync(
                new EvidenceDevelopmentPlanId("missing"));

        Assert.Null(result);
    }

    [Fact]
    public async Task ExecuteAsync_StartsEachLinkedGap()
    {
        var planId =
            new EvidenceDevelopmentPlanId("plan-1");

        var gap1 = new EvidenceGapId("gap-1");
        var gap2 = new EvidenceGapId("gap-2");

        var details =
            new EvidenceDevelopmentPlanDetails
            {
                Plan = new EvidenceDevelopmentPlan
                {
                    Id = planId,
                    ClaimIssueId = new ClaimIssueId("issue-1"),
                    Description = "Develop evidence."
                },
                Requirements = [],
                EvidenceGaps =
                [
                    new EvidenceDevelopmentPlanEvidenceGap
                    {
                        EvidenceDevelopmentPlanId = planId,
                        EvidenceGapId = gap1
                    },
                    new EvidenceDevelopmentPlanEvidenceGap
                    {
                        EvidenceDevelopmentPlanId = planId,
                        EvidenceGapId = gap2
                    }
                ],
                GapDetails =
                [
                    new EvidenceGap
                    {
                        Id = gap1,
                        ClaimIssueId = new ClaimIssueId("issue-1"),
                        RequirementId =
                            new RequirementId("requirement-1"),
                        Description = "First gap.",
                        Status = EvidenceGapStatuses.Open
                    },
                    new EvidenceGap
                    {
                        Id = gap2,
                        ClaimIssueId = new ClaimIssueId("issue-1"),
                        RequirementId =
                            new RequirementId("requirement-2"),
                        Description = "Second gap.",
                        Status = EvidenceGapStatuses.Open
                    }
                ],
                Artifacts = [],
                Executions = [],
                Results = []
            };

        var started = new List<EvidenceGapId>();

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                    Task.FromResult<EvidenceDevelopmentPlanDetails?>(
                        details));

        var workflow =
            Proxy<IEvidenceDevelopmentWorkflowCoordinator>(
                (method, args) =>
                {
                    var gapId = (EvidenceGapId)args![1]!;
                    started.Add(gapId);

                    return Task.FromResult(
                        new EvidenceDevelopmentExecution
                        {
                            EvidenceDevelopmentPlanId = planId,
                            EvidenceGapId = gapId,
                            WorkflowId =
                                new EMF.Core.Models.Identities.WorkflowId(
                                    $"workflow-{gapId.Value}")
                        });
                });

        var service =
            new EvidenceDevelopmentPlanExecutionService(
                plans,
                workflow);

        var result =
            await service.ExecuteAsync(planId);

        Assert.NotNull(result);
        Assert.Equal(2, result!.Count);
        Assert.Equal(new[] { gap1, gap2 }, started);
    }

    [Fact]
    public async Task ExecuteAsync_SkipsResolvedGaps()
    {
        var planId =
            new EvidenceDevelopmentPlanId("plan-resolved");

        var openGap =
            new EvidenceGapId("gap-open");

        var resolvedGap =
            new EvidenceGapId("gap-resolved");

        var details =
            new EvidenceDevelopmentPlanDetails
            {
                Plan = new EvidenceDevelopmentPlan
                {
                    Id = planId,
                    ClaimIssueId =
                        new ClaimIssueId("issue-resolved"),
                    Description = "Develop evidence."
                },
                Requirements = [],
                EvidenceGaps =
                [
                    new EvidenceDevelopmentPlanEvidenceGap
                    {
                        EvidenceDevelopmentPlanId = planId,
                        EvidenceGapId = openGap
                    },
                    new EvidenceDevelopmentPlanEvidenceGap
                    {
                        EvidenceDevelopmentPlanId = planId,
                        EvidenceGapId = resolvedGap
                    }
                ],
                GapDetails =
                [
                    new EvidenceGap
                    {
                        Id = openGap,
                        ClaimIssueId =
                            new ClaimIssueId("issue-resolved"),
                        RequirementId =
                            new RequirementId("req-open"),
                        Description = "Open gap.",
                        Status = EvidenceGapStatuses.Open
                    },
                    new EvidenceGap
                    {
                        Id = resolvedGap,
                        ClaimIssueId =
                            new ClaimIssueId("issue-resolved"),
                        RequirementId =
                            new RequirementId("req-resolved"),
                        Description = "Resolved gap.",
                        Status = EvidenceGapStatuses.Resolved
                    }
                ],
                Artifacts = [],
                Executions = [],
                Results = []
            };

        var started = new List<EvidenceGapId>();

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                    Task.FromResult<EvidenceDevelopmentPlanDetails?>(
                        details));

        var workflow =
            Proxy<IEvidenceDevelopmentWorkflowCoordinator>(
                (method, args) =>
                {
                    var gapId = (EvidenceGapId)args![1]!;
                    started.Add(gapId);

                    return Task.FromResult(
                        new EvidenceDevelopmentExecution
                        {
                            EvidenceDevelopmentPlanId = planId,
                            EvidenceGapId = gapId,
                            WorkflowId =
                                new EMF.Core.Models.Identities.WorkflowId(
                                    $"workflow-{gapId.Value}")
                        });
                });

        var service =
            new EvidenceDevelopmentPlanExecutionService(
                plans,
                workflow);

        var result =
            await service.ExecuteAsync(planId);

        Assert.NotNull(result);
        Assert.Single(result!);
        Assert.Equal(openGap, Assert.Single(started));
    }

    [Fact]
    public async Task ExecuteAsync_ReusesExistingExecution()
    {
        var planId =
            new EvidenceDevelopmentPlanId("plan-existing");

        var gapId =
            new EvidenceGapId("gap-existing");

        var existing =
            new EvidenceDevelopmentExecution
            {
                EvidenceDevelopmentPlanId = planId,
                EvidenceGapId = gapId,
                WorkflowId =
                    new EMF.Core.Models.Identities.WorkflowId(
                        "workflow-existing")
            };

        var details =
            new EvidenceDevelopmentPlanDetails
            {
                Plan =
                    new EvidenceDevelopmentPlan
                    {
                        Id = planId,
                        ClaimIssueId =
                            new ClaimIssueId("issue-existing"),
                        Description = "Develop evidence."
                    },
                Requirements = [],
                EvidenceGaps =
                [
                    new EvidenceDevelopmentPlanEvidenceGap
                    {
                        EvidenceDevelopmentPlanId = planId,
                        EvidenceGapId = gapId
                    }
                ],
                GapDetails =
                [
                    new EvidenceGap
                    {
                        Id = gapId,
                        ClaimIssueId =
                            new ClaimIssueId("issue-existing"),
                        RequirementId =
                            new RequirementId("requirement-existing"),
                        Description = "Existing gap.",
                        Status = EvidenceGapStatuses.Open
                    }
                ],
                Artifacts = [],
                Executions = [existing],
                Results = []
            };

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                    Task.FromResult<
                        EvidenceDevelopmentPlanDetails?>(
                            details));

        var workflow =
            Proxy<IEvidenceDevelopmentWorkflowCoordinator>(
                (method, args) =>
                    throw new InvalidOperationException(
                        $"{method.Name} should not be called."));

        var service =
            new EvidenceDevelopmentPlanExecutionService(
                plans,
                workflow);

        var result =
            await service.ExecuteAsync(planId);

        Assert.NotNull(result);
        Assert.Same(existing, Assert.Single(result!));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsMissingGapDetailsBeforeStartingWorkflows()
    {
        var planId =
            new EvidenceDevelopmentPlanId("plan-missing-gap");

        var presentGapId =
            new EvidenceGapId("gap-present");

        var missingGapId =
            new EvidenceGapId("gap-missing");

        var details =
            new EvidenceDevelopmentPlanDetails
            {
                Plan =
                    new EvidenceDevelopmentPlan
                    {
                        Id = planId,
                        ClaimIssueId =
                            new ClaimIssueId("issue-missing-gap"),
                        Description = "Develop evidence."
                    },
                Requirements = [],
                EvidenceGaps =
                [
                    new EvidenceDevelopmentPlanEvidenceGap
                    {
                        EvidenceDevelopmentPlanId = planId,
                        EvidenceGapId = presentGapId
                    },
                    new EvidenceDevelopmentPlanEvidenceGap
                    {
                        EvidenceDevelopmentPlanId = planId,
                        EvidenceGapId = missingGapId
                    }
                ],
                GapDetails =
                [
                    new EvidenceGap
                    {
                        Id = presentGapId,
                        ClaimIssueId =
                            new ClaimIssueId("issue-missing-gap"),
                        RequirementId =
                            new RequirementId("requirement-present"),
                        Description = "Present gap.",
                        Status = EvidenceGapStatuses.Open
                    }
                ],
                Artifacts = [],
                Executions = [],
                Results = []
            };

        var plans =
            Proxy<IEvidenceDevelopmentPlanService>(
                (method, args) =>
                    Task.FromResult<
                        EvidenceDevelopmentPlanDetails?>(
                            details));

        var started = 0;

        var workflow =
            Proxy<IEvidenceDevelopmentWorkflowCoordinator>(
                (method, args) =>
                {
                    started++;
                    var gapId = (EvidenceGapId)args![1]!;

                    return Task.FromResult(
                        new EvidenceDevelopmentExecution
                        {
                            EvidenceDevelopmentPlanId = planId,
                            EvidenceGapId = gapId,
                            WorkflowId =
                                new EMF.Core.Models.Identities.WorkflowId(
                                    $"workflow-{gapId.Value}")
                        });
                });

        var service =
            new EvidenceDevelopmentPlanExecutionService(
                plans,
                workflow);

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.ExecuteAsync(planId));

        Assert.Equal(
            "Evidence development plan references a missing evidence gap.",
            exception.Message);

        Assert.Equal(0, started);
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

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

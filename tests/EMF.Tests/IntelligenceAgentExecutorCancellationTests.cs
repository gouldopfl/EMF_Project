using EMF.Intelligence.Models.Identities;
using EMF.Security.Auditing.Models;

namespace EMF.Tests;

public sealed partial class
    IntelligenceAgentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_AuditsCancellation()
    {
        var agentId =
            new AgentId(
                "evidence-review-agent");

        var setup =
            CreateExecutor(agentId);

        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        await Assert.ThrowsAsync<
            OperationCanceledException>(
            () => setup.Executor.ExecuteAsync(
                agentId,
                "review-evidence",
                CreateContext(agentId),
                cancellation.Token));

        Assert.Null(
            setup.Agent.LastObjective);

        var audit =
            Assert.Single(
                setup.AuditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Cancelled,
            audit.Outcome);
    }

    [Fact]
    public async Task ExecuteAsync_AuditsAgentCancellation()
    {
        var agentId =
            new AgentId("evidence-review-agent");

        var setup =
            CreateExecutor(agentId);

        var agentCancellation =
            new OperationCanceledException(
                "Agent cancelled.");

        setup.Agent.Failure = agentCancellation;

        var thrown =
            await Assert.ThrowsAsync<
                OperationCanceledException>(
                () => setup.Executor.ExecuteAsync(
                    agentId,
                    "review-evidence",
                    CreateContext(agentId)));

        Assert.Same(
            agentCancellation,
            thrown);

        Assert.Null(setup.Agent.LastObjective);

        var audit =
            Assert.Single(setup.AuditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Cancelled,
            audit.Outcome);
    }
}

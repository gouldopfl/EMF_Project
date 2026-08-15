using EMF.Intelligence.Models.Identities;

namespace EMF.Tests;

public sealed partial class
    IntelligenceAgentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_DoesNotHideAuditFailure()
    {
        var agentId =
            new AgentId("evidence-review-agent");

        var setup =
            CreateExecutor(agentId);

        var auditFailure =
            new InvalidOperationException(
                "Audit write failed.");

        setup.AuditSink.Failure = auditFailure;

        var thrown =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () => setup.Executor.ExecuteAsync(
                    agentId,
                    "review-evidence",
                    CreateContext(agentId)));

        Assert.Same(auditFailure, thrown);

        Assert.Equal(
            "review-evidence",
            setup.Agent.LastObjective);

        Assert.Empty(setup.AuditSink.Records);
    }
}

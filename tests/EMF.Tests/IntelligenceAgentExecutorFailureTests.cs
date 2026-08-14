using EMF.Intelligence.Models.Identities;
using EMF.Security.Auditing.Models;

namespace EMF.Tests;

public sealed partial class
    IntelligenceAgentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_AuditsAgentFailure()
    {
        var agentId =
            new AgentId(
                "evidence-review-agent");

        var setup =
            CreateExecutor(agentId);

        var failure =
            new InvalidOperationException(
                "Agent failed.");

        setup.Agent.Failure = failure;

        var thrown =
            await Assert.ThrowsAsync<
                InvalidOperationException>(
                () => setup.Executor.ExecuteAsync(
                    agentId,
                    "review-evidence",
                    CreateContext(agentId)));

        Assert.Same(failure, thrown);

        var audit =
            Assert.Single(
                setup.AuditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Failed,
            audit.Outcome);
    }
}

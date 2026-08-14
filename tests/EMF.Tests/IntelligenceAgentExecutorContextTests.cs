using EMF.Intelligence.Models.Identities;

namespace EMF.Tests;

public sealed partial class
    IntelligenceAgentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RejectsMismatchedAgentContext()
    {
        var agentId =
            new AgentId(
                "evidence-review-agent");

        var setup =
            CreateExecutor(agentId);

        var mismatchedContext =
            CreateContext(
                new AgentId(
                    "different-agent"));

        await Assert.ThrowsAsync<
            ArgumentException>(
            () => setup.Executor.ExecuteAsync(
                agentId,
                "review-evidence",
                mismatchedContext));

        Assert.Null(
            setup.Agent.LastObjective);

        Assert.Empty(
            setup.AuditSink.Records);
    }
}

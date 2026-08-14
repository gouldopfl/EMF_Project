using EMF.Intelligence.Agents;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Auditing.Models;

namespace EMF.Tests;

public sealed partial class
    IntelligenceAgentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RejectsWrongAgentResult()
    {
        var agentId =
            new AgentId(
                "evidence-review-agent");

        var setup =
            CreateExecutor(agentId);

        setup.Agent.Result =
            CreateResult(
                new AgentId(
                    "different-agent"));

        await Assert.ThrowsAsync<
            IntelligenceAgentResultValidationException>(
            () => setup.Executor.ExecuteAsync(
                agentId,
                "review-evidence",
                CreateContext(agentId)));

        var audit =
            Assert.Single(
                setup.AuditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Failed,
            audit.Outcome);
    }
}

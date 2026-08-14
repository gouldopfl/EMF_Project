using EMF.Intelligence.Agents;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Auditing.Models;

namespace EMF.Tests;

public sealed partial class
    IntelligenceAgentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RunsConfiguredAgent()
    {
        var agentId =
            new AgentId(
                "evidence-review-agent");

        var agent =
            new TestAgent(
                agentId,
                CreateResult(agentId));

        var auditSink =
            new RecordingAuditSink();

        IIntelligenceAgentExecutor<
            string,
            string> executor =
            new IntelligenceAgentExecutor<
                string,
                string>(
                new IntelligenceAgentRegistry<
                    string,
                    string>([agent]),
                auditSink);

        var context =
            CreateContext(agentId);

        var result =
            await executor.ExecuteAsync(
                agentId,
                "review-evidence",
                context);

        Assert.Same(agent.Result, result);
        Assert.Equal(
            "review-evidence",
            agent.LastObjective);
        Assert.Same(
            context,
            agent.LastContext);

        var audit =
            Assert.Single(auditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Succeeded,
            audit.Outcome);

        Assert.Equal(
            agentId.Value,
            audit.ResourceId);
    }
}

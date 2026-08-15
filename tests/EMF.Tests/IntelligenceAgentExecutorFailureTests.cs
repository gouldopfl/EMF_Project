using EMF.Intelligence.Agents;
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

    [Fact]
    public async Task ExecuteAsync_AuditsUnsuccessfulAgentResult()
    {
        var agentId =
            new AgentId("evidence-review-agent");

        var setup =
            CreateExecutor(agentId);

        var successfulResult =
            setup.Agent.Result;

        setup.Agent.Result =
            new IntelligenceAgentResult<string>
            {
                Success = false,
                Message = "Agent could not complete the objective.",
                AgentId = successfulResult.AgentId,
                CorrelationId = successfulResult.CorrelationId,
                StartedUtc = successfulResult.StartedUtc,
                CompletedUtc = successfulResult.CompletedUtc
            };

        var result =
            await setup.Executor.ExecuteAsync(
                agentId,
                "review-evidence",
                CreateContext(agentId));

        Assert.Same(setup.Agent.Result, result);
        Assert.False(result.Success);
        Assert.Null(result.Output);

        var audit =
            Assert.Single(setup.AuditSink.Records);

        Assert.Equal(
            SecurityAuditOutcome.Failed,
            audit.Outcome);
    }
}

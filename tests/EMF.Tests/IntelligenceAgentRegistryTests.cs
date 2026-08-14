using EMF.Intelligence.Agents;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Tests;

public sealed class IntelligenceAgentRegistryTests
{
    [Fact]
    public void Resolve_ReturnsConfiguredAgent()
    {
        var agent =
            new TestAgent(
                new AgentId(
                    "evidence-review-agent"));

        var registry =
            new IntelligenceAgentRegistry<
                string,
                string>([agent]);

        var resolved =
            registry.Resolve(agent.Id);

        Assert.Same(agent, resolved);
    }

    [Fact]
    public void Resolve_ThrowsForUnconfiguredAgent()
    {
        var agentId =
            new AgentId(
                "missing-agent");

        var registry =
            new IntelligenceAgentRegistry<
                string,
                string>(
                Array.Empty<
                    IIntelligenceAgent<
                        string,
                        string>>());

        var exception =
            Assert.Throws<
                IntelligenceAgentUnavailableException>(
                () => registry.Resolve(agentId));

        Assert.Equal(
            agentId,
            exception.AgentId);
    }

    [Fact]
    public void Constructor_RejectsDuplicateAgentIds()
    {
        var agentId =
            new AgentId(
                "evidence-review-agent");

        IIntelligenceAgent<
            string,
            string>[] agents =
        [
            new TestAgent(agentId),
            new TestAgent(agentId)
        ];

        Assert.Throws<ArgumentException>(
            () =>
                new IntelligenceAgentRegistry<
                    string,
                    string>(agents));
    }

    private sealed class TestAgent :
        IIntelligenceAgent<
            string,
            string>
    {
        public TestAgent(AgentId id)
        {
            Id = id;
        }

        public AgentId Id { get; }

        public Task<
            IntelligenceAgentResult<string>>
            ExecuteAsync(
                string objective,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken =
                    default)
        {
            return Task.FromException<
                IntelligenceAgentResult<string>>(
                new NotSupportedException());
        }
    }
}

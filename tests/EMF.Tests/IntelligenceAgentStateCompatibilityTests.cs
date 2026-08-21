using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.State;

namespace EMF.Tests;

public sealed class IntelligenceAgentStateCompatibilityTests
{
    [Fact]
    public void EnsureSupported_RejectsNewerStoredState()
    {
        var agent = new TestStatefulAgent(2);

        var state = new IntelligenceAgentState
        {
            AgentId = agent.Id,
            StateId = "state-001",
            Version = 3,
            Payload = "{}",
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        Assert.Throws<InvalidOperationException>(
            () => IntelligenceAgentStateCompatibility
                .EnsureSupported(agent, state));
    }

    private sealed class TestStatefulAgent :
        IStatefulIntelligenceAgent
    {
        public TestStatefulAgent(int supportedStateVersion)
        {
            SupportedStateVersion = supportedStateVersion;
        }

        public AgentId Id { get; } =
            new("compatibility-agent");

        public int SupportedStateVersion { get; }
    }

    [Fact]
    public void EnsureSupported_AcceptsSupportedStoredState()
    {
        var agent = new TestStatefulAgent(2);

        var state = new IntelligenceAgentState
        {
            AgentId = agent.Id,
            StateId = "state-002",
            Version = 2,
            Payload = "{}",
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        IntelligenceAgentStateCompatibility
            .EnsureSupported(agent, state);
    }
}

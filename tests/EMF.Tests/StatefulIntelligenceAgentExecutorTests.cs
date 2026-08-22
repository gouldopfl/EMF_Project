using EMF.Intelligence.Agents;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.State;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class StatefulIntelligenceAgentExecutorTests
{
    [Fact]
    public async Task ExecuteAsync_RejectsIncompatibleStoredState()
    {
        var agentId =
            new AgentId("stateful-agent");

        var agent =
            new TestStatefulAgent(
                agentId,
                supportedStateVersion: 2);

        var state =
            new IntelligenceAgentState
            {
                AgentId = agentId,
                StateId = "state-001",
                Version = 3,
                Payload = "{}",
                UpdatedUtc = DateTimeOffset.UtcNow
            };

        var store =
            new RecordingStateStore(state);

        var executor =
            new StatefulIntelligenceAgentExecutor<
                string,
                string>(
                agent,
                store);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "operation-001"),
                new ProtectionClassificationId(
                    "confidential"),
                [],
                agentId);

        await Assert.ThrowsAsync<
            InvalidOperationException>(
            () => executor.ExecuteAsync(
                "objective",
                context,
                "state-001"));

        Assert.False(agent.Executed);
        Assert.Equal(1, store.GetCount);
        Assert.Equal(0, store.SaveCount);
    }

    private sealed class TestStatefulAgent :
        IStatefulIntelligenceAgent<string, string>
    {
        public TestStatefulAgent(
            AgentId id,
            int supportedStateVersion)
        {
            Id = id;
            SupportedStateVersion =
                supportedStateVersion;
        }

        public AgentId Id { get; }

        public int SupportedStateVersion { get; }

        public bool Executed { get; private set; }

        public Task<
            StatefulIntelligenceAgentResult<string>>
            ExecuteAsync(
                string objective,
                IntelligenceExecutionContext context,
                IntelligenceAgentState state,
                CancellationToken cancellationToken =
                    default)
        {
            Executed = true;

            throw new InvalidOperationException(
                "Agent should not execute.");
        }
    }

    private sealed class RecordingStateStore :
        IIntelligenceAgentStateStore
    {
        private readonly
            IntelligenceAgentState? _state;

        public RecordingStateStore(
            IntelligenceAgentState? state)
        {
            _state = state;
        }

        public int GetCount { get; private set; }

        public int SaveCount { get; private set; }

        public Task<IntelligenceAgentState?> GetAsync(
            AgentId agentId,
            string stateId,
            CancellationToken cancellationToken =
                default)
        {
            GetCount++;

            return Task.FromResult(_state);
        }

        public Task SaveAsync(
            IntelligenceAgentState state,
            CancellationToken cancellationToken =
                default)
        {
            SaveCount++;

            return Task.CompletedTask;
        }
    }
}

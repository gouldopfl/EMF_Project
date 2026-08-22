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


    [Fact]
    public async Task ExecuteAsync_PersistsUpdatedState()
    {
        var id = new AgentId("stateful-agent");
        var stored = new IntelligenceAgentState
        {
            AgentId = id,
            StateId = "state-002",
            Version = 2,
            Payload = "{}",
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        var updated = new IntelligenceAgentState
        {
            AgentId = id,
            StateId = "state-002",
            Version = 2,
            Payload = """{"updated":true}""",
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        var agent = new TestStatefulAgent(id, 2)
        {
            Result = new StatefulIntelligenceAgentResult<string>
            {
                Result = new IntelligenceAgentResult<string>
                {
                    Success = true,
                    Output = "done",
                    AgentId = id,
                    CorrelationId = new("operation-002"),
                    StartedUtc = DateTimeOffset.UtcNow,
                    CompletedUtc = DateTimeOffset.UtcNow
                },
                State = updated
            }
        };
        var store = new RecordingStateStore(stored);
        var executor = new StatefulIntelligenceAgentExecutor<string,string>(agent, store);
        var context = new IntelligenceExecutionContext(
            "security-steward",
            new("operation-002"),
            new("confidential"),
            [],
            id);

        await executor.ExecuteAsync("objective", context, "state-002");

        Assert.Same(stored, agent.LastState);
        Assert.Same(updated, store.LastSavedState);
        Assert.Equal(1, store.SaveCount);
    }


    [Fact]
    public async Task ExecuteAsync_RejectsReturnedStateWithDifferentStateId()
    {
        var id = new AgentId("stateful-agent");
        var stored = new IntelligenceAgentState
        {
            AgentId = id,
            StateId = "state-003",
            Version = 2,
            Payload = "{}",
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        var agent = new TestStatefulAgent(id, 2)
        {
            Result = new StatefulIntelligenceAgentResult<string>
            {
                Result = new IntelligenceAgentResult<string>
                {
                    Success = true,
                    Output = "done",
                    AgentId = id,
                    CorrelationId = new("operation-003"),
                    StartedUtc = DateTimeOffset.UtcNow,
                    CompletedUtc = DateTimeOffset.UtcNow
                },
                State = new IntelligenceAgentState
                {
                    AgentId = id,
                    StateId = "wrong-state",
                    Version = 2,
                    Payload = "{}",
                    UpdatedUtc = DateTimeOffset.UtcNow
                }
            }
        };
        var store = new RecordingStateStore(stored);
        var executor = new StatefulIntelligenceAgentExecutor<string,string>(agent, store);
        var context = new IntelligenceExecutionContext(
            "security-steward",
            new("operation-003"),
            new("confidential"),
            [],
            id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync("objective", context, "state-003"));

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

        public IntelligenceAgentState? LastState { get; private set; }

        public StatefulIntelligenceAgentResult<string>? Result { get; set; }

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
            LastState = state;

            if (Result is null)
                throw new InvalidOperationException(
                    "Agent should not execute.");

            return Task.FromResult(Result);
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

        public IntelligenceAgentState? LastSavedState
        { get; private set; }

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
            LastSavedState = state;

            return Task.CompletedTask;
        }
    }
}

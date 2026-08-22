using EMF.Intelligence.Agents;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.State;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class StatefulIntelligenceAgentExecutorTests
{
    [Fact]
    public void Constructor_RequiresAuditSink()
    {
        var constructor =
            typeof(StatefulIntelligenceAgentExecutor<string,string>)
                .GetConstructors()
                .Single();

        var parameters = constructor.GetParameters();

        Assert.Equal(3, parameters.Length);
        Assert.Equal(
            typeof(EMF.Security.Auditing.ISecurityAuditSink),
            parameters[2].ParameterType);
    }

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
                store,
                new RecordingAuditSink());

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
            Revision = 7,
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
        var audit = new RecordingAuditSink();
        var executor = new StatefulIntelligenceAgentExecutor<string,string>(agent, store, audit);
        var context = new IntelligenceExecutionContext(
            "security-steward",
            new("operation-002"),
            new("confidential"),
            [],
            id);

        await executor.ExecuteAsync("objective", context, "state-002");

        Assert.Same(stored, agent.LastState);
        Assert.NotNull(store.LastSavedState);
        Assert.Equal(7, store.LastSavedState.Revision);
        Assert.Equal(updated.Payload, store.LastSavedState.Payload);
        Assert.Equal(1, store.SaveCount);

        var record = Assert.Single(audit.Records);
        Assert.Equal(
            EMF.Security.Auditing.Models.SecurityAuditOutcome.Succeeded,
            record.Outcome);
        Assert.Equal(id.Value, record.ResourceId);
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
        var executor = new StatefulIntelligenceAgentExecutor<string,string>(agent, store, new RecordingAuditSink());
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


    [Fact]
    public async Task ExecuteAsync_RejectsReturnedStateWithDifferentAgentId()
    {
        var id = new AgentId("stateful-agent");
        var stored = new IntelligenceAgentState
        {
            AgentId = id,
            StateId = "state-004",
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
                    CorrelationId = new("operation-004"),
                    StartedUtc = DateTimeOffset.UtcNow,
                    CompletedUtc = DateTimeOffset.UtcNow
                },
                State = new IntelligenceAgentState
                {
                    AgentId = new("other-agent"),
                    StateId = "state-004",
                    Version = 2,
                    Payload = "{}",
                    UpdatedUtc = DateTimeOffset.UtcNow
                }
            }
        };
        var store = new RecordingStateStore(stored);
        var executor = new StatefulIntelligenceAgentExecutor<string,string>(agent, store, new RecordingAuditSink());
        var context = new IntelligenceExecutionContext(
            "security-steward",
            new("operation-004"),
            new("confidential"),
            [],
            id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync("objective", context, "state-004"));

        Assert.Equal(0, store.SaveCount);
    }


    [Fact]
    public async Task ExecuteAsync_RejectsMissingStoredState()
    {
        var id = new AgentId("stateful-agent");
        var agent = new TestStatefulAgent(id, 2);
        var store = new RecordingStateStore(null);
        var executor =
            new StatefulIntelligenceAgentExecutor<string,string>(
                agent,
                store,
                new RecordingAuditSink());
        var context = new IntelligenceExecutionContext(
            "security-steward",
            new("operation-005"),
            new("confidential"),
            [],
            id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(
                "objective",
                context,
                "state-005"));

        Assert.False(agent.Executed);
        Assert.Equal(1, store.GetCount);
        Assert.Equal(0, store.SaveCount);
    }


    [Fact]
    public async Task ExecuteAsync_RejectsReturnedStateWithUnsupportedVersion()
    {
        var id = new AgentId("stateful-agent");
        var stored = new IntelligenceAgentState
        {
            AgentId = id,
            StateId = "state-006",
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
                    CorrelationId = new("operation-006"),
                    StartedUtc = DateTimeOffset.UtcNow,
                    CompletedUtc = DateTimeOffset.UtcNow
                },
                State = new IntelligenceAgentState
                {
                    AgentId = id,
                    StateId = "state-006",
                    Version = 3,
                    Payload = "{}",
                    UpdatedUtc = DateTimeOffset.UtcNow
                }
            }
        };
        var store = new RecordingStateStore(stored);
        var executor =
            new StatefulIntelligenceAgentExecutor<string,string>(
                agent,
                store,
                new RecordingAuditSink());
        var context = new IntelligenceExecutionContext(
            "security-steward",
            new("operation-006"),
            new("confidential"),
            [],
            id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(
                "objective",
                context,
                "state-006"));

        Assert.Equal(0, store.SaveCount);
    }


    [Fact]
    public async Task ExecuteAsync_DoesNotAuditSuccessWhenStateSaveFails()
    {
        var id = new AgentId("stateful-agent");
        var stored = new IntelligenceAgentState
        {
            AgentId = id,
            StateId = "state-007",
            Version = 2,
            Revision = 1,
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
                    CorrelationId = new("operation-007"),
                    StartedUtc = DateTimeOffset.UtcNow,
                    CompletedUtc = DateTimeOffset.UtcNow
                },
                State = stored
            }
        };
        var store = new RecordingStateStore(stored)
        {
            SaveFailure =
                new InvalidOperationException("save failed")
        };
        var audit = new RecordingAuditSink();
        var executor =
            new StatefulIntelligenceAgentExecutor<string,string>(
                agent,
                store,
                audit);
        var context = new IntelligenceExecutionContext(
            "security-steward",
            new("operation-007"),
            new("confidential"),
            [],
            id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => executor.ExecuteAsync(
                "objective",
                context,
                "state-007"));

        Assert.Empty(audit.Records);
    }


    [Fact]
    public async Task ExecuteAsync_DoesNotHideAuditFailure()
    {
        var id = new AgentId("stateful-agent");
        var stored = new IntelligenceAgentState
        {
            AgentId = id,
            StateId = "state-008",
            Version = 2,
            Revision = 1,
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
                    CorrelationId = new("operation-008"),
                    StartedUtc = DateTimeOffset.UtcNow,
                    CompletedUtc = DateTimeOffset.UtcNow
                },
                State = stored
            }
        };
        var store = new RecordingStateStore(stored);
        var auditFailure =
            new InvalidOperationException("audit failed");
        var audit = new RecordingAuditSink
        {
            Failure = auditFailure
        };
        var executor =
            new StatefulIntelligenceAgentExecutor<string,string>(
                agent,
                store,
                audit);
        var context = new IntelligenceExecutionContext(
            "security-steward",
            new("operation-008"),
            new("confidential"),
            [],
            id);

        var thrown =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => executor.ExecuteAsync(
                    "objective",
                    context,
                    "state-008"));

        Assert.Same(auditFailure, thrown);
        Assert.Equal(1, store.SaveCount);
        Assert.Empty(audit.Records);
    }


    [Fact]
    public async Task ExecuteAsync_AuditsAgentFailure()
    {
        var id = new AgentId("stateful-agent");
        var stored = new IntelligenceAgentState
        {
            AgentId = id,
            StateId = "state-009",
            Version = 2,
            Revision = 1,
            Payload = "{}",
            UpdatedUtc = DateTimeOffset.UtcNow
        };

        var failure =
            new InvalidOperationException("agent failed");

        var agent = new TestStatefulAgent(id, 2)
        {
            Failure = failure
        };

        var store = new RecordingStateStore(stored);
        var audit = new RecordingAuditSink();

        var executor =
            new StatefulIntelligenceAgentExecutor<string,string>(
                agent,
                store,
                audit);

        var context = new IntelligenceExecutionContext(
            "security-steward",
            new("operation-009"),
            new("confidential"),
            [],
            id);

        var thrown =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => executor.ExecuteAsync(
                    "objective",
                    context,
                    "state-009"));

        Assert.Same(failure, thrown);

        var record = Assert.Single(audit.Records);

        Assert.Equal(
            SecurityAuditOutcome.Failed,
            record.Outcome);

        Assert.Equal(0, store.SaveCount);
    }


    [Fact]
    public async Task ExecuteAsync_AuditsAgentCancellation()
    {
        var id = new AgentId("stateful-agent");
        var stored = new IntelligenceAgentState
        {
            AgentId = id,
            StateId = "state-010",
            Version = 2,
            Revision = 1,
            Payload = "{}",
            UpdatedUtc = DateTimeOffset.UtcNow
        };
        var agent = new TestStatefulAgent(id, 2)
        {
            Failure = new OperationCanceledException()
        };
        var store = new RecordingStateStore(stored);
        var audit = new RecordingAuditSink();
        var executor =
            new StatefulIntelligenceAgentExecutor<string,string>(
                agent,
                store,
                audit);
        var context = new IntelligenceExecutionContext(
            "security-steward",
            new("operation-010"),
            new("confidential"),
            [],
            id);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => executor.ExecuteAsync(
                "objective",
                context,
                "state-010"));

        var record = Assert.Single(audit.Records);
        Assert.Equal(
            SecurityAuditOutcome.Cancelled,
            record.Outcome);
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

        public Exception? Failure { get; set; }

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

            if (Failure is not null)
                return Task.FromException<
                    StatefulIntelligenceAgentResult<string>>(
                    Failure);

            if (Result is null)
                throw new InvalidOperationException(
                    "Agent should not execute.");

            return Task.FromResult(Result);
        }
    }


    private sealed class RecordingAuditSink :
        ISecurityAuditSink
    {
        public List<SecurityAuditRecord> Records { get; } = [];

        public Exception? Failure { get; set; }

        public Task WriteAsync(
            SecurityAuditRecord record,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
                return Task.FromException(Failure);

            Records.Add(record);
            return Task.CompletedTask;
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

        public Exception? SaveFailure { get; set; }

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

            if (SaveFailure is not null)
                return Task.FromException(SaveFailure);

            return Task.CompletedTask;
        }
    }
}

using EMF.Intelligence.Agents;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed partial class
    IntelligenceAgentExecutorTests
{
    private static IntelligenceExecutionContext
        CreateContext(AgentId agentId)
    {
        return new IntelligenceExecutionContext(
            "security-steward",
            new IntelligenceCorrelationId(
                "operation-001"),
            new ProtectionClassificationId(
                "confidential"),
            [],
            agentId);
    }

    private static IntelligenceAgentResult<string>
        CreateResult(AgentId agentId)
    {
        var startedUtc =
            new DateTimeOffset(
                2026, 8, 14, 15, 0, 0,
                TimeSpan.Zero);

        return new IntelligenceAgentResult<string>
        {
            Success = true,
            Output = "agent-result",
            AgentId = agentId,
            CorrelationId =
                new IntelligenceCorrelationId(
                    "operation-001"),
            StartedUtc = startedUtc,
            CompletedUtc =
                startedUtc.AddSeconds(1)
        };
    }

    private sealed class TestAgent :
        IIntelligenceAgent<
            string,
            string>
    {
        public TestAgent(
            AgentId id,
            IntelligenceAgentResult<string> result)
        {
            Id = id;
            Result = result;
        }

        public AgentId Id { get; }

        public IntelligenceAgentResult<string>
            Result { get; set; }

        public Exception? Failure { get; set; }

        public string? LastObjective
        { get; private set; }

        public IntelligenceExecutionContext? LastContext
        { get; private set; }

        public Task<IntelligenceAgentResult<string>>
            ExecuteAsync(
                string objective,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken =
                    default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            if (Failure is not null)
            {
                return Task.FromException<
                    IntelligenceAgentResult<string>>(
                    Failure);
            }

            LastObjective = objective;
            LastContext = context;

            return Task.FromResult(Result);
        }
    }

    private sealed class RecordingAuditSink :
        ISecurityAuditSink
    {
        public Exception? Failure { get; set; }

        public List<SecurityAuditRecord> Records
        { get; } = [];

        public Task WriteAsync(
            SecurityAuditRecord record,
            CancellationToken cancellationToken =
                default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Failure is not null)
            {
                return Task.FromException(Failure);
            }

            Records.Add(record);

            return Task.CompletedTask;
        }
    }

    private static (
        IntelligenceAgentExecutor<string, string>
            Executor,
        TestAgent Agent,
        RecordingAuditSink AuditSink)
        CreateExecutor(AgentId agentId)
    {
        var agent =
            new TestAgent(
                agentId,
                CreateResult(agentId));

        var auditSink =
            new RecordingAuditSink();

        var executor =
            new IntelligenceAgentExecutor<
                string,
                string>(
                new IntelligenceAgentRegistry<
                    string,
                    string>([agent]),
                auditSink);

        return (executor, agent, auditSink);
    }
}

using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;

namespace EMF.Intelligence.Agents;

internal sealed class IntelligenceAgentAuditWriter
{
    private readonly ISecurityAuditSink _auditSink;

    public IntelligenceAgentAuditWriter(
        ISecurityAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(auditSink);

        _auditSink = auditSink;
    }

    public Task WriteAsync<TResult>(
        AgentId agentId,
        IntelligenceExecutionContext context,
        IntelligenceAgentResult<TResult>? result,
        SecurityAuditOutcome outcome,
        DateTimeOffset occurredUtc)
        where TResult : notnull
    {
        var facts =
            new Dictionary<string, string>(
                StringComparer.Ordinal)
            {
                ["correlationId"] =
                    context.CorrelationId.Value,
                ["protectionClassificationId"] =
                    context
                        .ProtectionClassificationId
                        .Value,
                ["inputArtifactIds"] =
                    string.Join(
                        ",",
                        context.InputArtifactIds
                            .Select(id => id.Value))
            };

        if (result is not null)
        {
            facts["startedUtc"] =
                result.StartedUtc.ToString("O");

            facts["completedUtc"] =
                result.CompletedUtc.ToString("O");

            facts["capabilityExecutionCount"] =
                (result.CapabilityExecutions?.Count ?? 0)
                    .ToString();

            facts["requiresReview"] =
                result.RequiresReview.ToString();

            facts["sourceArtifactIds"] =
                string.Join(
                    ",",
                    result.SourceArtifactIds ??
                        Array.Empty<
                            EMF.Core.Models.Identities
                                .ArtifactId>());
        }

        return _auditSink.WriteAsync(
            new SecurityAuditRecord
            {
                Operation =
                    "IntelligenceAgent.Execute",
                ResourceType =
                    "IntelligenceAgent",
                ResourceId = agentId.Value,
                SubjectId = context.SubjectId,
                Outcome = outcome,
                OccurredUtc = occurredUtc,
                Facts = facts
            },
            CancellationToken.None);
    }
}

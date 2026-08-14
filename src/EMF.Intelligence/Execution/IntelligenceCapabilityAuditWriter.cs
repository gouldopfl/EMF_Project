using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;

namespace EMF.Intelligence.Execution;

internal sealed class IntelligenceCapabilityAuditWriter
{
    private readonly ISecurityAuditSink _auditSink;

    public IntelligenceCapabilityAuditWriter(
        ISecurityAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(auditSink);

        _auditSink = auditSink;
    }

    public Task WriteAsync(
        IntelligenceCapabilityId capabilityId,
        IntelligenceExecutionContext context,
        IntelligenceProviderId? providerId,
        IntelligenceExecutionMetadata? metadata,
        AuthorizationDecision? policyDecision,
        SecurityAuditOutcome outcome,
        DateTimeOffset occurredUtc)
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

        if (context.AgentId.HasValue)
        {
            facts["agentId"] =
                context.AgentId.Value.Value;
        }

        if (metadata is not null)
        {
            facts["engineName"] =
                metadata.EngineName;

            facts["startedUtc"] =
                metadata.StartedUtc.ToString("O");

            facts["completedUtc"] =
                metadata.CompletedUtc.ToString("O");

            if (!string.IsNullOrWhiteSpace(
                    metadata.EngineVersion))
            {
                facts["engineVersion"] =
                    metadata.EngineVersion;
            }

            if (!string.IsNullOrWhiteSpace(
                    metadata.ProviderOperationId))
            {
                facts["providerOperationId"] =
                    metadata.ProviderOperationId;
            }
        }

        return _auditSink.WriteAsync(
            new SecurityAuditRecord
            {
                Operation =
                    "IntelligenceCapability.Execute",
                ResourceType =
                    "IntelligenceCapability",
                ResourceId = capabilityId.Value,
                SubjectId = context.SubjectId,
                PolicyDecision = policyDecision,
                Destination = providerId?.Value,
                Outcome = outcome,
                OccurredUtc = occurredUtc,
                Facts = facts
            },
            CancellationToken.None);
    }
}

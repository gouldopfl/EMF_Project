using EMF.Core.Models.Identities;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Intelligence.Models;

public sealed class IntelligenceExecutionContext
{
    public IntelligenceExecutionContext(
        string subjectId,
        IntelligenceCorrelationId correlationId,
        ProtectionClassificationId
            protectionClassificationId,
        IReadOnlyCollection<ArtifactId>
            inputArtifactIds,
        AgentId? agentId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            subjectId);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            correlationId.Value);

        ArgumentException.ThrowIfNullOrWhiteSpace(
            protectionClassificationId.Value);

        ArgumentNullException.ThrowIfNull(
            inputArtifactIds);

        if (agentId.HasValue)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(
                agentId.Value.Value);
        }

        var artifacts = inputArtifactIds.ToArray();

        if (artifacts.Any(
                artifact =>
                    string.IsNullOrWhiteSpace(
                        artifact.Value)))
        {
            throw new ArgumentException(
                "Input Artifact IDs cannot be empty.",
                nameof(inputArtifactIds));
        }

        if (artifacts.Distinct().Count() !=
            artifacts.Length)
        {
            throw new ArgumentException(
                "Input Artifact IDs must be unique.",
                nameof(inputArtifactIds));
        }

        SubjectId = subjectId;
        CorrelationId = correlationId;
        ProtectionClassificationId =
            protectionClassificationId;
        InputArtifactIds = artifacts;
        AgentId = agentId;
    }

    public string SubjectId { get; }

    public IntelligenceCorrelationId CorrelationId
    { get; }

    public ProtectionClassificationId
        ProtectionClassificationId { get; }

    public IReadOnlyList<ArtifactId> InputArtifactIds
    { get; }

    public AgentId? AgentId { get; }
}

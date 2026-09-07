using System.Text.Json;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Integrity;

namespace EMF.Orchestration.Services;

public sealed record ContainerProcessingDecision
{
    public required bool ShouldProcess { get; init; }

    public required ContentFingerprint Fingerprint { get; init; }
}

public sealed class ContainerProcessingGuard
{
    private readonly IEvidenceRepository _repository;
    private readonly IContentFingerprintService _fingerprints;

    public ContainerProcessingGuard(
        IEvidenceRepository repository,
        IContentFingerprintService fingerprints)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(fingerprints);

        _repository = repository;
        _fingerprints = fingerprints;
    }

    public async Task<ContainerProcessingDecision> EvaluateAsync(
        Artifact artifact,
        ReadOnlyMemory<byte> content,
        string processorId,
        string processorVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentException.ThrowIfNullOrWhiteSpace(processorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(processorVersion);

        cancellationToken.ThrowIfCancellationRequested();

        var actual =
            await _fingerprints.ComputeAsync(
                content,
                cancellationToken);

        if (actual is null ||
            string.IsNullOrWhiteSpace(actual.Algorithm) ||
            string.IsNullOrWhiteSpace(actual.Value))
        {
            throw new InvalidOperationException(
                "Container fingerprint service returned an invalid fingerprint.");
        }

        if (artifact.Fingerprint is not null &&
            (!string.Equals(
                artifact.Fingerprint.Algorithm,
                actual.Algorithm,
                StringComparison.OrdinalIgnoreCase) ||
             !string.Equals(
                artifact.Fingerprint.Value,
                actual.Value,
                StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException(
                "Container content fingerprint does not match the artifact fingerprint.");
        }

        var algorithmKey = Key(processorId, "fingerprintAlgorithm");
        var valueKey = Key(processorId, "fingerprintValue");
        var versionKey = Key(processorId, "version");

        var hasAlgorithm = artifact.Metadata.ContainsKey(algorithmKey);
        var hasValue = artifact.Metadata.ContainsKey(valueKey);
        var hasVersion = artifact.Metadata.ContainsKey(versionKey);

        if (hasAlgorithm || hasValue || hasVersion)
        {
            var algorithm = GetString(artifact, algorithmKey);
            var value = GetString(artifact, valueKey);
            var version = GetString(artifact, versionKey);

            if (!hasAlgorithm || !hasValue || !hasVersion ||
                string.IsNullOrWhiteSpace(algorithm) ||
                string.IsNullOrWhiteSpace(value) ||
                string.IsNullOrWhiteSpace(version))
            {
                throw new InvalidDataException(
                    "Container processing metadata is incomplete.");
            }

            if (string.Equals(
                    algorithm,
                    actual.Algorithm,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    value,
                    actual.Value,
                    StringComparison.OrdinalIgnoreCase) &&
                string.Equals(
                    version,
                    processorVersion,
                    StringComparison.Ordinal))
            {
                return new ContainerProcessingDecision
                {
                    ShouldProcess = false,
                    Fingerprint = actual
                };
            }
        }

        return new ContainerProcessingDecision
        {
            ShouldProcess = true,
            Fingerprint = actual
        };
    }

    public Task MarkProcessedAsync(
        Artifact artifact,
        ContentFingerprint fingerprint,
        string processorId,
        string processorVersion,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);
        ArgumentNullException.ThrowIfNull(fingerprint);
        ArgumentException.ThrowIfNullOrWhiteSpace(processorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(processorVersion);

        return _repository.MergeArtifactMetadataAsync(
            artifact.Id,
            new Dictionary<string, object>
            {
                [Key(processorId, "fingerprintAlgorithm")] =
                    fingerprint.Algorithm,
                [Key(processorId, "fingerprintValue")] =
                    fingerprint.Value,
                [Key(processorId, "version")] =
                    processorVersion
            },
            cancellationToken);
    }

    private static string Key(
        string processorId,
        string suffix) =>
        $"containerProcessing.{processorId}.{suffix}";

    private static string? GetString(
        Artifact artifact,
        string key)
    {
        if (!artifact.Metadata.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            string text => text,
            JsonElement element
                when element.ValueKind == JsonValueKind.String =>
                element.GetString(),
            _ => null
        };
    }
}

using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Security.Authorization;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Models;
using EMF.Security.Storage.Models;

namespace EMF.Security.Storage;

public sealed class ArtifactEnvelopeRewrappingService :
    IArtifactEnvelopeRewrappingService
{
    private readonly IArtifactContentStore _contentStore;
    private readonly IEnvelopeKeyRewrappingService
        _rewrappingService;
    private readonly IAuthorizationPolicy
        _authorizationPolicy;
    private readonly ISecurityAuditSink
        _auditSink;

    public ArtifactEnvelopeRewrappingService(
        IArtifactContentStore contentStore,
        IEnvelopeKeyRewrappingService rewrappingService,
        IAuthorizationPolicy authorizationPolicy,
        ISecurityAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(
            rewrappingService);
        ArgumentNullException.ThrowIfNull(
            authorizationPolicy);
        ArgumentNullException.ThrowIfNull(auditSink);

        _contentStore = contentStore;
        _rewrappingService = rewrappingService;
        _authorizationPolicy = authorizationPolicy;
        _auditSink = auditSink;
    }

    public async Task<ArtifactEnvelopeRewrappingResult>
        RewrapAsync(
            ArtifactEnvelopeRewrappingRequest request,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var authorization =
            await _authorizationPolicy.EvaluateAsync(
                new AuthorizationRequest
                {
                    SubjectId = request.SubjectId,
                    PermissionId =
                        SecurityPermissions
                            .ArtifactEnvelopeRewrap,
                    ArtifactId = request.ArtifactId,
                    ProtectionClassificationId =
                        request.ProtectionClassificationId
                },
                cancellationToken);

        if (authorization != AuthorizationDecision.Allow)
        {
            await WriteAuditAsync(
                request,
                authorization,
                SecurityAuditOutcome.Denied,
                DateTimeOffset.UtcNow);
            throw new UnauthorizedAccessException(
                "Artifact envelope rewrapping was denied.");
        }

        var serialized =
            await _contentStore.ReadAsync(
                request.ArtifactId,
                cancellationToken);

        if (serialized is null)
        {
            return await CreateResultAsync(
                request,
                ArtifactEnvelopeRewrappingOutcome.NotFound,
                null,
                null);
        }

        var envelope =
            ValidateEnvelope(
                JsonSerializer
                    .Deserialize<EncryptedEnvelope>(
                        serialized));

        var rewrapped =
            await _rewrappingService.RewrapAsync(
                envelope,
                cancellationToken);

        ValidateRewrappedEnvelope(
            envelope,
            rewrapped);

        if (ReferenceEquals(envelope, rewrapped) ||
            (envelope.KeyEncryptionKeyId ==
                rewrapped.KeyEncryptionKeyId &&
             envelope.WrappedDataEncryptionKey
                .SequenceEqual(
                    rewrapped.WrappedDataEncryptionKey)))
        {
            return await CreateResultAsync(
                request,
                ArtifactEnvelopeRewrappingOutcome
                    .AlreadyCurrent,
                envelope.KeyEncryptionKeyId,
                rewrapped.KeyEncryptionKeyId);
        }

        var replacement =
            JsonSerializer.SerializeToUtf8Bytes(
                rewrapped);

        await _contentStore.WriteAsync(
            request.ArtifactId,
            replacement,
            cancellationToken);

        return await CreateResultAsync(
            request,
            ArtifactEnvelopeRewrappingOutcome.Updated,
            envelope.KeyEncryptionKeyId,
            rewrapped.KeyEncryptionKeyId);
    }

    private static EncryptedEnvelope ValidateEnvelope(
        EncryptedEnvelope? envelope)
    {
        if (envelope is null ||
            envelope.Ciphertext is null ||
            envelope.Nonce is null ||
            envelope.Nonce.Length == 0 ||
            envelope.AuthenticationTag is null ||
            envelope.AuthenticationTag.Length == 0 ||
            envelope.WrappedDataEncryptionKey is null ||
            envelope.WrappedDataEncryptionKey.Length == 0 ||
            string.IsNullOrWhiteSpace(
                envelope.KeyEncryptionKeyId) ||
            string.IsNullOrWhiteSpace(
                envelope.Algorithm))
        {
            throw new InvalidOperationException(
                "Encrypted artifact envelope is invalid.");
        }

        return envelope;
    }

    private static void ValidateRewrappedEnvelope(
        EncryptedEnvelope original,
        EncryptedEnvelope replacement)
    {
        ValidateEnvelope(replacement);

        if (!original.Ciphertext.SequenceEqual(
                replacement.Ciphertext) ||
            !original.Nonce.SequenceEqual(
                replacement.Nonce) ||
            !original.AuthenticationTag.SequenceEqual(
                replacement.AuthenticationTag) ||
            original.Algorithm != replacement.Algorithm)
        {
            throw new InvalidOperationException(
                "Rewrapping changed protected content metadata.");
        }
    }

    private Task WriteAuditAsync(
        ArtifactEnvelopeRewrappingRequest request,
        AuthorizationDecision? policyDecision,
        SecurityAuditOutcome outcome,
        DateTimeOffset occurredUtc)
    {
        return _auditSink.WriteAsync(
            new SecurityAuditRecord
            {
                Operation =
                    SecurityPermissions
                        .ArtifactEnvelopeRewrap
                        .ToString(),
                ResourceType = "Artifact",
                ResourceId = request.ArtifactId.Value,
                SubjectId = request.SubjectId,
                PolicyDecision = policyDecision,
                Outcome = outcome,
                OccurredUtc = occurredUtc
            },
            CancellationToken.None);
    }

    private async Task<ArtifactEnvelopeRewrappingResult>
        CreateResultAsync(
            ArtifactEnvelopeRewrappingRequest request,
            ArtifactEnvelopeRewrappingOutcome outcome,
            string? previousKeyId,
            string? currentKeyId)
    {
        var result = new ArtifactEnvelopeRewrappingResult
        {
            ArtifactId = request.ArtifactId,
            Outcome = outcome,
            PreviousKeyEncryptionKeyId =
                previousKeyId,
            CurrentKeyEncryptionKeyId =
                currentKeyId,
            CompletedUtc =
                DateTimeOffset.UtcNow
        };

        var facts =
            new Dictionary<string, string>(
                StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(previousKeyId))
        {
            facts["previousKeyEncryptionKeyId"] =
                previousKeyId;
        }

        if (!string.IsNullOrWhiteSpace(currentKeyId))
        {
            facts["currentKeyEncryptionKeyId"] =
                currentKeyId;
        }

        await _auditSink.WriteAsync(
            new SecurityAuditRecord
            {
                Operation =
                    SecurityPermissions
                        .ArtifactEnvelopeRewrap
                        .ToString(),
                ResourceType = "Artifact",
                ResourceId = request.ArtifactId.Value,
                SubjectId = request.SubjectId,
                PolicyDecision =
                    AuthorizationDecision.Allow,
                Outcome = outcome switch
                {
                    ArtifactEnvelopeRewrappingOutcome.Updated =>
                        SecurityAuditOutcome.Succeeded,
                    ArtifactEnvelopeRewrappingOutcome.AlreadyCurrent =>
                        SecurityAuditOutcome.Skipped,
                    ArtifactEnvelopeRewrappingOutcome.NotFound =>
                        SecurityAuditOutcome.Failed,
                    _ => SecurityAuditOutcome.Failed
                },
                OccurredUtc = result.CompletedUtc,
                Facts = facts
            },
            CancellationToken.None);

        return result;
    }
}

using System.Text.Json;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Storage;

namespace EMF.Tests;

public sealed partial class ArtifactEnvelopeRewrappingServiceTests
{
    [Fact]
    public async Task RewrapAsync_AuditsAuthorizationCancellation()
    {
        var cancellation =
            new OperationCanceledException(
                "Authorization cancelled.");

        var audit = new RecordingSecurityAuditSink();

        var service =
            CreateService(
                new MissingContentStore(),
                new TestRewrappingService(),
                new FailingAuthorizationPolicy(cancellation),
                audit);

        var thrown =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.RewrapAsync(
                    CreateRequest(
                        new("artifact-authorization-cancelled"))));

        Assert.Same(cancellation, thrown);

        var record = Assert.Single(audit.Records);
        Assert.Null(record.PolicyDecision);
        Assert.Equal(
            SecurityAuditOutcome.Cancelled,
            record.Outcome);
    }

    [Fact]
    public async Task RewrapAsync_AuditsAuthorizationFailure()
    {
        var failure =
            new InvalidOperationException(
                "Authorization failed.");

        var audit = new RecordingSecurityAuditSink();

        var service =
            CreateService(
                new MissingContentStore(),
                new TestRewrappingService(),
                new FailingAuthorizationPolicy(failure),
                audit);

        var thrown =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RewrapAsync(
                    CreateRequest(
                        new("artifact-authorization-failed"))));

        Assert.Same(failure, thrown);

        var record = Assert.Single(audit.Records);
        Assert.Null(record.PolicyDecision);
        Assert.Equal(
            SecurityAuditOutcome.Failed,
            record.Outcome);
    }

    [Fact]
    public async Task RewrapAsync_AuditsContentReadCancellation()
    {
        var cancellation =
            new OperationCanceledException(
                "Content read cancelled.");

        var audit = new RecordingSecurityAuditSink();

        var service =
            CreateService(
                new FailingReadContentStore(cancellation),
                new TestRewrappingService(),
                new AllowPolicy(),
                audit);

        var thrown =
            await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.RewrapAsync(
                    CreateRequest(
                        new("artifact-read-cancelled"))));

        Assert.Same(cancellation, thrown);

        var record = Assert.Single(audit.Records);
        Assert.Equal(
            AuthorizationDecision.Allow,
            record.PolicyDecision);
        Assert.Equal(
            SecurityAuditOutcome.Cancelled,
            record.Outcome);
    }

    [Fact]
    public async Task RewrapAsync_AuditsContentReadFailure()
    {
        var failure =
            new IOException("Content read failed.");

        var audit = new RecordingSecurityAuditSink();

        var service =
            CreateService(
                new FailingReadContentStore(failure),
                new TestRewrappingService(),
                new AllowPolicy(),
                audit);

        var thrown =
            await Assert.ThrowsAsync<IOException>(
                () => service.RewrapAsync(
                    CreateRequest(
                        new("artifact-read-failed"))));

        Assert.Same(failure, thrown);

        var record = Assert.Single(audit.Records);
        Assert.Equal(
            AuthorizationDecision.Allow,
            record.PolicyDecision);
        Assert.Equal(
            SecurityAuditOutcome.Failed,
            record.Outcome);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RewrapAsync_FailureAuditRetainsKnownKeyIdentities(bool replacementFailure)
    {
        var original = new EncryptedEnvelope
        {
            FormatVersion = 2,
            Ciphertext = [1, 2, 3],
            Nonce = new byte[12],
            AuthenticationTag = new byte[16],
            WrappedDataEncryptionKey = [10],
            KeyEncryptionKeyId = "key/v1",
            Algorithm = "AES-256-GCM"
        };
        var stored = JsonSerializer.SerializeToUtf8Bytes(original);
        var contentStore = new FailingReplacementContentStore(stored);
        var audit = new RecordingSecurityAuditSink();
        var service = CreateService(
            contentStore,
            replacementFailure ? new TestRewrappingService() : new FailingRewrappingService(),
            new AllowPolicy(), audit);
        var artifactId = new ArtifactId("synthetic-key-audit-failure");

        if (replacementFailure)
        {
            var result = await service.RewrapAsync(CreateRequest(artifactId));
            Assert.Equal(EMF.Security.Storage.Models.ArtifactEnvelopeRewrappingOutcome.RequiresReview, result.Outcome);
            Assert.Empty(audit.Records);
            Assert.Equal(stored, await contentStore.ReadAsync(artifactId));
            return;
        }
        var failure = await Record.ExceptionAsync(() => service.RewrapAsync(CreateRequest(artifactId)));

        Assert.NotNull(failure);
        var record = Assert.Single(audit.Records);
        Assert.Equal(SecurityAuditOutcome.Failed, record.Outcome);
        Assert.Equal(artifactId.Value, record.ResourceId);
        Assert.Equal("key/v1", record.Facts["previousKeyEncryptionKeyId"]);
        if (replacementFailure)
            Assert.Equal("key/v2", record.Facts["currentKeyEncryptionKeyId"]);
        else
            Assert.False(record.Facts.ContainsKey("currentKeyEncryptionKeyId"));
        var durable = await contentStore.ReadAsync(artifactId);
        Assert.True(stored.SequenceEqual(durable!));
    }

    private sealed class FailingReadContentStore(
        Exception failure) : IArtifactContentStore
    {
        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromException<byte[]?>(failure);

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class FailingAuthorizationPolicy(
        Exception failure) : IAuthorizationPolicy
    {
        public Task<AuthorizationDecision> EvaluateAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromException<AuthorizationDecision>(failure);
    }
}

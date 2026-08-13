using System.Text.Json;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;
using EMF.Security.Authorization;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Security.Storage;
using EMF.Security.Storage.Models;

namespace EMF.Tests;

public sealed class ArtifactEnvelopeRewrappingServiceTests
{
    [Fact]
    public async Task RewrapAsync_ReplacesStoredEnvelope()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        try
        {
            var artifactId =
                new ArtifactId("artifact-001");

            var original =
                new EncryptedEnvelope
                {
                    Ciphertext = [1, 2, 3],
                    Nonce = [4, 5, 6],
                    AuthenticationTag = [7, 8, 9],
                    WrappedDataEncryptionKey = [10],
                    KeyEncryptionKeyId = "key/v1",
                    Algorithm = "AES-256-GCM"
                };

            var contentStore =
                new FileSystemArtifactContentStore(root);

            await contentStore.WriteAsync(
                artifactId,
                JsonSerializer.SerializeToUtf8Bytes(
                    original));

            var policy = new AllowPolicy();

            var service =
                new ArtifactEnvelopeRewrappingService(
                    contentStore,
                    new TestRewrappingService(),
                    policy);

            var result =
                await service.RewrapAsync(
                    CreateRequest(artifactId));

            Assert.Equal(
                ArtifactEnvelopeRewrappingOutcome.Updated,
                result.Outcome);

            Assert.Equal(
                "key/v1",
                result.PreviousKeyEncryptionKeyId);

            Assert.Equal(
                "key/v2",
                result.CurrentKeyEncryptionKeyId);

            var stored =
                JsonSerializer.Deserialize<
                    EncryptedEnvelope>(
                        await contentStore.ReadAsync(
                            artifactId));

            Assert.NotNull(stored);
            Assert.Equal(
                original.Ciphertext,
                stored!.Ciphertext);

            Assert.Equal(
                [20],
                stored.WrappedDataEncryptionKey);

            Assert.Equal(
                "key/v2",
                stored.KeyEncryptionKeyId);

            Assert.Equal(
                SecurityPermissions.ArtifactEnvelopeRewrap,
                policy.LastRequest!.PermissionId);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }


    [Fact]
    public async Task RewrapAsync_DeniedRequestDoesNotProceed()
    {
        var service =
            new ArtifactEnvelopeRewrappingService(
                new FileSystemArtifactContentStore(
                    Path.GetTempPath()),
                new TestRewrappingService(),
                new DenyPolicy());

        await Assert.ThrowsAsync<
            UnauthorizedAccessException>(
                () => service.RewrapAsync(
                    CreateRequest(
                        new ArtifactId(
                            "denied-artifact"))));
    }
    private static ArtifactEnvelopeRewrappingRequest
        CreateRequest(ArtifactId artifactId)
    {
        return new ArtifactEnvelopeRewrappingRequest
        {
            SubjectId = "security-steward",
            ArtifactId = artifactId,
            ProtectionClassificationId =
                new ProtectionClassificationId(
                    ProtectionClassifications.Confidential)
        };
    }

    private sealed class DenyPolicy : IAuthorizationPolicy
    {
        public Task<AuthorizationDecision> EvaluateAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                AuthorizationDecision.Deny);
        }
    }

    private sealed class AllowPolicy :
        IAuthorizationPolicy
    {
        public AuthorizationRequest? LastRequest
        {
            get;
            private set;
        }

        public Task<AuthorizationDecision> EvaluateAsync(
            AuthorizationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            return Task.FromResult(
                AuthorizationDecision.Allow);
        }
    }

    private sealed class TestRewrappingService :
        IEnvelopeKeyRewrappingService
    {
        public Task<EncryptedEnvelope> RewrapAsync(
            EncryptedEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new EncryptedEnvelope
                {
                    Ciphertext =
                        envelope.Ciphertext.ToArray(),
                    Nonce =
                        envelope.Nonce.ToArray(),
                    AuthenticationTag =
                        envelope.AuthenticationTag.ToArray(),
                    WrappedDataEncryptionKey = [20],
                    KeyEncryptionKeyId = "key/v2",
                    Algorithm = envelope.Algorithm
                });
        }
    }
}

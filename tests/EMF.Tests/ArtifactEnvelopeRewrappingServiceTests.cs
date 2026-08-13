using EMF.Core.Contracts.Storage;
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
    public async Task RewrapAsync_TamperingLeavesEnvelopeUnchanged()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var artifactId = new ArtifactId("artifact-tampered");
            var original = new EncryptedEnvelope
            {
                Ciphertext = [1, 2, 3],
                Nonce = [4, 5, 6],
                AuthenticationTag = [7, 8, 9],
                WrappedDataEncryptionKey = [10],
                KeyEncryptionKeyId = "key/v1",
                Algorithm = "AES-256-GCM"
            };

            var originalBytes =
                JsonSerializer.SerializeToUtf8Bytes(original);
            var contentStore =
                new FileSystemArtifactContentStore(root);

            await contentStore.WriteAsync(
                artifactId,
                originalBytes);

            var service = new ArtifactEnvelopeRewrappingService(
                contentStore,
                new TamperingRewrappingService(),
                new AllowPolicy());

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.RewrapAsync(
                    CreateRequest(artifactId)));

            Assert.Equal(
                originalBytes,
                await contentStore.ReadAsync(artifactId));
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

    public async Task RewrapAsync_ReplacementFailurePreservesEnvelope()
    {
        var artifactId =
            new ArtifactId("artifact-replacement-failure");

        var original = new EncryptedEnvelope
        {
            Ciphertext = [1, 2, 3],
            Nonce = [4, 5, 6],
            AuthenticationTag = [7, 8, 9],
            WrappedDataEncryptionKey = [10],
            KeyEncryptionKeyId = "key/v1",
            Algorithm = "AES-256-GCM"
        };

        var originalBytes =
            JsonSerializer.SerializeToUtf8Bytes(original);

        var contentStore =
            new FailingReplacementContentStore(originalBytes);

        var service = new ArtifactEnvelopeRewrappingService(
            contentStore,
            new TestRewrappingService(),
            new AllowPolicy());

        await Assert.ThrowsAsync<IOException>(
            () => service.RewrapAsync(
                CreateRequest(artifactId)));

        Assert.Equal(
            originalBytes,
            await contentStore.ReadAsync(artifactId));
    }

    [Fact]

    public async Task RewrapAsync_MissingArtifactReturnsNotFound()
    {
        var artifactId = new ArtifactId("artifact-missing");
        var service = new ArtifactEnvelopeRewrappingService(
            new MissingContentStore(),
            new TestRewrappingService(),
            new AllowPolicy());

        var result = await service.RewrapAsync(
            CreateRequest(artifactId));

        Assert.Equal(
            ArtifactEnvelopeRewrappingOutcome.NotFound,
            result.Outcome);
        Assert.Equal(artifactId, result.ArtifactId);
        Assert.Null(result.PreviousKeyEncryptionKeyId);
        Assert.Null(result.CurrentKeyEncryptionKeyId);
    }

    [Fact]

    public async Task RewrapAsync_CurrentEnvelopeSkipsReplacement()
    {
        var artifactId = new ArtifactId("artifact-current");
        var original = new EncryptedEnvelope
        {
            Ciphertext = [1, 2, 3],
            Nonce = [4, 5, 6],
            AuthenticationTag = [7, 8, 9],
            WrappedDataEncryptionKey = [10],
            KeyEncryptionKeyId = "key/v2",
            Algorithm = "AES-256-GCM"
        };

        var originalBytes =
            JsonSerializer.SerializeToUtf8Bytes(original);
        var contentStore =
            new FailingReplacementContentStore(originalBytes);
        var service = new ArtifactEnvelopeRewrappingService(
            contentStore,
            new AlreadyCurrentRewrappingService(),
            new AllowPolicy());

        var result = await service.RewrapAsync(
            CreateRequest(artifactId));

        Assert.Equal(
            ArtifactEnvelopeRewrappingOutcome.AlreadyCurrent,
            result.Outcome);
        Assert.Equal("key/v2",
            result.PreviousKeyEncryptionKeyId);
        Assert.Equal("key/v2",
            result.CurrentKeyEncryptionKeyId);
        Assert.Equal(originalBytes,
            await contentStore.ReadAsync(artifactId));
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

    private sealed class MissingContentStore :
        IArtifactContentStore
    {
        public Task WriteAsync(ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();

        public Task<byte[]?> ReadAsync(ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public Task DeleteAsync(ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException();
    }

    private sealed class AlreadyCurrentRewrappingService :
        IEnvelopeKeyRewrappingService
    {
        public Task<EncryptedEnvelope> RewrapAsync(
            EncryptedEnvelope envelope,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(envelope);
    }


    private sealed class FailingReplacementContentStore :
        IArtifactContentStore
    {
        private readonly byte[] _content;

        public FailingReplacementContentStore(byte[] content)
        {
            _content = content.ToArray();
        }

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            throw new IOException("Replacement failed.");
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<byte[]?>(_content.ToArray());
        }

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException();
        }
    }


    private sealed class TamperingRewrappingService :
        IEnvelopeKeyRewrappingService
    {
        public Task<EncryptedEnvelope> RewrapAsync(
            EncryptedEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(
                new EncryptedEnvelope
                {
                    Ciphertext = [99],
                    Nonce = envelope.Nonce.ToArray(),
                    AuthenticationTag =
                        envelope.AuthenticationTag.ToArray(),
                    WrappedDataEncryptionKey = [20],
                    KeyEncryptionKeyId = "key/v2",
                    Algorithm = envelope.Algorithm
                });
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

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Services;
using EMF.Security.Storage;

namespace EMF.Tests;

public sealed class EncryptedArtifactContentStoreTests
{

    [Fact]
    public async Task ReadAsync_RejectsValidButUnboundEnvelope()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var encryption = new DevelopmentEnvelopeEncryptionService(
                new InMemoryEncryptionKeyProvider(new[]
                {
                    new EncryptionKey { KeyId = "test-key", KeyMaterial = new byte[32] }
                }));
            var envelope = await encryption.EncryptAsync(Encoding.UTF8.GetBytes("synthetic protected content"));
            var inner = new FileSystemArtifactContentStore(root);
            var id = new ArtifactId("wrong-artifact");
            await inner.WriteAsync(id, JsonSerializer.SerializeToUtf8Bytes(envelope));

            var store = new EncryptedArtifactContentStore(inner, encryption);
            await Assert.ThrowsAsync<CryptographicException>(() => store.ReadAsync(id));
            // Explicit legacy migration can still decrypt through the generic API.
            Assert.Equal("synthetic protected content", Encoding.UTF8.GetString(await encryption.DecryptAsync(envelope)));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task WriteAsync_RejectsProviderThatIgnoresIdentityContext()
    {
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var store = new EncryptedArtifactContentStore(
                new FileSystemArtifactContentStore(root), new UnboundProvider());
            await Assert.ThrowsAsync<CryptographicException>(() => store.WriteAsync(
                new ArtifactId("artifact"), Encoding.UTF8.GetBytes("synthetic protected content")));
            Assert.False(Directory.Exists(root));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    private sealed class UnboundProvider : IEnvelopeEncryptionService
    {
        public Task<EncryptedEnvelope> EncryptWithContextAsync(ReadOnlyMemory<byte> plaintext,
            ReadOnlyMemory<byte> authenticatedContext, CancellationToken cancellationToken = default)
            => Task.FromResult(new EncryptedEnvelope
            {
                FormatVersion = EncryptedEnvelopeFormat.CurrentVersion,
                Algorithm = EncryptedEnvelopeFormat.Aes256GcmAlgorithm,
                Ciphertext = Array.Empty<byte>(), Nonce = new byte[12],
                AuthenticationTag = new byte[16], WrappedDataEncryptionKey = new byte[32],
                KeyEncryptionKeyId = "test-key"
            });
        public Task<EncryptedEnvelope> EncryptAsync(ReadOnlyMemory<byte> plaintext,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> DecryptAsync(EncryptedEnvelope envelope,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<byte[]> DecryptWithContextAsync(EncryptedEnvelope envelope,
            ReadOnlyMemory<byte> authenticatedContext, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    [Fact]
    public async Task DeleteAsync_RemovesEncryptedContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var key = new EncryptionKey
            {
                KeyId = "test-key",
                KeyMaterial = new byte[32]
            };

            var store =
                new EncryptedArtifactContentStore(
                    new FileSystemArtifactContentStore(root),
                    new DevelopmentEnvelopeEncryptionService(
                        new InMemoryEncryptionKeyProvider(
                            new[] { key })));

            var id = new ArtifactId("artifact-delete");
            var content = Encoding.UTF8.GetBytes("protected delete");

            await store.WriteAsync(id, content);
            await store.DeleteAsync(id);

            var result = await store.ReadAsync(id);

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }


    [Fact]
    public async Task WriteAsync_ReplacesExistingContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var key = new EncryptionKey
            {
                KeyId = "test-key",
                KeyMaterial = new byte[32]
            };

            var store =
                new EncryptedArtifactContentStore(
                    new FileSystemArtifactContentStore(root),
                    new DevelopmentEnvelopeEncryptionService(
                        new InMemoryEncryptionKeyProvider(
                            new[] { key })));

            var id = new ArtifactId("artifact-replace");

            var first =
                Encoding.UTF8.GetBytes("first protected content");

            var second =
                Encoding.UTF8.GetBytes("second protected content");

            await store.WriteAsync(id, first);
            await store.WriteAsync(id, second);

            var result = await store.ReadAsync(id);

            Assert.Equal(second, result);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }


    [Fact]
    public async Task ReadAsync_RejectsCorruptedEncryptedContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var key = new EncryptionKey
            {
                KeyId = "test-key",
                KeyMaterial = new byte[32]
            };

            var inner =
                new FileSystemArtifactContentStore(root);

            var store =
                new EncryptedArtifactContentStore(
                    inner,
                    new DevelopmentEnvelopeEncryptionService(
                        new InMemoryEncryptionKeyProvider(
                            new[] { key })));

            var id = new ArtifactId("artifact-corrupt");

            await store.WriteAsync(
                id,
                Encoding.UTF8.GetBytes("protected content"));

            await inner.WriteAsync(
                id,
                Encoding.UTF8.GetBytes("corrupted envelope"));

            await Assert.ThrowsAnyAsync<Exception>(
                () => store.ReadAsync(id));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }


    [Fact]
    public async Task WriteThenRead_RoundTripsContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var key = new EncryptionKey
            {
                KeyId = "test-key",
                KeyMaterial = new byte[32]
            };

            var encryption =
                new DevelopmentEnvelopeEncryptionService(
                    new InMemoryEncryptionKeyProvider(
                        new[] { key }));

            var store =
                new EncryptedArtifactContentStore(
                    new FileSystemArtifactContentStore(root),
                    encryption);

            var id = new ArtifactId("artifact-1");
            var content = Encoding.UTF8.GetBytes("protected emf");

            await store.WriteAsync(id, content);

            var result = await store.ReadAsync(id);

            Assert.Equal(content, result);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }


    [Fact]
    public async Task ReadAsync_RejectsEnvelopeFromDifferentArtifact()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var key = new EncryptionKey
            {
                KeyId = "test-key",
                KeyMaterial = new byte[32]
            };

            var inner =
                new FileSystemArtifactContentStore(root);

            var store =
                new EncryptedArtifactContentStore(
                    inner,
                    new DevelopmentEnvelopeEncryptionService(
                        new InMemoryEncryptionKeyProvider(
                            new[] { key })));

            var sourceId =
                new ArtifactId("artifact-source");

            var targetId =
                new ArtifactId("artifact-target");

            await store.WriteAsync(
                sourceId,
                Encoding.UTF8.GetBytes(
                    "source protected content"));

            var sourceEnvelope =
                await inner.ReadAsync(sourceId);

            Assert.NotNull(sourceEnvelope);

            await inner.WriteAsync(
                targetId,
                sourceEnvelope);

            await Assert.ThrowsAnyAsync<CryptographicException>(
                () => store.ReadAsync(targetId));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

}

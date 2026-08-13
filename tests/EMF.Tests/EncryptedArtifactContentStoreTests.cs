using System.Text;
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
}

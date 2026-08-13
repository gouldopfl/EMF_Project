using System.Text;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;

namespace EMF.Tests;

public sealed class FileSystemArtifactContentStoreTests
{

    [Fact]
    public async Task DeleteAsync_RemovesStoredContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var store = new FileSystemArtifactContentStore(root);
            var id = new ArtifactId("artifact-delete");
            var content = Encoding.UTF8.GetBytes("delete me");

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
            var store = new FileSystemArtifactContentStore(root);
            var id = new ArtifactId("artifact-replace");

            var first =
                Encoding.UTF8.GetBytes("first content");

            var second =
                Encoding.UTF8.GetBytes("second content");

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
    public async Task WriteThenRead_RoundTripsContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var store = new FileSystemArtifactContentStore(root);
            var id = new ArtifactId("artifact-1");
            var content = Encoding.UTF8.GetBytes("hello emf");

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

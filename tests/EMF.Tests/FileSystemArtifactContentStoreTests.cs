using System.Text;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;

namespace EMF.Tests;

public sealed class FileSystemArtifactContentStoreTests
{
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

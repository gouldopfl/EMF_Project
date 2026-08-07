using EMF.Discovery.Models;
using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class DiscoveryTests
{
    [Fact]
    public async Task DiscoverAsync_CountsFilesAndDirectories()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"emf-discovery-{Guid.NewGuid():N}");

        Directory.CreateDirectory(rootPath);

        try
        {
            File.WriteAllText(Path.Combine(rootPath, "file1.txt"), "one");

            var childPath = Path.Combine(rootPath, "child");
            Directory.CreateDirectory(childPath);

            File.WriteAllText(
                Path.Combine(childPath, "file2.txt"),
                "two");

            var service = new FileSystemDiscoveryService();

            var statistics = await service.DiscoverAsync(
                rootPath,
                new DiscoveryOptions());

            Assert.Equal(2, statistics.DirectoriesDiscovered);
            Assert.Equal(2, statistics.FilesDiscovered);
            Assert.True(statistics.TotalBytes > 0);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }
}

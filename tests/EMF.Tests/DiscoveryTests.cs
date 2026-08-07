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

    [Fact]
    public async Task DiscoverAsync_WhenRecursiveIsFalse_DoesNotDescendIntoChildDirectories()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"emf-discovery-{Guid.NewGuid():N}");

        Directory.CreateDirectory(rootPath);

        try
        {
            File.WriteAllText(Path.Combine(rootPath, "root.txt"), "root");

            var childPath = Path.Combine(rootPath, "child");
            Directory.CreateDirectory(childPath);

            File.WriteAllText(
                Path.Combine(childPath, "child.txt"),
                "child");

            var service = new FileSystemDiscoveryService();

            var statistics = await service.DiscoverAsync(
                rootPath,
                new DiscoveryOptions
                {
                    Recursive = false
                });

            Assert.Equal(1, statistics.DirectoriesDiscovered);
            Assert.Equal(1, statistics.FilesDiscovered);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }




    [Fact]
    public async Task DiscoverAsync_ByDefault_ExcludesHiddenFiles()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"emf-discovery-{Guid.NewGuid():N}");

        Directory.CreateDirectory(rootPath);

        try
        {
            File.WriteAllText(
                Path.Combine(rootPath, "visible.txt"),
                "visible");

            File.WriteAllText(
                Path.Combine(rootPath, ".hidden.txt"),
                "hidden");

            var service = new FileSystemDiscoveryService();

            var statistics = await service.DiscoverAsync(
                rootPath,
                new DiscoveryOptions());

            Assert.Equal(1, statistics.FilesDiscovered);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }


    [Fact]
    public async Task DiscoverAsync_WhenIncludeHiddenFilesIsTrue_IncludesHiddenFiles()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"emf-discovery-{Guid.NewGuid():N}");

        Directory.CreateDirectory(rootPath);

        try
        {
            File.WriteAllText(
                Path.Combine(rootPath, "visible.txt"),
                "visible");

            File.WriteAllText(
                Path.Combine(rootPath, ".hidden.txt"),
                "hidden");

            var service = new FileSystemDiscoveryService();

            var statistics = await service.DiscoverAsync(
                rootPath,
                new DiscoveryOptions
                {
                    IncludeHiddenFiles = true
                });

            Assert.Equal(2, statistics.FilesDiscovered);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }



    [Fact]
    public async Task DiscoverAsync_WhenSourceDoesNotExist_ThrowsDirectoryNotFoundException()
    {
        var sourcePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-missing-{Guid.NewGuid():N}");

        var service = new FileSystemDiscoveryService();

        await Assert.ThrowsAsync<DirectoryNotFoundException>(
            () => service.DiscoverAsync(
                sourcePath,
                new DiscoveryOptions()));
    }


    [Fact]
    public async Task DiscoverAsync_WhenCancelled_ThrowsOperationCanceledException()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"emf-discovery-{Guid.NewGuid():N}");

        Directory.CreateDirectory(rootPath);

        try
        {
            using var cancellationTokenSource =
                new CancellationTokenSource();

            cancellationTokenSource.Cancel();

            var service = new FileSystemDiscoveryService();

            await Assert.ThrowsAsync<OperationCanceledException>(
                () => service.DiscoverAsync(
                    rootPath,
                    new DiscoveryOptions(),
                    cancellationTokenSource.Token));
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }

}

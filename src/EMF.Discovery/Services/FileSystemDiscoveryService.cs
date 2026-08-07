using System.Diagnostics;
using System.Runtime.CompilerServices;
using EMF.Discovery.Contracts;
using EMF.Discovery.Models;

namespace EMF.Discovery.Services;

public sealed class FileSystemDiscoveryService :
    IDiscoveryService,
    IStreamingDiscoveryService
{
    public Task<DiscoveryStatistics> DiscoverAsync(
        string sourcePath,
        DiscoveryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);

        var rootDirectory = new DirectoryInfo(sourcePath);

        if (!rootDirectory.Exists)
        {
            throw new DirectoryNotFoundException(
                $"The discovery source directory does not exist: {sourcePath}");
        }

        var stopwatch = Stopwatch.StartNew();
        var statistics = new DiscoveryStatistics();
        var directoriesToVisit = new Stack<DirectoryInfo>();
        var visitedDirectories = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        directoriesToVisit.Push(rootDirectory);

        while (directoriesToVisit.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentDirectory = directoriesToVisit.Pop();
            var resolvedPath = ResolveDirectoryPath(currentDirectory);

            if (!visitedDirectories.Add(resolvedPath))
            {
                continue;
            }

            statistics.DirectoriesDiscovered++;

            FileSystemInfo[] entries;

            try
            {
                entries = currentDirectory.GetFileSystemInfos();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!options.IncludeHiddenFiles && IsHidden(entry))
                {
                    continue;
                }

                if (entry is FileInfo file)
                {
                    statistics.FilesDiscovered++;
                    statistics.TotalBytes += file.Length;
                    continue;
                }

                if (entry is not DirectoryInfo directory)
                {
                    continue;
                }

                var isSymbolicLink =
                    directory.Attributes.HasFlag(FileAttributes.ReparsePoint);

                if (isSymbolicLink && !options.FollowSymbolicLinks)
                {
                    continue;
                }

                if (options.Recursive)
                {
                    directoriesToVisit.Push(directory);
                }
            }
        }

        stopwatch.Stop();
        statistics.Elapsed = stopwatch.Elapsed;

        return Task.FromResult(statistics);
    }

    public async IAsyncEnumerable<DiscoveredItem> DiscoverItemsAsync(
        string sourcePath,
        DiscoveryOptions options,
        [EnumeratorCancellation]
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentNullException.ThrowIfNull(options);

        var rootDirectory = new DirectoryInfo(sourcePath);

        if (!rootDirectory.Exists)
        {
            throw new DirectoryNotFoundException(
                $"The discovery source directory does not exist: {sourcePath}");
        }

        var directoriesToVisit = new Stack<DirectoryInfo>();
        var visitedDirectories = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        directoriesToVisit.Push(rootDirectory);

        while (directoriesToVisit.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentDirectory = directoriesToVisit.Pop();
            var resolvedPath = ResolveDirectoryPath(currentDirectory);

            if (!visitedDirectories.Add(resolvedPath))
            {
                continue;
            }

            FileSystemInfo[] entries;

            try
            {
                entries = currentDirectory.GetFileSystemInfos();
            }
            catch (UnauthorizedAccessException)
            {
                continue;
            }
            catch (IOException)
            {
                continue;
            }

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!options.IncludeHiddenFiles && IsHidden(entry))
                {
                    continue;
                }

                if (entry is FileInfo file)
                {
                    yield return new DiscoveredItem
                    {
                        Name = file.Name,
                        SourcePath = file.FullName,
                        SourceType = "file",
                        SizeBytes = file.Length,
                        CreatedUtc = file.CreationTimeUtc,
                        ModifiedUtc = file.LastWriteTimeUtc
                    };

                    await Task.Yield();
                    continue;
                }

                if (entry is not DirectoryInfo directory)
                {
                    continue;
                }

                var isSymbolicLink =
                    directory.Attributes.HasFlag(FileAttributes.ReparsePoint);

                if (isSymbolicLink && !options.FollowSymbolicLinks)
                {
                    continue;
                }

                if (options.Recursive)
                {
                    directoriesToVisit.Push(directory);
                }
            }
        }
    }

    private static bool IsHidden(FileSystemInfo entry)
    {
        return entry.Attributes.HasFlag(FileAttributes.Hidden)
            || entry.Name.StartsWith('.');
    }

    private static string ResolveDirectoryPath(DirectoryInfo directory)
    {
        try
        {
            return directory.ResolveLinkTarget(returnFinalTarget: true)?.FullName
                ?? directory.FullName;
        }
        catch (IOException)
        {
            return directory.FullName;
        }
    }
}

using System.Runtime.CompilerServices;
using EMF.Discovery.Contracts;
using EMF.Discovery.Models;
using EMF.Inventory.Models;

namespace EMF.Orchestration.Services;

public sealed class InventoryBoundedDiscoveryService(InventoryProcessingLimits limits) : IStreamingDiscoveryService
{
    public async IAsyncEnumerable<DiscoveredItem> DiscoverItemsAsync(string sourcePath, DiscoveryOptions options, [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        limits.Validate(); var root = Path.GetFullPath(sourcePath); if (!Directory.Exists(root)) throw new DirectoryNotFoundException(root);
        var pending = new Stack<(string Path, int Depth)>(); var visited = new HashSet<string>(StringComparer.Ordinal); pending.Push((root, 0)); int entries = 0;
        while (pending.TryPop(out var current))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var directory = new DirectoryInfo(current.Path); var identity = directory.ResolveLinkTarget(true)?.FullName ?? directory.FullName;
            if (visited.Contains(identity)) continue;
            if (visited.Count >= limits.MaximumDirectories || current.Depth > limits.MaximumDepth) throw new InvalidDataException("Inventory directory budget exhausted."); visited.Add(identity);
            // Streaming enumeration: no GetFileSystemInfos array before admission.
            using var enumerator = Directory.EnumerateFileSystemEntries(current.Path).GetEnumerator();
            while (enumerator.MoveNext())
            {
                cancellationToken.ThrowIfCancellationRequested(); if (checked(++entries) > limits.MaximumDirectoryEntries) throw new InvalidDataException("Inventory entry budget exhausted.");
                var path = enumerator.Current; var attributes = File.GetAttributes(path);
                if (!options.IncludeHiddenFiles && (attributes.HasFlag(FileAttributes.Hidden) || Path.GetFileName(path).StartsWith('.'))) continue;
                if (attributes.HasFlag(FileAttributes.ReparsePoint) && !options.FollowSymbolicLinks) continue;
                if (attributes.HasFlag(FileAttributes.Directory))
                { if (options.Recursive) { if (pending.Count >= limits.MaximumDirectories) throw new InvalidDataException("Inventory directory queue exhausted."); pending.Push((path, current.Depth + 1)); } continue; }
                var file = new FileInfo(path);
                yield return new() { Name = file.Name, SourcePath = file.FullName, SourceType = "file", SizeBytes = file.Length, CreatedUtc = file.CreationTimeUtc, ModifiedUtc = file.LastWriteTimeUtc };
                await Task.Yield();
            }
        }
    }
}

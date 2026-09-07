namespace EMF.ArchitectureAuditor;

public sealed class SourceInventory
{
    public const long DefaultMaxFileBytes = 5L * 1024 * 1024;
    public const int DefaultMaxFileCount = 10_000;
    public const long DefaultMaxTotalBytes = 250L * 1024 * 1024;

    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".git",
            ".vs",
            "bin",
            "obj",
            "node_modules"
        };

    private readonly long _maxFileBytes;
    private readonly int _maxFileCount;
    private readonly long _maxTotalBytes;

    public SourceInventory(
        long maxFileBytes = DefaultMaxFileBytes,
        int maxFileCount = DefaultMaxFileCount,
        long maxTotalBytes = DefaultMaxTotalBytes)
    {
        if (maxFileBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));

        if (maxFileCount <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFileCount));

        if (maxTotalBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxTotalBytes));

        _maxFileBytes = maxFileBytes;
        _maxFileCount = maxFileCount;
        _maxTotalBytes = maxTotalBytes;
    }

    public IReadOnlyList<SourceFile> Discover(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        var root = Path.GetFullPath(repositoryRoot);

        if (!Directory.Exists(root))
            throw new DirectoryNotFoundException(root);

        var files = new List<SourceFile>();
        long totalBytes = 0;

        foreach (var path in Enumerate(root))
        {
            var info = new FileInfo(path);

            if (info.Length > _maxFileBytes)
                throw new InvalidDataException(
                    $"Source file exceeds the maximum allowed size: {path}");

            totalBytes = checked(totalBytes + info.Length);

            if (totalBytes > _maxTotalBytes)
                throw new InvalidDataException(
                    "Source inventory exceeds the maximum aggregate size.");

            files.Add(
                new SourceFile(
                    Path.GetRelativePath(root, path),
                    path,
                    info.Length));

            if (files.Count > _maxFileCount)
                throw new InvalidDataException(
                    "Source inventory exceeds the maximum file count.");
        }

        return files
            .OrderBy(x => x.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    private static IEnumerable<string> Enumerate(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();

            foreach (var file in Directory.EnumerateFiles(
                         directory,
                         "*.cs",
                         SearchOption.TopDirectoryOnly))
            {
                yield return file;
            }

            foreach (var child in Directory.EnumerateDirectories(
                         directory,
                         "*",
                         SearchOption.TopDirectoryOnly))
            {
                if (!ExcludedDirectories.Contains(
                        Path.GetFileName(child)))
                {
                    pending.Push(child);
                }
            }
        }
    }
}

public sealed record SourceFile(
    string RelativePath,
    string FullPath,
    long SizeBytes);

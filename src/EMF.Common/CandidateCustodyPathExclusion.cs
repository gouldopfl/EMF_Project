using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace EMF.Infrastructure;

// Shared deny-only source, linked into path-owning assemblies without adding
// foundational dependencies. This grants no namespace access or mutation right.
internal static class CandidateCustodyPathExclusion
{
    internal static void Reject(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var resolved = Inspect(path, new HashSet<string>(OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal), 0);
        if (OperatingSystem.IsLinux()) PinExistingDirectories(Directory.Exists(resolved) ? resolved : Path.GetDirectoryName(resolved)!);
    }
    private static string Inspect(string path, HashSet<string> visited, int links)
    {
        if (links > 40) throw new IOException("Filesystem alias depth exceeds safe bounds.");
        var full = Path.GetFullPath(path);
        RejectComponent(full);
        var traversal = Path.IsPathFullyQualified(path) ? path : Path.Combine(Environment.CurrentDirectory, path);
        var parts = traversal[Path.GetPathRoot(traversal)!.Length..].Split(
            new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
        var current = Path.GetPathRoot(full)!;
        foreach (var part in parts)
        {
            if (part == ".") continue;
            if (part == "..") { current = Path.GetDirectoryName(current) ?? Path.GetPathRoot(current)!; continue; }
            current = Path.Combine(current, part);
            // Follow only for inspection, never for mutation. Ordinary nonreserved
            // aliases remain permitted; reserved or unresolvable aliases fail closed.
            while (true)
            {
                var info = (FileSystemInfo)new DirectoryInfo(current);
                var target = info.LinkTarget ?? new FileInfo(current).LinkTarget;
                if (target is null) break;
                if (++links > 40 || !visited.Add(current)) throw new IOException("Filesystem alias cannot be safely resolved.");
                var alias = current;
                current = Path.IsPathRooted(target) ? target : Path.Combine(Path.GetDirectoryName(current)!, target);
                RejectComponent(current);
                // Resolve aliased ancestry too, including a link to an ordinary
                // descendant whose ancestor is itself a custody alias.
                current = Inspect(current, visited, links);
                visited.Remove(alias);
            }
            RejectComponent(current);
        }
        return current;
    }
    internal static void RejectSqlite(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        if (path == ":memory:" || path.Length == 0) return;
        // SQLite URI filenames can hide components using percent encoding.
        if (path.StartsWith("file:", StringComparison.OrdinalIgnoreCase))
        {
            var filename = path[5..].Split(new[] { '?', '#' }, 2)[0];
            if (filename.StartsWith("//", StringComparison.Ordinal))
            {
                var slash = filename.IndexOf('/', 2);
                var authority = slash < 0 ? filename[2..] : filename[2..slash];
                if (authority.Length == 0 || authority.Equals("localhost", StringComparison.OrdinalIgnoreCase))
                    filename = slash < 0 ? "/" : filename[slash..];
                else if (!OperatingSystem.IsWindows())
                    throw new IOException("SQLite URI authority is unsupported on this platform.");
            }
            if (filename == ":memory:") return;
            Reject(Uri.UnescapeDataString(filename));
            return;
        }
        Reject(path);
    }
    private static void PinExistingDirectories(string path)
    {
        using var root = new SafeFileHandle((IntPtr)open("/", 0x10000 | 0x20000 | 0x80000), true);
        if (root.IsInvalid) throw new IOException("Cannot pin generic writer ancestry.");
        SafeFileHandle? current = null;
        try
        {
            foreach (var component in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                var fd = openat((current ?? root).DangerousGetHandle().ToInt32(), component, 0x10000 | 0x20000 | 0x80000, 0);
                if (fd < 0 && Marshal.GetLastPInvokeError() == 2) break;
                if (fd < 0) throw new IOException("Resolved generic writer ancestry is not a no-follow directory.");
                var next = new SafeFileHandle((IntPtr)fd, true); current?.Dispose(); current = next;
            }
        }
        finally { current?.Dispose(); }
    }
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", SetLastError = true)] private static extern int openat(int directory, string path, int flags, int mode);
    private static void RejectComponent(string path)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (path.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(component => string.Equals(component, "zip-candidate-custody", comparison)))
            throw new IOException("D1 candidate custody is reserved for the certified namespace capability.");
    }
}

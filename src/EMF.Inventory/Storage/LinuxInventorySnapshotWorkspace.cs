using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using EMF.Inventory.Contracts;
using EMF.Inventory.Models;

namespace EMF.Inventory.Storage;

// Linux filesystem mechanics only. Encryption and authorization are injected elsewhere.
public sealed class LinuxInventorySnapshotWorkspace
{
    private const UnixFileMode DirectoryMode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;
    private const UnixFileMode FileModeBits = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private readonly Func<string, Task>? _beforeRemove;
    public string Root { get; }
    public LinuxInventorySnapshotWorkspace(string root, Func<string, Task>? beforeRemove = null)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Inventory workspaces require Linux.");
        Root = System.IO.Path.GetFullPath(root);
        _beforeRemove = beforeRemove;
        // This slice admits only dedicated VM-local state under these host-controlled bases.
        // Repository, home sync directories and Windows transfer mounts are never candidates.
        if (!new[] { "/tmp/", "/var/tmp/", "/var/lib/emf/" }.Any(prefix => Root.StartsWith(prefix, StringComparison.Ordinal)))
            throw new IOException("Inventory state requires a dedicated VM-local root.");
        for (string? p = Root; p is not null; p = System.IO.Path.GetDirectoryName(p))
        {
            if (new DirectoryInfo(p).LinkTarget is not null || Directory.Exists(System.IO.Path.Combine(p, ".git")))
                throw new IOException("Inventory workspace must be outside repositories and symbolic links.");
        }
        Directory.CreateDirectory(Root, DirectoryMode);
        Validate(Root, true);
        FlushDirectory(Root);
    }

    public string Create(string parentId, string objectId, bool ephemeral = false)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        InventoryIdentity.Validate(parentId); InventoryIdentity.Validate(objectId);
        var dir = System.IO.Path.Combine(Root, "work-" + InventoryIdentity.New());
        Directory.CreateDirectory(dir, DirectoryMode);
        using (var marker = CreateFile(System.IO.Path.Combine(dir, "owner.json")))
        {
            JsonSerializer.Serialize(marker, new Marker(parentId, objectId, ephemeral)); marker.Flush(true);
        }
        FlushDirectory(dir); FlushDirectory(Root);
        return System.IO.Path.Combine(dir, "snapshot.sqlite");
    }

    public FileStream CreateFile(string path)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        return new(path, new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None, Options = FileOptions.Asynchronous, UnixCreateMode = FileModeBits });
    }

    private string WorkDirectory(string path)
    {
        var dir = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!;
        var name = System.IO.Path.GetFileName(dir);
        if (System.IO.Path.GetDirectoryName(dir) != Root || !name.StartsWith("work-", StringComparison.Ordinal))
            throw new IOException("Workspace is not owned by this store.");
        InventoryIdentity.Validate(name[5..]); return dir;
    }
    private string CleanupPath(string dir) => System.IO.Path.Combine(Root, "cleanup-" + System.IO.Path.GetFileName(dir) + ".json");
    private T ReadRecord<T>(string path)
    {
        Validate(path, false);
        if (new FileInfo(path).Length > 1024) throw new InvalidDataException("Invalid workspace owner.");
        return JsonSerializer.Deserialize<T>(File.ReadAllText(path), new JsonSerializerOptions
        { UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow }) ?? throw new InvalidDataException("Invalid workspace owner.");
    }
    private Cleanup? PrepareRemoval(string path)
    {
        var dir = WorkDirectory(path); var recordPath = CleanupPath(dir);
        if (File.Exists(recordPath))
        {
            var known = ReadRecord<Cleanup>(recordPath); ValidateCleanup(known, dir);
            FlushDirectory(Root); return known;
        }
        if (new DirectoryInfo(dir).LinkTarget is not null || File.Exists(dir)) throw new IOException("Workspace is not an owned directory.");
        if (!Directory.Exists(dir)) { FlushDirectory(Root); return null; }
        Validate(dir, true);
        var owner = ReadRecord<Marker>(System.IO.Path.Combine(dir, "owner.json"));
        ValidateOwner(owner);
        var record = new Cleanup(System.IO.Path.GetFileName(dir), owner);
        // Exact ownership remains durable outside the directory while its marker is unlinked.
        var temporary = recordPath + ".tmp-" + InventoryIdentity.New();
        try
        {
            using (var file = CreateFile(temporary)) { JsonSerializer.Serialize(file, record); file.Flush(true); }
            File.Move(temporary, recordPath); FlushDirectory(Root);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        return record;
    }
    private static void ValidateCleanup(Cleanup record, string dir)
    {
        if (string.IsNullOrEmpty(record.WorkDirectory) || !record.WorkDirectory.StartsWith("work-", StringComparison.Ordinal) ||
            record.WorkDirectory != System.IO.Path.GetFileName(dir)) throw new InvalidDataException("Cleanup workspace mismatch.");
        InventoryIdentity.Validate(record.WorkDirectory[5..]);
        ValidateOwner(record.Owner);
    }
    private static void ValidateOwner(Marker owner)
    {
        if (owner.Version != 1) throw new InvalidDataException("Unknown workspace owner version.");
        InventoryIdentity.Validate(owner.ParentId); InventoryIdentity.Validate(owner.ObjectId);
    }
    public async Task RemoveAsync(string path)
    {
        var record = PrepareRemoval(path);
        if (record is null) return;
        if (_beforeRemove is not null) await _beforeRemove(path).ConfigureAwait(false);
        RemovePrepared(path, record);
    }
    public void Remove(string path) { var record = PrepareRemoval(path); if (record is not null) RemovePrepared(path, record); }
    private void RemovePrepared(string path, Cleanup record)
    {
        var dir = WorkDirectory(path); ValidateCleanup(record, dir);
        var marker = System.IO.Path.Combine(dir, "owner.json");
        if (new DirectoryInfo(dir).LinkTarget is not null || File.Exists(dir)) throw new IOException("Workspace is not an owned directory.");
        // Recovery enters here directly. Its cleanup proof must be durable before
        // any inner ownership marker is removed, including after a failed flush.
        FlushDirectory(Root);
        if (Directory.Exists(dir))
        {
            Validate(dir, true);
            if (File.Exists(marker) && ReadRecord<Marker>(marker) != record.Owner) throw new InvalidDataException("Cleanup owner mismatch.");
            // Never recursively delete an unknown entry or link.
            var entries = Directory.EnumerateFileSystemEntries(dir).Take(9).ToArray();
            if (entries.Length > 8) throw new InvalidDataException("Unknown workspace contents.");
            foreach (var file in entries)
            {
                if (System.IO.Path.GetFileName(file) is not ("owner.json" or "snapshot.sqlite" or "snapshot.sqlite-wal" or "snapshot.sqlite-shm" or "snapshot.sqlite-journal"))
                    throw new InvalidDataException("Unknown workspace contents.");
                Validate(file, false);
            }
            foreach (var file in entries.Where(f => f != marker)) File.Delete(file);
            if (File.Exists(marker)) File.Delete(marker);
            Directory.Delete(dir);
        }
        FlushDirectory(Root);
        // Cleanup proof is removed only after the physical directory is gone and flushed.
        File.Delete(CleanupPath(dir)); FlushDirectory(Root);
    }

    public async Task RecoverAsync(IInventoryParentJournal journal, int limit, CancellationToken ct = default)
    {
        if (limit is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(limit));
        int count = 0; var visited = new HashSet<string>(StringComparer.Ordinal);
        foreach (var recordPath in Directory.EnumerateFiles(Root, "cleanup-work-*.json"))
        {
            if (++count > limit) return; ct.ThrowIfCancellationRequested();
            var record = ReadRecord<Cleanup>(recordPath);
            var dir = System.IO.Path.Combine(Root, record.WorkDirectory);
            ValidateCleanup(record, dir);
            if (CleanupPath(dir) != recordPath) throw new InvalidDataException("Cleanup record mismatch.");
            visited.Add(dir);
            await using var gate = TryAcquire(record.Owner.ParentId);
            if (gate is null) continue;
            if (await MayRecoverAsync(journal, record.Owner, ct).ConfigureAwait(false)) RemovePrepared(System.IO.Path.Combine(dir, "snapshot.sqlite"), record);
        }
        foreach (var dir in Directory.EnumerateDirectories(Root, "work-*"))
        {
            if (visited.Contains(dir)) continue;
            if (++count > limit) break; ct.ThrowIfCancellationRequested(); Validate(dir, true);
            var owner = ReadRecord<Marker>(System.IO.Path.Combine(dir, "owner.json"));
            ValidateOwner(owner);
            await using var gate = TryAcquire(owner.ParentId);
            if (gate is null) continue;
            if (await MayRecoverAsync(journal, owner, ct).ConfigureAwait(false)) Remove(System.IO.Path.Combine(dir, "snapshot.sqlite"));
        }
    }
    private static async Task<bool> MayRecoverAsync(IInventoryParentJournal journal, Marker owner, CancellationToken ct)
    {
        var retained = await journal.ReadRetentionAsync(owner.ObjectId, ct).ConfigureAwait(false);
        // Ephemeral inputs have no protected publication authority. Never infer that for an
        // old/unknown protected workspace merely because no retention row can be found.
        return owner.Ephemeral ? retained is null : retained?.ParentId == owner.ParentId &&
            retained.Status is InventoryRetentionStatus.Sealed or InventoryRetentionStatus.Released;
    }
    private IAsyncDisposable? TryAcquire(string identity)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        InventoryIdentity.Validate(identity);
        var name = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        var path = System.IO.Path.Combine(Root, "gate-" + name);
        // FileStream's path constructor takes its own sharing lock. Recovery must
        // leave contention detection to the explicit nonblocking flock below.
        int fd = openCreate(path, 2 | 0x40 | 0x20000 | 0x80000, 0x180);
        if (fd < 0) throw new IOException("Cannot open Inventory recovery gate.", new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError()));
        var handle = new Microsoft.Win32.SafeHandles.SafeFileHandle((IntPtr)fd, ownsHandle: true);
        FileStream stream;
        try { stream = new FileStream(handle, FileAccess.ReadWrite); }
        catch { handle.Dispose(); throw; }
        try
        {
            Validate(path, false);
            if (flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) == 0) return new Gate(stream);
            if (Marshal.GetLastPInvokeError() is not (11 or 35)) throw new IOException("Inventory execution gate failed.");
            stream.Dispose(); return null;
        }
        catch { stream.Dispose(); throw; }
    }
    private FileStream OpenGate(string identity)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var name = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        return new FileStream(System.IO.Path.Combine(Root, "gate-" + name), new FileStreamOptions
        { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.ReadWrite, UnixCreateMode = FileModeBits });
    }

    public async Task<IAsyncDisposable> AcquireAsync(string identity, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        InventoryIdentity.Validate(identity);
        var stream = OpenGate(identity);
        var path = stream.Name;
        try
        {
            Validate(path, false); var watch = Stopwatch.StartNew();
            while (flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), 2 | 4) != 0)
            {
                ct.ThrowIfCancellationRequested();
                if (Marshal.GetLastPInvokeError() is not (11 or 35)) throw new IOException("Inventory execution gate failed.");
                if (watch.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Inventory execution gate timed out.");
                await Task.Delay(10, ct).ConfigureAwait(false);
            }
            ct.ThrowIfCancellationRequested(); return new Gate(stream);
        }
        catch { stream.Dispose(); throw; }
    }

    public static void Validate(string path, bool directory)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException();
        var b = Marshal.AllocHGlobal(256);
        try
        {
            if (statx(-100, path, 0x100, 0xA, b) != 0 || (Marshal.ReadInt32(b) & 0xA) != 0xA)
                throw new IOException("Workspace ownership cannot be verified.");
            var mode = (ushort)Marshal.ReadInt16(b, 28);
            if ((mode & 0xF000) != (directory ? 0x4000 : 0x8000) ||
                unchecked((uint)Marshal.ReadInt32(b, 20)) != geteuid() ||
                (mode & 0xFFF) != (directory ? 0x1C0 : 0x180))
                throw new IOException("Workspace permissions or ownership are unsafe.");
        }
        finally { Marshal.FreeHGlobal(b); }
    }

    public static void FlushDirectory(string path)
    {
        int fd = open(path, 0x10000 | 0x20000 | 0x80000);
        if (fd < 0) throw new IOException("Cannot open workspace directory.");
        var b = Marshal.AllocHGlobal(256);
        try
        {
            if (fstatfs(fd, b) != 0 || Marshal.ReadInt64(b) is not (0xEF53 or 0x58465342 or 0x9123683E))
                throw new PlatformNotSupportedException("Inventory requires an admitted VM-local filesystem.");
            if (fsync(fd) != 0) throw new IOException("Workspace directory flush failed.");
        }
        finally { Marshal.FreeHGlobal(b); close(fd); }
    }
    private sealed record Marker(string ParentId, string ObjectId, bool Ephemeral = false, int Version = 1);
    private sealed record Cleanup(string WorkDirectory, Marker Owner);
    private sealed class Gate(FileStream stream) : IAsyncDisposable
    { public ValueTask DisposeAsync() { stream.Dispose(); return ValueTask.CompletedTask; } }
    [DllImport("libc", SetLastError = true)] private static extern int statx(int fd, string path, int flags, uint mask, IntPtr buffer);
    [DllImport("libc")] private static extern uint geteuid();
    [DllImport("libc", SetLastError = true)] private static extern int open(string path, int flags);
    [DllImport("libc", EntryPoint = "open", SetLastError = true)] private static extern int openCreate(string path, int flags, uint mode);
    [DllImport("libc", SetLastError = true)] private static extern int fsync(int fd);
    [DllImport("libc")] private static extern int close(int fd);
    [DllImport("libc", SetLastError = true)] private static extern int fstatfs(int fd, IntPtr buffer);
    [DllImport("libc", SetLastError = true)] private static extern int flock(int fd, int operation);
}

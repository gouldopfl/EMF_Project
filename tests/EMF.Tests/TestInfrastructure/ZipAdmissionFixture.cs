using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Models;
using EMF.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests.TestInfrastructure;

internal sealed class ZipAdmissionFixture : IAsyncDisposable
{
    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now = new(2026, 10, 10, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }
    public string Root { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "emf-zip-admission-" + Guid.NewGuid().ToString("N"));
    public string Path => System.IO.Path.Combine(Root, "evidence.db");
    public Clock Time { get; } = new();
    public SqliteZipExtractionJournal Journal => new(Path, Time);
    public ZipAdmissionKey Key { get; } = new("synthetic-test-issuer", "stable-test-request");
    public ZipDurableProfile Profile { get; } = ZipDurableProfile.Create(new string('A', 64), new string('B', 64),
        new("clamd", "test-policy", new string('C', 64)), "synthetic-test-protection");
    public ZipAdmissionBinding Binding => new("workflow", "workflow-operation", "zip-archives", "parent-artifact",
        "source-content", "source-revision", "synthetic-test-actor", "synthetic-authorized-operation",
        Profile.CanonicalJson, Profile.Fingerprint, Profile.ParentNamespaceId, Profile.ChildNamespaceId);
    public static async Task<ZipAdmissionFixture> CreateAsync(bool initialize = true)
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("ZIP admission durability requires Linux.");
        var f = new ZipAdmissionFixture();
        Directory.CreateDirectory(f.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        await new SqliteEvidenceRepository(f.Path).InitializeAsync();
        File.SetUnixFileMode(f.Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        if (initialize) await f.Journal.InitializeAsync();
        return f;
    }
    public ZipAdmissionEvent Approval(ZipParentAdmission p) => new("synthetic-approval-event", ZipAdmissionEventKind.Approved,
        p.Binding.OriginalActorId, Time.Now, new(p.OperationId, ZipAdmissionValidation.BindingHash(p.Binding), p.Key.IssuerId,
            "synthetic-decision", "authority-revision-1", "policy-v1",
            ZipAuthorityCapabilities.Admit | ZipAuthorityCapabilities.Recover | ZipAuthorityCapabilities.Review,
            Time.Now, Time.Now.AddHours(1)));
    public async Task SqlAsync(string sql, params (string Key, object Value)[] parameters)
    {
        await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
        await c.OpenAsync(); using var q = c.CreateCommand(); q.CommandText = sql;
        foreach (var (key, value) in parameters) q.Parameters.AddWithValue(key, value);
        await q.ExecuteNonQueryAsync();
    }
    public async Task<long> ScalarAsync(string sql)
    {
        await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = Path, Pooling = false }.ToString());
        await c.OpenAsync(); using var q = c.CreateCommand(); q.CommandText = sql;
        return Convert.ToInt64(await q.ExecuteScalarAsync());
    }
    public ValueTask DisposeAsync()
    {
        using var c = new SqliteConnection("Data Source=" + Path); SqliteConnection.ClearPool(c);
        Directory.Delete(Root, true); return ValueTask.CompletedTask;
    }
}

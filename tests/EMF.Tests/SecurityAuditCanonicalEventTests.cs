using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Models;
using EMF.Security.Persistence.Sqlite;
using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;
namespace EMF.Tests;

public sealed class SecurityAuditCanonicalEventTests
{
    [Fact]
    public async Task Concurrent_identical_appends_return_the_same_verified_acknowledgement()
    {
        await using var f = await Fixture.CreateAsync(); var record = Record();
        var acknowledgements = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => new SqliteSecurityAuditSink(f.Path).AppendAsync(record)));
        Assert.All(acknowledgements, ack => Assert.Equal(acknowledgements[0], ack));
        Assert.Equal(1, (await new SqliteSecurityAuditIntegrityVerifier(f.Path).VerifyAsync()).ProtectedRecordCount);
        Assert.Equal(acknowledgements[0], (await f.Sink.FindVerifiedAsync(record.AuditEventId!.Value))!.Acknowledgement);
    }
    [Fact]
    public async Task Same_identity_with_changed_canonical_data_conflicts_without_append()
    {
        await using var f = await Fixture.CreateAsync(); var record = Record(); await f.Sink.AppendAsync(record);
        var changed = Record(record.AuditEventId, actor: "other");
        await Assert.ThrowsAsync<SecurityAuditIdentityConflictException>(() => f.Sink.AppendAsync(changed));
        Assert.Equal(1, (await new SqliteSecurityAuditIntegrityVerifier(f.Path).VerifyAsync()).ProtectedRecordCount);
    }
    [Theory]
    [InlineData("OriginalActorId='changed'")]
    [InlineData("OperationId='changed'")]
    [InlineData("AuditEventId='changed'")]
    [InlineData("FactsJson='{\"disposition\":\"NotFound\"}'")]
    [InlineData("RecoveryActorId='forged'")]
    [InlineData("OccurredUtc='2026-10-03T00:00:00+00:00'")]
    public async Task Structured_tampering_prevents_verification_acknowledgement_and_further_append(string assignment)
    {
        await using var f = await Fixture.CreateAsync(); var record = Record(); await f.Sink.AppendAsync(record);
        await f.ExecuteAsync("UPDATE SecurityAuditRecords SET " + assignment);
        Assert.False((await new SqliteSecurityAuditIntegrityVerifier(f.Path).VerifyAsync()).IsValid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Sink.FindVerifiedAsync(record.AuditEventId!.Value));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Sink.AppendAsync(Record()));
    }
    [Fact]
    public async Task V1_to_v2_preserves_historical_bytes_and_prohibits_subsequent_v1_writers()
    {
        await using var f = await Fixture.CreateAsync();
        var historical = new SecurityAuditRecord
        {
            Operation = "historical",
            ResourceType = "Artifact",
            ResourceId = "old",
            SubjectId = "original",
            Outcome = SecurityAuditOutcome.Succeeded,
            OccurredUtc = DateTimeOffset.UtcNow,
            Facts = new Dictionary<string, string> { { "unbounded-history", "original" } }
        };
        await f.Sink.WriteAsync(historical);
        var before = await f.SnapshotAsync(); await f.Sink.AppendAsync(Record());
        Assert.Equal(before, (await f.SnapshotAsync()));
        var integrity = await new SqliteSecurityAuditIntegrityVerifier(f.Path).VerifyAsync();
        Assert.True(integrity.IsValid); Assert.Equal(2, integrity.ProtectedRecordCount);
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Sink.WriteAsync(historical));
        Assert.Equal(2, (await new SqliteSecurityAuditIntegrityVerifier(f.Path).VerifyAsync()).ProtectedRecordCount);
    }
    [Fact]
    public async Task Version3_schema_migration_preserves_v1_hashes_and_autoincrement_high_water()
    {
        await using var f = await Fixture.CreateAsync();
        // Build the actual historical v1 schema using its migration SQL, then migrate it.
        using (var connection = new SqliteConnection($"Data Source={f.Path}"))
        {
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "DROP TABLE SecurityAuditRecords; DELETE FROM SecurityAudit_SchemaMigrations WHERE Version=3;"; command.ExecuteNonQuery();
            command.CommandText = "CREATE TABLE SecurityAuditRecords(Id INTEGER PRIMARY KEY AUTOINCREMENT,Operation TEXT NOT NULL,ResourceType TEXT NOT NULL,ResourceId TEXT NOT NULL,SubjectId TEXT NOT NULL,PolicyDecision TEXT,Destination TEXT,Outcome TEXT NOT NULL,OccurredUtc TEXT NOT NULL,FactsJson TEXT NOT NULL,IntegrityVersion INTEGER NOT NULL DEFAULT 0 CHECK(IntegrityVersion IN(0,1)),PreviousRecordHash TEXT,RecordHash TEXT);"; command.ExecuteNonQuery();
            command.CommandText = "INSERT INTO SecurityAuditRecords(Id,Operation,ResourceType,ResourceId,SubjectId,Outcome,OccurredUtc,FactsJson) VALUES(77,'legacy','Artifact','old','actor','Succeeded','historic','{}'); INSERT INTO SecurityAuditRecords(Id,Operation,ResourceType,ResourceId,SubjectId,Outcome,OccurredUtc,FactsJson) VALUES(90,'removed','Artifact','old','actor','Succeeded','historic','{}'); DELETE FROM SecurityAuditRecords WHERE Id=90;"; command.ExecuteNonQuery();
        }
        await new SecurityAuditSqliteSchema(f.Path).InitializeAsync();
        var ack = await f.Sink.AppendAsync(Record()); Assert.True(ack.RecordId > 90);
        var integrity = await new SqliteSecurityAuditIntegrityVerifier(f.Path).VerifyAsync(); Assert.True(integrity.IsValid); Assert.Equal(1, integrity.LegacyRecordCount);
    }
    [Fact]
    public void Canonical_order_and_utc_are_stable_and_unapproved_facts_and_unicode_are_rejected()
    {
        var id = SecurityAuditEventId.New(); var time = new DateTimeOffset(2026, 10, 3, 1, 2, 3, TimeSpan.Zero);
        var first = Record(id, time: time, facts: new Dictionary<string, string> { { "disposition", "Updated" }, { "classificationId", "confidential" } });
        var second = Record(id, time: time.ToOffset(TimeSpan.FromHours(2)), facts: new Dictionary<string, string> { { "classificationId", "confidential" }, { "disposition", "Updated" } });
        Assert.Equal(SecurityAuditCanonicalEvent.Encode(first), SecurityAuditCanonicalEvent.Encode(second));
        Assert.Throws<ArgumentException>(() => SecurityAuditCanonicalEvent.Encode(Record(facts: new Dictionary<string, string> { { "plaintext", "forbidden" } })));
        Assert.ThrowsAny<ArgumentException>(() => SecurityAuditCanonicalEvent.Encode(Record(actor: "invalid\ud800")));
    }
    private static SecurityAuditRecord Record(SecurityAuditEventId? id = null, string actor = "steward", DateTimeOffset? time = null, IReadOnlyDictionary<string, string>? facts = null)
        => new()
        {
            AuditEventId = id ?? SecurityAuditEventId.New(),
            OperationId = new("synthetic-operation"),
            OriginalActorId = actor,
            SubjectId = actor,
            Operation = SecurityPermissions.ArtifactEnvelopeRewrap.ToString(),
            ResourceType = SecurityResourceTypes.Artifact,
            ResourceId = "synthetic-artifact",
            PolicyDecision = AuthorizationDecision.Allow,
            Outcome = SecurityAuditOutcome.Succeeded,
            OccurredUtc = time ?? new DateTimeOffset(2026, 10, 3, 1, 2, 3, TimeSpan.Zero),
            Facts = facts ?? new Dictionary<string, string> { { "disposition", "Updated" } }
        };
    private sealed class Fixture : IAsyncDisposable
    {
        private readonly string _root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        public string Path => System.IO.Path.Combine(_root, "audit.sqlite");
        public SqliteSecurityAuditSink Sink => new(Path);
        public static async Task<Fixture> CreateAsync() { var f = new Fixture(); Directory.CreateDirectory(f._root); await f.Sink.InitializeAsync(); return f; }
        public async Task ExecuteAsync(string sql) { using var c = new SqliteConnection($"Data Source={Path}"); c.Open(); using var command = c.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync(); }
        public async Task<string> SnapshotAsync() { using var c = new SqliteConnection($"Data Source={Path}"); c.Open(); using var command = c.CreateCommand(); command.CommandText = "SELECT Operation || '|' || OccurredUtc || '|' || FactsJson || '|' || RecordHash FROM SecurityAuditRecords WHERE Id=1"; return (string)(await command.ExecuteScalarAsync())!; }
        public ValueTask DisposeAsync() { SqliteConnection.ClearAllPools(); Directory.Delete(_root, true); return ValueTask.CompletedTask; }
    }
}

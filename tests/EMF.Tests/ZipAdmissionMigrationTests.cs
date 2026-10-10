using System.Reflection;
using System.Text.Json;
using EMF.Core.Contracts.Zip;
using EMF.Persistence.Repositories;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ZipAdmissionMigrationTests
{
    private static string VersionFourSql()
    {
        var type = typeof(SqliteZipExtractionJournal);
        var sql = (string)type.GetProperty("SchemaSql", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        foreach (var name in new[] { "ZipRetentionSchema", "ZipExtractionWorkSchema", "ZipScanSchema" })
            sql += "\n" + (string)type.Assembly.GetType("EMF.Persistence.Repositories." + name)!
                .GetField("Sql", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        return sql;
    }
    private static async Task<ZipParentBinding> SeedVersionFourAsync(ZipAdmissionFixture f)
    {
        await f.SqlAsync(VersionFourSql());
        var binding = new ZipParentBinding("existing-parent", "existing-artifact", new("retained", "revision", new string('D', 64), 100), new string('E', 64));
        await f.SqlAsync("""
            INSERT INTO Artifacts VALUES('existing-artifact','parent.zip','file','2026-10-10T12:00:00Z',NULL,NULL,'{}');
            INSERT INTO ZipExtractionParents VALUES($op,$binding,'',0,1,$until,0,NULL,NULL,-1,$budget,NULL);
            """, ("$op", binding.OperationId), ("$binding", JsonSerializer.Serialize(binding)),
            ("$until", DateTimeOffset.MinValue.ToString("O")), ("$budget", JsonSerializer.Serialize(new ZipBudget())));
        return binding;
    }
    private static async Task<string> SnapshotAsync(ZipAdmissionFixture f, bool includeNew = true)
        => await SnapshotAsync(f.Path, includeNew);
    private static async Task<string> SnapshotAsync(string path, bool includeNew = true)
    {
        await using var c = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        await c.OpenAsync(); using var q = c.CreateCommand();
        q.CommandText = "SELECT name,type,tbl_name,sql FROM sqlite_master WHERE name NOT LIKE 'sqlite_%' ORDER BY name";
        var output = new List<string>(); var tables = new List<string>();
        using (var rows = await q.ExecuteReaderAsync())
            while (await rows.ReadAsync())
            {
                var name = rows.GetString(0); var table = rows.GetString(2);
                if (!includeNew && (table is "ZipParentAdmissions" or "ZipParentAdmissionEvents")) continue;
                var values = new object[4]; rows.GetValues(values); output.Add(JsonSerializer.Serialize(values));
                if (rows.GetString(1) == "table") tables.Add(name);
            }
        foreach (var table in tables)
        {
            if (!includeNew && table == "ZipExtractionSchema") continue;
            q.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\" ORDER BY rowid";
            using var rows = await q.ExecuteReaderAsync();
            while (await rows.ReadAsync()) { var values = new object[rows.FieldCount]; rows.GetValues(values); output.Add(table + JsonSerializer.Serialize(values)); }
        }
        return JsonSerializer.Serialize(output);
    }

    [Fact]
    public async Task Genuine_v4_upgrade_preserves_all_existing_evidence_and_does_not_backfill_approval()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync(false);
        var binding = await SeedVersionFourAsync(f); var before = await SnapshotAsync(f, false);
        Assert.Equal(4, await f.ScalarAsync("SELECT MAX(Version) FROM ZipExtractionSchema"));
        await f.Journal.InitializeAsync(); await f.Journal.InitializeAsync();
        Assert.Equal(5, await f.ScalarAsync("SELECT MAX(Version) FROM ZipExtractionSchema"));
        Assert.Equal(before, await SnapshotAsync(f, false));
        Assert.Equal(binding, (await f.Journal.ReadAsync(binding.OperationId))!.Binding);
        Assert.Empty(await f.Journal.ReadRecoveryAdmissionsAsync(null, 100));
        Assert.Equal(0, await f.ScalarAsync("SELECT COUNT(*) FROM ZipParentAdmissionEvents"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task V5_collision_rolls_back_every_partial_object_and_restart_can_retry(bool late)
    {
        await using var f = await ZipAdmissionFixture.CreateAsync(false); await SeedVersionFourAsync(f);
        await f.SqlAsync(late
            ? "CREATE TRIGGER ZipAdmissionEventNoReplace BEFORE INSERT ON Artifacts BEGIN SELECT 1; END"
            : "CREATE TABLE ZipParentAdmissions(Collision TEXT)");
        var before = await SnapshotAsync(f);
        await Assert.ThrowsAsync<SqliteException>(() => f.Journal.InitializeAsync());
        Assert.Equal(before, await SnapshotAsync(f));
        Assert.Equal(4, await f.ScalarAsync("SELECT MAX(Version) FROM ZipExtractionSchema"));
        await f.SqlAsync(late ? "DROP TRIGGER ZipAdmissionEventNoReplace" : "DROP TABLE ZipParentAdmissions");
        await f.Journal.InitializeAsync();
        Assert.Equal(5, await f.ScalarAsync("SELECT MAX(Version) FROM ZipExtractionSchema"));
        Assert.Empty(await f.Journal.ReadRecoveryAdmissionsAsync(null, 100));
    }

    [Fact]
    public async Task Missing_v5_invariant_trigger_rejects_initialization_and_admission_reads()
    {
        await using var f = await ZipAdmissionFixture.CreateAsync();
        var p = await f.Journal.ResolveOrReserveAdmissionAsync(f.Key, f.Binding);
        await f.SqlAsync("DROP TRIGGER ZipAdmissionNoUpdate");
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.InitializeAsync());
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Journal.ReadAdmissionAsync(p.OperationId));
    }

    [Fact]
    public async Task V4_upgrade_preserves_completed_extraction_retention_scans_charges_and_acknowledgements_byte_for_byte()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync(); var runtime = new ZipDurableRuntimeFixture(evidence);
        var parent = await runtime.CreateParent(two: true); var completed = await runtime.Driver.RunAsync(parent);
        Assert.Equal(ZipParentState.Released, completed.State);
        await evidence.SqlAsync("DROP TABLE ZipParentAdmissionEvents; DROP TABLE ZipParentAdmissions; DELETE FROM ZipExtractionSchema WHERE Version=5;");
        var before = await SnapshotAsync(evidence.DatabasePath, false);
        await runtime.Journal.InitializeAsync(); await runtime.Journal.InitializeAsync();
        Assert.Equal(before, await SnapshotAsync(evidence.DatabasePath, false));
        Assert.Equal(completed.Fence, (await runtime.Read()).Fence);
        Assert.Empty(await runtime.Journal.ReadRecoveryAdmissionsAsync(null, 100));
    }
}

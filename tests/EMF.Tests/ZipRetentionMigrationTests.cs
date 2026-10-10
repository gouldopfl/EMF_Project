using System.Reflection;
using System.Text.Json;
using EMF.Core.Contracts.Zip;
using EMF.Persistence.Repositories;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ZipRetentionMigrationTests
{
    private static string Json<T>(T value) => JsonSerializer.Serialize(value);

    private static async Task<ZipParentSnapshot> InstallVersionOneAsync(ArtifactIngestionFixture fixture)
    {
        // Use the actual version-one schema, rather than creating v2 and deleting its objects.
        var schema = (string)typeof(SqliteZipExtractionJournal)
            .GetProperty("SchemaSql", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
        await fixture.SqlAsync(schema);
        var binding = new ZipParentBinding("migration-parent", "migration-artifact",
            new("retained-parent", "original-revision", new string('A', 64), 100), new string('B', 64));
        ZipEntryPlan[] entries =
        [
            new(0, 0, "duplicate.txt", "duplicate.txt", 5, 10, 123, false, false, "stable-child-0", "stable-artifact-0"),
            new(1, 1, "duplicate.txt", "duplicate.txt", 6, 12, 456, false, false, "stable-child-1", "stable-artifact-1")
        ];
        const string receipt = "{\"PreflightVersion\":1}";
        var plan = new ZipPlan(ZipPlanBinding.Compute(receipt, entries), receipt, entries);
        var reservation = new ZipWorkReservation("original-preflight", ZipWorkKind.Preflight, null, 65_639);
        var snapshot = new ZipParentSnapshot(binding,
            new(binding.OperationId, "original-worker", 1, 4, plan.Hash, -1), DateTimeOffset.UtcNow.AddMinutes(5),
            ZipParentState.Planned, plan, new(PreflightAttempts: 1, PreflightBytes: 65_639),
            entries.Select(e => new ZipEntryProgress(e)).ToArray());
        await fixture.SqlAsync("""
            INSERT INTO ZipExtractionParents VALUES($op,$binding,$owner,1,4,$until,$state,$plan,$hash,-1,$budget,NULL);
            INSERT INTO ZipExtractionReservations VALUES($op,$reservation,NULL,$kind,$reservationJson);
            """, ("$op", binding.OperationId), ("$binding", Json(binding)), ("$owner", snapshot.Fence.Owner),
            ("$until", snapshot.OwnerUntil.ToString("O")), ("$state", (int)snapshot.State), ("$plan", Json(plan)),
            ("$hash", plan.Hash), ("$budget", Json(snapshot.Budget)), ("$reservation", reservation.Id),
            ("$kind", (int)reservation.Kind), ("$reservationJson", Json(reservation)));
        foreach (var entry in entries)
            await fixture.SqlAsync("INSERT INTO ZipExtractionEntries VALUES($op,$central,$file,$child,$artifact,$plan,$progress)",
                ("$op", binding.OperationId), ("$central", entry.CentralOrdinal), ("$file", entry.FileOrdinal!.Value),
                ("$child", entry.ChildOperationId!), ("$artifact", entry.ProvisionalArtifactId!),
                ("$plan", Json(entry)), ("$progress", Json(new ZipEntryProgress(entry))));
        return snapshot;
    }

    private static async Task<string> SnapshotAsync(string path, bool sharedOnly)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = path, Pooling = false }.ToString());
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT name,type,tbl_name,sql FROM sqlite_master " +
            (sharedOnly ? "WHERE name NOT LIKE 'Zip%' AND tbl_name NOT LIKE 'Zip%' " : "") + "ORDER BY name";
        var snapshot = new List<string>();
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount]; reader.GetValues(values);
                snapshot.Add(Json(values));
            }
        command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' " +
            (sharedOnly ? "AND name NOT LIKE 'Zip%' " : "") + "ORDER BY name";
        var tables = new List<string>();
        using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) tables.Add(reader.GetString(0));
        foreach (var table in tables)
        {
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"") + "\" ORDER BY rowid";
            using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount]; reader.GetValues(values);
                snapshot.Add(table + ":" + Json(values));
            }
        }
        return Json(snapshot);
    }

    private static async Task<long> ScalarAsync(ArtifactIngestionFixture fixture, string sql)
    {
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = fixture.DatabasePath, Pooling = false }.ToString());
        await connection.OpenAsync(); using var command = connection.CreateCommand(); command.CommandText = sql;
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    [Fact]
    public async Task Version_one_upgrade_preserves_parent_plan_identities_reservations_and_shared_evidence()
    {
        await using var fixture = await ArtifactIngestionFixture.CreateAsync();
        Assert.True((await fixture.Restart().IngestAsync(fixture.Draft, fixture.Content)).IsAdopted);
        var original = await InstallVersionOneAsync(fixture);
        var shared = await SnapshotAsync(fixture.DatabasePath, true);
        Assert.Equal(1, await ScalarAsync(fixture, "SELECT MAX(Version) FROM ZipExtractionSchema"));
        await new SqliteZipExtractionJournal(fixture.DatabasePath).InitializeAsync();
        Assert.Equal(5, await ScalarAsync(fixture, "SELECT MAX(Version) FROM ZipExtractionSchema"));
        var restarted = new SqliteZipExtractionJournal(fixture.DatabasePath);
        Assert.Equal(Json(original), Json(await restarted.ReadAsync(original.Binding.OperationId)));
        Assert.Equal(1, await ScalarAsync(fixture, "SELECT COUNT(*) FROM ZipExtractionReservations"));
        Assert.Equal(0, await ScalarAsync(fixture, "SELECT COUNT(*) FROM ZipExtractionRetentions"));
        Assert.Equal(shared, await SnapshotAsync(fixture.DatabasePath, true));
        await restarted.InitializeAsync();
        await fixture.Persistence.InitializeAsync();
        Assert.Equal(Json(original), Json(await restarted.ReadAsync(original.Binding.OperationId)));
        Assert.Equal(shared, await SnapshotAsync(fixture.DatabasePath, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_upgrade_rolls_back_all_partial_objects_and_fresh_restart_recovers(bool lateTriggerCollision)
    {
        await using var fixture = await ArtifactIngestionFixture.CreateAsync();
        Assert.True((await fixture.Restart().IngestAsync(fixture.Draft, fixture.Content)).IsAdopted);
        var original = await InstallVersionOneAsync(fixture);
        var shared = await SnapshotAsync(fixture.DatabasePath, true);
        await fixture.SqlAsync(lateTriggerCollision
            ? "CREATE TRIGGER ZipRetentionFrozenEvidence BEFORE UPDATE ON ZipExtractionParents BEGIN SELECT 1; END"
            : "CREATE TABLE ZipExtractionRetentions(Unexpected TEXT)");
        var before = await SnapshotAsync(fixture.DatabasePath, false);
        await Assert.ThrowsAsync<SqliteException>(() => new SqliteZipExtractionJournal(fixture.DatabasePath).InitializeAsync());
        Assert.Equal(before, await SnapshotAsync(fixture.DatabasePath, false));
        Assert.Equal(shared, await SnapshotAsync(fixture.DatabasePath, true));
        Assert.Equal(1, await ScalarAsync(fixture, "SELECT MAX(Version) FROM ZipExtractionSchema"));
        Assert.Equal(0, await ScalarAsync(fixture, "SELECT COUNT(*) FROM sqlite_master WHERE name IN ('ZipRetentionBindingImmutable','ZipRetentionNoDelete','ZipRetentionNoReplace')"));
        await fixture.SqlAsync(lateTriggerCollision ? "DROP TRIGGER ZipRetentionFrozenEvidence" : "DROP TABLE ZipExtractionRetentions");
        var restarted = new SqliteZipExtractionJournal(fixture.DatabasePath);
        await restarted.InitializeAsync();
        Assert.Equal(5, await ScalarAsync(fixture, "SELECT MAX(Version) FROM ZipExtractionSchema"));
        Assert.Equal(Json(original), Json(await restarted.ReadAsync(original.Binding.OperationId)));
        Assert.Equal(0, await ScalarAsync(fixture, "SELECT COUNT(*) FROM ZipExtractionRetentions"));
        Assert.Equal(shared, await SnapshotAsync(fixture.DatabasePath, true));
        await fixture.Persistence.InitializeAsync();
        Assert.Equal(shared, await SnapshotAsync(fixture.DatabasePath, true));
    }
}

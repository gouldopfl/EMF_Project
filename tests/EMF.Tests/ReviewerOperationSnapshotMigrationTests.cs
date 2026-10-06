using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ReviewerOperationSnapshotMigrationTests
{
    private const string Table = "VeteransClaims_ReviewerOperationSnapshots";

    [Fact]
    public async Task FreshDatabase_ContainsExactAuthoritySchemaAndLedger95()
    {
        using var database = new TestDatabase();
        await new VeteransClaimsSqliteSchema(database.Path).InitializeAsync();
        await using var connection = await database.OpenAsync();
        Assert.Equal(95L, await ScalarAsync(connection, "SELECT MAX(Version) FROM VeteransClaims_SchemaMigrations"));
        Assert.Equal(95L, await ScalarAsync(connection, "SELECT COUNT(*) FROM VeteransClaims_SchemaMigrations"));
        Assert.Equal("AddReviewerOperationSnapshots", await ScalarAsync(connection,
            "SELECT Name FROM VeteransClaims_SchemaMigrations WHERE Version=95"));

        var expectedColumns = new[]
        {
            "OperationSnapshotId", "ReviewerOperationId", "State", "Revision", "OwnerToken", "Profile",
            "RepresentationVersion", "BundleSha256", "ReadyValidationVersion", "Disposition", "FailureCategory"
        };
        var columns = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = $"PRAGMA table_info({Table})";
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                columns.Add(reader.GetString(1));
                if (reader.GetString(1) == "OperationSnapshotId")
                {
                    Assert.Equal(1L, reader.GetInt64(3));
                    Assert.Equal(1L, reader.GetInt64(5));
                }
            }
        }
        Assert.Equal(expectedColumns, columns);
        var triggers = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='trigger' AND tbl_name=$table ORDER BY name";
            command.Parameters.AddWithValue("$table", Table);
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) triggers.Add(reader.GetString(0));
        }
        Assert.Equal(new[]
        {
            "ReviewerOperationSnapshot_AllowedUpdate", "ReviewerOperationSnapshot_InitialInsert",
            "ReviewerOperationSnapshot_NoDelete", "ReviewerOperationSnapshot_NoDuplicateInsert"
        }, triggers);
        Assert.Equal(1L, await ScalarAsync(connection,
            "SELECT \"unique\" FROM pragma_index_list('VeteransClaims_ReviewerOperationSnapshots') " +
            "WHERE name='UX_VeteransClaims_ReviewerOperationSnapshots_ReviewerOperationId'"));
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM pragma_foreign_key_list('VeteransClaims_ReviewerOperationSnapshots')"));
    }

    [Fact]
    public async Task UpgradeFrom94_IsAdditiveAndRepeatedInitializationPreservesAuthorityAndPackages()
    {
        using var database = new TestDatabase();
        await new VeteransClaimsSqliteMigrator(database.Path,
            VeteransClaimsSqliteMigrations.All.Where(x => x.Version <= 94).ToArray()).MigrateAsync();
        await using var connection = await database.OpenAsync();
        Assert.Equal(94L, await ScalarAsync(connection, "SELECT MAX(Version) FROM VeteransClaims_SchemaMigrations"));
        await ExecuteAsync(connection, """
            INSERT INTO VeteransClaims_Veterans(Id) VALUES('veteran');
            INSERT INTO VeteransClaims_Claims(Id,VeteranId) VALUES('claim','veteran');
            INSERT INTO VeteransClaims_ClaimIssues(Id,ClaimId,ClaimIssueType) VALUES('issue','claim','original');
            INSERT INTO VeteransClaims_EvidencePackages(Id,ClaimIssueId,Purpose,ReviewerRole,CreationOrdinal)
                VALUES('legacy-package','issue','original-purpose','original-role',1);
            INSERT INTO VeteransClaims_EvidencePackages(Id,ClaimIssueId,Purpose,ReviewerRole,ReviewerSnapshotVersion,CreationOrdinal)
                VALUES('sealed-package','issue','sealed-purpose','sealed-role',1,2);
            INSERT INTO VeteransClaims_ReviewerPackageSnapshots(EvidencePackageId,Version,Payload,Sha256)
                VALUES('sealed-package',1,'original-retained-payload',
                    'AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA');
            """);
        var domainBefore = await DomainRowsAsync(connection);
        var ledgerBefore = await ScalarAsync(connection,
            "SELECT group_concat(Version || ':' || Name || ':' || AppliedUtc, '|') " +
            "FROM (SELECT * FROM VeteransClaims_SchemaMigrations ORDER BY Version)");
        var schema = new VeteransClaimsSqliteSchema(database.Path);
        await schema.InitializeAsync();
        Assert.Equal(domainBefore, await DomainRowsAsync(connection));
        Assert.Equal(ledgerBefore, await ScalarAsync(connection,
            "SELECT group_concat(Version || ':' || Name || ':' || AppliedUtc, '|') " +
            "FROM (SELECT * FROM VeteransClaims_SchemaMigrations WHERE Version<=94 ORDER BY Version)"));
        await ExecuteAsync(connection, """
            INSERT INTO VeteransClaims_ReviewerOperationSnapshots
                (OperationSnapshotId,ReviewerOperationId,State,Revision,OwnerToken,Profile,RepresentationVersion,Disposition)
            VALUES('snapshot','reviewer-operation','Capturing',1,'owner','Reviewer.AdoptedUtf8.ContractProof.v1',1,'Active');
            """);
        var applied95 = await ScalarAsync(connection, "SELECT AppliedUtc FROM VeteransClaims_SchemaMigrations WHERE Version=95");
        await schema.InitializeAsync();
        Assert.Equal(applied95, await ScalarAsync(connection, "SELECT AppliedUtc FROM VeteransClaims_SchemaMigrations WHERE Version=95"));
        Assert.Equal(domainBefore, await DomainRowsAsync(connection));
        Assert.Equal(1L, await ScalarAsync(connection, $"SELECT COUNT(*) FROM {Table} WHERE OperationSnapshotId='snapshot' AND Revision=1 AND OwnerToken='owner'"));
        Assert.Equal(95L, await ScalarAsync(connection, "SELECT COUNT(*) FROM VeteransClaims_SchemaMigrations"));
    }

    [Fact]
    public async Task FailedMigration95_RollsBackAllAuthorityObjectsAndLedgerPublication()
    {
        using var database = new TestDatabase();
        var previous = VeteransClaimsSqliteMigrations.All.Where(x => x.Version <= 94).ToArray();
        await new VeteransClaimsSqliteMigrator(database.Path, previous).MigrateAsync();
        var migration = VeteransClaimsSqliteMigrations.All.Single(x => x.Version == 95);
        var failing = previous.Append(new VeteransClaimsSqliteMigration(95, migration.Name,
            migration.Sql + "\nINSERT INTO MissingReviewerMigrationFailureTable VALUES(1);")).ToArray();
        await Assert.ThrowsAsync<SqliteException>(() => new VeteransClaimsSqliteMigrator(database.Path, failing).MigrateAsync());
        await using var connection = await database.OpenAsync();
        Assert.Equal(94L, await ScalarAsync(connection, "SELECT MAX(Version) FROM VeteransClaims_SchemaMigrations"));
        Assert.Equal(0L, await ScalarAsync(connection,
            "SELECT COUNT(*) FROM sqlite_master WHERE tbl_name='VeteransClaims_ReviewerOperationSnapshots'"));
        Assert.Equal(0L, await ScalarAsync(connection, "SELECT COUNT(*) FROM VeteransClaims_SchemaMigrations WHERE Version=95"));
        await new VeteransClaimsSqliteSchema(database.Path).InitializeAsync();
        Assert.Equal(95L, await ScalarAsync(connection, "SELECT MAX(Version) FROM VeteransClaims_SchemaMigrations"));
    }

    [Fact]
    public async Task NewerUnsupportedLedger_IsRejectedWithoutChangingExistingAuthority()
    {
        using var database = new TestDatabase();
        var schema = new VeteransClaimsSqliteSchema(database.Path);
        await schema.InitializeAsync();
        await using var connection = await database.OpenAsync();
        await ExecuteAsync(connection, """
            INSERT INTO VeteransClaims_ReviewerOperationSnapshots
                (OperationSnapshotId,ReviewerOperationId,State,Revision,OwnerToken,Profile,RepresentationVersion,Disposition)
            VALUES('snapshot','reviewer-operation','Capturing',1,'owner','Reviewer.AdoptedUtf8.ContractProof.v1',1,'Active');
            INSERT INTO VeteransClaims_SchemaMigrations(Version,Name,AppliedUtc) VALUES(96,'FutureReviewerMigration','future');
            """);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => schema.InitializeAsync());
        Assert.Contains("unsupported migration version 96", error.Message);
        Assert.Equal(96L, await ScalarAsync(connection, "SELECT COUNT(*) FROM VeteransClaims_SchemaMigrations"));
        Assert.Equal(1L, await ScalarAsync(connection, $"SELECT COUNT(*) FROM {Table} WHERE OperationSnapshotId='snapshot' AND Revision=1 AND OwnerToken='owner'"));
    }

    private static Task<object?> DomainRowsAsync(SqliteConnection connection) => ScalarAsync(connection, """
        SELECT json_object(
            'veterans',(SELECT json_group_array(json_object('id',Id)) FROM VeteransClaims_Veterans),
            'claims',(SELECT json_group_array(json_object('id',Id,'veteran',VeteranId)) FROM VeteransClaims_Claims),
            'issues',(SELECT json_group_array(json_object('id',Id,'claim',ClaimId,'type',ClaimIssueType)) FROM VeteransClaims_ClaimIssues),
            'packages',(SELECT json_group_array(json_object('id',Id,'issue',ClaimIssueId,'purpose',Purpose,
                'role',ReviewerRole,'version',ReviewerSnapshotVersion,'sealed',ReviewerSnapshotSealed)) FROM VeteransClaims_EvidencePackages),
            'snapshots',(SELECT json_group_array(json_object('id',EvidencePackageId,'version',Version,'payload',Payload,'sha',Sha256))
                FROM VeteransClaims_ReviewerPackageSnapshots))
        """);

    private static async Task<object?> ScalarAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static async Task ExecuteAsync(SqliteConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private sealed class TestDatabase : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"{Guid.NewGuid():N}.db");
        public async Task<SqliteConnection> OpenAsync()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = Path, ForeignKeys = true, Pooling = false }.ToString());
            await connection.OpenAsync();
            return connection;
        }
        public void Dispose()
        {
            using var connection = VeteransClaimsSqliteConnectionFactory.Create(Path);
            SqliteConnection.ClearPool(connection);
            File.Delete(Path);
        }
    }
}

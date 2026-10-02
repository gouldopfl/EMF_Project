using EMF.Persistence.Storage;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ContentCatalogBaselineTests
{
    [Theory]
    [InlineData("ArtifactId TEXT PRIMARY KEY", "ArtifactId TEXT")]
    [InlineData("EnumerationSequence INTEGER PRIMARY KEY AUTOINCREMENT", "EnumerationSequence INTEGER")]
    [InlineData("INTEGER PRIMARY KEY AUTOINCREMENT", "INTEGER PRIMARY KEY")]
    [InlineData("OperationId TEXT NOT NULL UNIQUE", "OperationId TEXT NOT NULL")]
    [InlineData("MutationRevision TEXT UNIQUE", "MutationRevision TEXT")]
    [InlineData("Revision TEXT NOT NULL", "Revision TEXT")]
    [InlineData("Length INTEGER NOT NULL", "Length INTEGER")]
    [InlineData("Request TEXT NOT NULL", "Request TEXT")]
    [InlineData("Receipt TEXT NOT NULL", "Receipt TEXT")]
    [InlineData("OperationId TEXT NOT NULL UNIQUE", "OperationId TEXT UNIQUE")]
    [InlineData("Length INTEGER NOT NULL", "Length TEXT NOT NULL")]
    [InlineData("Generation TEXT", "Generation TEXT DEFAULT ''")]
    public void MalformedVersionTwoSchemaIsRejectedWithoutMutation(string original, string replacement)
    {
        using var connection = Open(ContentCatalogSchema.Foundation.Replace(original, replacement, StringComparison.Ordinal));
        var before = Schema(connection);
        var callbackReached = false;
        Assert.Throws<InvalidDataException>(() => ContentCatalogSchema.Upgrade(connection, (_, _) => callbackReached = true));
        Assert.False(callbackReached);
        Assert.Equal(before, Schema(connection));
        Assert.Equal(2L, Scalar(connection, "PRAGMA user_version"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnexpectedOwnedTableTriggerIsRejected(bool upgraded)
    {
        using var connection = Open(ContentCatalogSchema.Foundation);
        if (upgraded) ContentCatalogSchema.Upgrade(connection);
        Execute(connection, "CREATE TRIGGER UnexpectedStateTrigger AFTER INSERT ON ContentState BEGIN DELETE FROM ContentReceipts; END");
        var before = Schema(connection);
        if (upgraded) Assert.Throws<InvalidDataException>(() => ContentCatalogSchema.Validate(connection));
        else Assert.Throws<InvalidDataException>(() => ContentCatalogSchema.Upgrade(connection));
        Assert.Equal(before, Schema(connection));
        Assert.Equal(upgraded ? 3L : 2L, Scalar(connection, "PRAGMA user_version"));
    }

    [Fact]
    public void NonemptyBaselineRequiresSemanticValidator()
    {
        using var connection = Open(ContentCatalogSchema.Foundation);
        Execute(connection, "INSERT INTO ContentState VALUES('artifact','revision',NULL,0,NULL)");
        var before = Schema(connection);
        Assert.Throws<InvalidDataException>(() => ContentCatalogSchema.Upgrade(connection));
        Assert.Equal(before, Schema(connection));
        Assert.Equal(2L, Scalar(connection, "PRAGMA user_version"));
    }

    [Fact]
    public void SemanticValidationFailureRollsBackBeforeSchemaUpgrade()
    {
        using var connection = Open(ContentCatalogSchema.Foundation);
        Execute(connection, "INSERT INTO ContentState VALUES('artifact','revision',NULL,0,NULL)");
        var before = Schema(connection);
        Assert.Throws<InvalidDataException>(() => ContentCatalogSchema.Upgrade(connection, (catalog, transaction) =>
        {
            using var command = catalog.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT Revision FROM ContentState";
            Assert.Equal("revision", command.ExecuteScalar());
            throw new InvalidDataException("Synthetic incomplete receipt lineage.");
        }));
        Assert.Equal(before, Schema(connection));
        Assert.Equal(2L, Scalar(connection, "PRAGMA user_version"));
        Assert.Equal("revision", Scalar(connection, "SELECT Revision FROM ContentState"));
    }

    [Fact]
    public void GenuineFoundationUpgradesOnceAndPreservesExistingData()
    {
        using var connection = Open(ContentCatalogSchema.Foundation);
        Execute(connection, "INSERT INTO ContentState VALUES('artifact','revision',NULL,0,NULL)");
        var validations = 0;
        ContentCatalogSchema.Upgrade(connection, (catalog, transaction) =>
        {
            validations++;
            using var command = catalog.CreateCommand(); command.Transaction = transaction;
            command.CommandText = "SELECT Revision FROM ContentState";
            Assert.Equal("revision", command.ExecuteScalar());
        });
        ContentCatalogSchema.Upgrade(connection, (_, _) => validations++);
        ContentCatalogSchema.Validate(connection);
        Assert.Equal(1, validations);
        Assert.Equal(3L, Scalar(connection, "PRAGMA user_version"));
        Assert.Equal(2L, Scalar(connection, "SELECT count(*) FROM ContentSchemaMigrations"));
        Assert.Equal("revision", Scalar(connection, "SELECT Revision FROM ContentState"));
    }

    [Theory]
    [InlineData("receipt-insert")]
    [InlineData("receipt-update")]
    [InlineData("origin-insert")]
    [InlineData("origin-update")]
    public void RevisionAuthorityIsUniqueAcrossOriginsAndReceipts(string operation)
    {
        using var connection = Open(ContentCatalogSchema.Foundation);
        ContentCatalogSchema.Upgrade(connection);
        const string receipt = "INSERT INTO ContentReceipts(OperationId,Request,Receipt,MutationRevision) VALUES('op','request','receipt','shared')";
        const string origin = "INSERT INTO ContentMigrationArtifacts VALUES('other-artifact',1,'stamp','digest','generation','shared','Retained')";
        if (operation.StartsWith("receipt", StringComparison.Ordinal))
        {
            Execute(connection, origin);
            if (operation == "receipt-insert") Assert.Throws<SqliteException>(() => Execute(connection, receipt));
            else
            {
                Execute(connection, receipt.Replace("'shared'", "'distinct'", StringComparison.Ordinal));
                Assert.Throws<SqliteException>(() => Execute(connection, "UPDATE ContentReceipts SET MutationRevision='shared'"));
                Assert.Equal("distinct", Scalar(connection, "SELECT MutationRevision FROM ContentReceipts"));
            }
        }
        else
        {
            Execute(connection, receipt);
            if (operation == "origin-insert") Assert.Throws<SqliteException>(() => Execute(connection, origin));
            else
            {
                Execute(connection, origin.Replace("'shared'", "'distinct'", StringComparison.Ordinal));
                Assert.Throws<SqliteException>(() => Execute(connection, "UPDATE ContentMigrationArtifacts SET Revision='shared'"));
                Assert.Equal("distinct", Scalar(connection, "SELECT Revision FROM ContentMigrationArtifacts"));
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MissingOrWeakenedLineageTriggerFailsSchemaValidation(bool replaced)
    {
        using var connection = Open(ContentCatalogSchema.Foundation);
        ContentCatalogSchema.Upgrade(connection);
        Execute(connection, "DROP TRIGGER ContentOriginReceiptInsert");
        if (replaced) Execute(connection, "CREATE TRIGGER ContentOriginReceiptInsert BEFORE INSERT ON ContentMigrationArtifacts BEGIN SELECT 1; END");
        Assert.Throws<InvalidDataException>(() => ContentCatalogSchema.Validate(connection));
    }

    private static SqliteConnection Open(string schema)
    {
        var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        Execute(connection, schema); return connection;
    }
    private static void Execute(SqliteConnection connection, string sql)
    { using var command = connection.CreateCommand(); command.CommandText = sql; command.ExecuteNonQuery(); }
    private static object? Scalar(SqliteConnection connection, string sql)
    { using var command = connection.CreateCommand(); command.CommandText = sql; return command.ExecuteScalar(); }
    private static string Schema(SqliteConnection connection)
        => (string)Scalar(connection, "SELECT group_concat(name || ':' || coalesce(sql,''),char(10)) FROM (SELECT name,sql FROM sqlite_master ORDER BY name)")!;
}

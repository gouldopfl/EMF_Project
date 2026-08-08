using EMF.Persistence.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class PersistenceTests
{
    [Fact]
    public async Task SqliteEvidenceRepository_InitializeAsync_CreatesSchema()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-persistence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            Assert.True(File.Exists(databasePath));

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath}");

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('Artifacts', 'Relationships');
                """;

            var tables = new List<string>();

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            Assert.Contains("Artifacts", tables);
            Assert.Contains("Relationships", tables);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }
}

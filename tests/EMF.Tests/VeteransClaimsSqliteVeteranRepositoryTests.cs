using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteVeteranRepositoryTests
{
    [Fact]
    public async Task Repository_StoresAndRetrievesVeteranThroughContract()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var sqliteRepository =
                new SqliteVeteranRepository(databasePath);

            await sqliteRepository.InitializeAsync();

            IVeteranRepository repository =
                sqliteRepository;

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-001")
            };

            await repository.AddVeteranAsync(veteran);

            var stored =
                await repository.GetVeteranAsync(veteran.Id);

            Assert.NotNull(stored);
            Assert.Equal(veteran.Id, stored!.Id);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Repository_ReturnsNullForMissingVeteran()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteVeteranRepository(databasePath);

            await repository.InitializeAsync();

            var stored =
                await repository.GetVeteranAsync(
                    new VeteranId("missing-veteran"));

            Assert.Null(stored);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task InitializeAsync_IsRepeatableAndDomainScoped()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteVeteranRepository(databasePath);

            await repository.InitializeAsync();
            await repository.InitializeAsync();

            var builder = new SqliteConnectionStringBuilder
            {
                DataSource = databasePath
            };

            await using var connection =
                new SqliteConnection(builder.ToString());

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                ORDER BY name;
                """;

            var tableNames = new List<string>();

            await using var reader =
                await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tableNames.Add(reader.GetString(0));
            }

            Assert.Contains(
                "VeteransClaims_Veterans",
                tableNames);

            Assert.DoesNotContain(
                "Artifacts",
                tableNames);

            Assert.DoesNotContain(
                "WorkflowExecutions",
                tableNames);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}

using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;
using EMF.Discovery.Services;
using EMF.Discovery.Models;
using EMF.Inventory.Providers;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class OrchestrationTests
{
    [Fact]
    public void SelectProvider_ForSqliteDatabase_ReturnsSqliteProvider()
    {
        var item = new DiscoveredItem
        {
            Name = "evidence.db",
            SourcePath = "/data/evidence.db",
            SourceType = "file"
        };

        var service = new InventoryRoutingService(
            new[] { new SqliteInventoryProvider() });

        var provider = service.SelectProvider(item);

        Assert.IsType<SqliteInventoryProvider>(provider);
    }

    [Fact]
    public void SelectProvider_ForUnsupportedFile_ReturnsNull()
    {
        var item = new DiscoveredItem
        {
            Name = "evidence.pdf",
            SourcePath = "/data/evidence.pdf",
            SourceType = "file"
        };

        var service = new InventoryRoutingService(
            new[] { new SqliteInventoryProvider() });

        var provider = service.SelectProvider(item);

        Assert.Null(provider);
    }

    [Fact]
    public async Task DiscoveryToInventory_EndToEnd_ProcessesSqliteDatabase()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"emf-orchestration-{Guid.NewGuid():N}");

        Directory.CreateDirectory(rootPath);

        var databasePath = Path.Combine(rootPath, "evidence.db");

        try
        {
            var connectionString =
                new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
                {
                    DataSource = databasePath
                }.ToString();

            await using (var connection =
                new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
            {
                await connection.OpenAsync();

                await using var command = connection.CreateCommand();

                command.CommandText =
                    """
                    CREATE TABLE evidence (
                        id INTEGER PRIMARY KEY,
                        name TEXT NOT NULL
                    );

                    INSERT INTO evidence (name) VALUES ('test');
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var discovery =
                new EMF.Discovery.Services.FileSystemDiscoveryService();

            var routing = new InventoryRoutingService(
                new[] { new SqliteInventoryProvider() });

            DiscoveredItem? discoveredDatabase = null;

            await foreach (var item in discovery.DiscoverItemsAsync(
                rootPath,
                new EMF.Discovery.Models.DiscoveryOptions()))
            {
                if (item.SourcePath == databasePath)
                {
                    discoveredDatabase = item;
                    break;
                }
            }

            Assert.NotNull(discoveredDatabase);

            var provider = routing.SelectProvider(discoveredDatabase);

            Assert.NotNull(provider);

            var inventory = await provider.CreateInventoryAsync(
                discoveredDatabase.SourcePath);

            Assert.Equal("SQLite", inventory.DatabaseEngine);

            var table = Assert.Single(inventory.Tables);

            Assert.Equal("evidence", table.Name);
            Assert.Equal(1, table.RowCount);
        }
        finally
        {
            if (Directory.Exists(rootPath))
            {
                Directory.Delete(rootPath, recursive: true);
            }
        }
    }

[Fact]
public async Task InventoryOrchestrationService_ExecutesDiscoveryRoutingAndInventory()
{
    var rootPath = Path.Combine(
        Path.GetTempPath(),
        $"emf-orchestration-{Guid.NewGuid():N}");

    Directory.CreateDirectory(rootPath);

    var databasePath = Path.Combine(rootPath, "test.db");

    try
    {
        await using (var connection =
            new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={databasePath}"))
        {
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE sample (id INTEGER PRIMARY KEY, name TEXT);";

            await command.ExecuteNonQueryAsync();
        }

        var discovery = new FileSystemDiscoveryService();

        var routing = new InventoryRoutingService(
            new[] { new SqliteInventoryProvider() });

        var service = new InventoryOrchestrationService(
            discovery,
            routing);

        var results = new List<InventoryOrchestrationResult>();

        await foreach (var orchestrationResult in service.ExecuteAsync(
            rootPath,
            new DiscoveryOptions()))
        {
            results.Add(orchestrationResult);
        }

        var result = Assert.Single(results);

        Assert.True(result.Success);
        Assert.NotNull(result.Inventory);
        Assert.Equal(databasePath, result.DiscoveredItem.SourcePath);
        Assert.Equal(databasePath, result.Inventory.DatabasePath);
        Assert.Contains(
            result.Inventory.Tables,
            table => table.Name == "sample");
    }
    finally
    {
        Directory.Delete(rootPath, recursive: true);
    }
}


[Fact]
public async Task InventoryOrchestrationService_TracksStatistics()
{
    var rootPath = Path.Combine(
        Path.GetTempPath(),
        $"emf-orchestration-stats-{Guid.NewGuid():N}");

    Directory.CreateDirectory(rootPath);

    var databasePath = Path.Combine(rootPath, "stats.db");
    var ignoredPath = Path.Combine(rootPath, "notes.txt");

    try
    {
        await File.WriteAllTextAsync(ignoredPath, "not a database");

        await using (var connection =
            new Microsoft.Data.Sqlite.SqliteConnection(
                $"Data Source={databasePath}"))
        {
            await connection.OpenAsync();

            var command = connection.CreateCommand();
            command.CommandText =
                "CREATE TABLE sample (id INTEGER PRIMARY KEY);";

            await command.ExecuteNonQueryAsync();
        }

        var discovery = new FileSystemDiscoveryService();

        var routing = new InventoryRoutingService(
            new[] { new SqliteInventoryProvider() });

        var service = new InventoryOrchestrationService(
            discovery,
            routing);

        await foreach (var _ in service.ExecuteAsync(
            rootPath,
            new DiscoveryOptions()))
        {
        }

        Assert.Equal(2, service.Statistics.ItemsDiscovered);
        Assert.Equal(1, service.Statistics.ItemsHandled);
        Assert.Equal(1, service.Statistics.ItemsSkipped);
        Assert.Equal(1, service.Statistics.InventoriesCompleted);
        Assert.Equal(0, service.Statistics.ItemsFailed);
        Assert.True(service.Statistics.Elapsed >= TimeSpan.Zero);
    }
    finally
    {
        Directory.Delete(rootPath, recursive: true);
    }
}

    [Fact]
    public async Task InventoryOrchestrationService_ContinuesAfterProviderFailure()
    {
        var rootPath = Path.Combine(
            Path.GetTempPath(),
            $"emf-failure-{Guid.NewGuid():N}");

        Directory.CreateDirectory(rootPath);

        var badPath = Path.Combine(rootPath, "bad.db");
        var goodPath = Path.Combine(rootPath, "good.db");

        try
        {
            await File.WriteAllTextAsync(
                badPath,
                "not a sqlite database");

            await using (var connection =
                new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={goodPath}"))
            {
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText =
                    "CREATE TABLE evidence (id INTEGER PRIMARY KEY);";

                await command.ExecuteNonQueryAsync();
            }

            var service = new InventoryOrchestrationService(
                new FileSystemDiscoveryService(),
                new InventoryRoutingService(
                    new[] { new SqliteInventoryProvider() }));

            var results = new List<InventoryOrchestrationResult>();

            await foreach (var result in service.ExecuteAsync(
                rootPath,
                new DiscoveryOptions()))
            {
                results.Add(result);
            }

            Assert.Equal(2, results.Count);
            Assert.Single(results, r => r.Success);
            Assert.Single(results, r => !r.Success);

            Assert.Equal(2, service.Statistics.ItemsHandled);
            Assert.Equal(1, service.Statistics.InventoriesCompleted);
            Assert.Equal(1, service.Statistics.ItemsFailed);
        }
        finally
        {
            Directory.Delete(rootPath, recursive: true);
        }
    }


[Fact]
public void ArtifactFactory_MapsDiscoveredItemToArtifactAndProvenance()
{
    var createdUtc = DateTimeOffset.UtcNow.AddMinutes(-10);
    var modifiedUtc = DateTimeOffset.UtcNow.AddMinutes(-5);

    var item = new DiscoveredItem
    {
        Name = "oscar.db",
        SourcePath = "/opt/emf-lab/datasets/LD-VET-001/extracted/oscar.db",
        SourceType = "file",
        SizeBytes = 1276899328,
        CreatedUtc = createdUtc,
        ModifiedUtc = modifiedUtc,
        Metadata = new Dictionary<string, object>
        {
            ["extension"] = ".db"
        }
    };

    var factory = new ArtifactFactory();

    var result = factory.Create(
        item,
        new ArtifactId("artifact-oscar-001"));

    Assert.Equal("artifact-oscar-001", result.Artifact.Id.Value);
    Assert.Equal("oscar.db", result.Artifact.Name);
    Assert.Equal("file", result.Artifact.ArtifactType);
    Assert.Equal(createdUtc, result.Artifact.CreatedUtc);

    Assert.Equal(
        "/opt/emf-lab/datasets/LD-VET-001/extracted/oscar.db",
        result.Provenance.Source);

    Assert.Equal(
        result.Artifact.Id,
        result.Provenance.ArtifactId);

    Assert.Equal(
        ".db",
        result.Artifact.Metadata["extension"]);

    Assert.Equal(
        1276899328L,
        result.Artifact.Metadata["sizeBytes"]);
}


[Fact]
public void GuidArtifactIdGenerator_GeneratesUniqueIds()
{
    var generator = new GuidArtifactIdGenerator();

    var first = generator.Generate();
    var second = generator.Generate();

    Assert.False(string.IsNullOrWhiteSpace(first.Value));
    Assert.False(string.IsNullOrWhiteSpace(second.Value));
    Assert.NotEqual(first, second);
}
}

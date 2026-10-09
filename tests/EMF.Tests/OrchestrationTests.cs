using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Models;
using EMF.Discovery.Services;
using EMF.Discovery.Models;
using EMF.Inventory.Providers;
using EMF.Integrity;
using EMF.Orchestration.Services;
using EMF.Persistence.Storage;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class OrchestrationTests
{
    private static InventoryOrchestrationService CreateInventoryService(InventoryMigrationFixture runtime,
        EMF.Discovery.Contracts.IStreamingDiscoveryService discovery, EMF.Orchestration.Contracts.IInventoryRoutingService routing,
        EMF.Orchestration.Contracts.IArtifactFactory factory, EMF.Orchestration.Contracts.IArtifactIdGenerator ids, EMF.Core.Contracts.IContentFingerprintService fingerprints)
        => new(discovery, routing, factory, ids, fingerprints, runtime.Snapshots, runtime.Journal, runtime.Workspace, runtime.Limits);

    private sealed class WrongIdentityFactory :
        EMF.Orchestration.Contracts.IArtifactFactory
    {
        public ArtifactCreationResult Create(
            DiscoveredItem item, ArtifactId artifactId,
            EMF.Core.Models.Integrity.ContentFingerprint? fingerprint) =>
            new ArtifactFactory().Create(
                item, new ArtifactId("wrong-artifact"), fingerprint);
    }


    [Fact]
    public async Task InventoryOrchestrationService_RejectsFactoryArtifactIdentityMismatch()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        var path = Path.Combine(Path.GetTempPath(), $"emf-orchestration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            await runtime.SqlAsync(Path.Combine(path, "bad.db"), "CREATE TABLE guard(id INTEGER);");

            var service = CreateInventoryService(runtime,
                new FileSystemDiscoveryService(),
                new InventoryRoutingService([new SqliteInventoryProvider()]),
                new WrongIdentityFactory(),
                new GuidArtifactIdGenerator(),
                new Sha256ContentFingerprintService());

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var _ in service.ExecuteAsync(path, new DiscoveryOptions())) { }
            });
        }
        finally { Directory.Delete(path, true); }
    }

    [Fact]
    public async Task InventoryOrchestrationService_RejectsFactoryFingerprintMismatch()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        var path = Path.Combine(Path.GetTempPath(), $"emf-orchestration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            await runtime.SqlAsync(Path.Combine(path, "bad.db"), "CREATE TABLE guard(id INTEGER);");

            var service = CreateInventoryService(runtime,
                new FileSystemDiscoveryService(),
                new InventoryRoutingService([new SqliteInventoryProvider()]),
                new WrongFingerprintFactory(),
                new GuidArtifactIdGenerator(),
                new Sha256ContentFingerprintService());

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var _ in service.ExecuteAsync(path, new DiscoveryOptions())) { }
            });
        }
        finally { Directory.Delete(path, true); }
    }

    private sealed class WrongFingerprintFactory :
        EMF.Orchestration.Contracts.IArtifactFactory
    {
        public ArtifactCreationResult Create(
            DiscoveredItem item, ArtifactId artifactId,
            EMF.Core.Models.Integrity.ContentFingerprint? fingerprint) =>
            new ArtifactFactory().Create(
                item, artifactId,
                new EMF.Core.Models.Integrity.ContentFingerprint
                {
                    Algorithm = "SHA256",
                    Value = "different-fingerprint"
                });
    }

    [Fact]
    public async Task InventoryOrchestrationService_RejectsFactoryProvenanceIdentityMismatch()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        var path = Path.Combine(Path.GetTempPath(), $"emf-orchestration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            await runtime.SqlAsync(Path.Combine(path, "bad.db"), "CREATE TABLE guard(id INTEGER);");

            var service = CreateInventoryService(runtime,
                new FileSystemDiscoveryService(),
                new InventoryRoutingService([new SqliteInventoryProvider()]),
                new WrongProvenanceIdentityFactory(),
                new GuidArtifactIdGenerator(),
                new Sha256ContentFingerprintService());

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var _ in service.ExecuteAsync(path, new DiscoveryOptions())) { }
            });
        }
        finally { Directory.Delete(path, true); }
    }

    private sealed class WrongProvenanceIdentityFactory :
        EMF.Orchestration.Contracts.IArtifactFactory
    {
        public ArtifactCreationResult Create(
            DiscoveredItem item, ArtifactId artifactId,
            EMF.Core.Models.Integrity.ContentFingerprint? fingerprint)
        {
            var valid = new ArtifactFactory().Create(item, artifactId, fingerprint);
            return new ArtifactCreationResult
            {
                Artifact = valid.Artifact,
                Provenance = new Provenance
                {
                    ArtifactId = new ArtifactId("wrong-artifact"),
                    Source = valid.Provenance.Source,
                    RecordedBy = valid.Provenance.RecordedBy
                }
            };
        }
    }

    [Fact]
    public async Task InventoryOrchestrationService_RejectsFactoryProvenanceSourceMismatch()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        var path = Path.Combine(Path.GetTempPath(), $"emf-orchestration-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        try
        {
            await runtime.SqlAsync(Path.Combine(path, "bad.db"), "CREATE TABLE guard(id INTEGER);");

            var service = CreateInventoryService(runtime,
                new FileSystemDiscoveryService(),
                new InventoryRoutingService([new SqliteInventoryProvider()]),
                new WrongProvenanceSourceFactory(),
                new GuidArtifactIdGenerator(),
                new Sha256ContentFingerprintService());

            await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            {
                await foreach (var _ in service.ExecuteAsync(path, new DiscoveryOptions())) { }
            });
        }
        finally { Directory.Delete(path, true); }
    }

    private sealed class WrongProvenanceSourceFactory :
        EMF.Orchestration.Contracts.IArtifactFactory
    {
        public ArtifactCreationResult Create(
            DiscoveredItem item, ArtifactId artifactId,
            EMF.Core.Models.Integrity.ContentFingerprint? fingerprint)
        {
            var valid = new ArtifactFactory().Create(item, artifactId, fingerprint);
            return new ArtifactCreationResult
            {
                Artifact = valid.Artifact,
                Provenance = new Provenance
                {
                    ArtifactId = artifactId,
                    Source = "different-source",
                    RecordedBy = valid.Provenance.RecordedBy
                }
            };
        }
    }

    [Fact]
    public async Task InventoryOrchestrationService_RejectsDirectContentFallback()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        Assert.Throws<NotSupportedException>(() => new InventoryOrchestrationService(new FileSystemDiscoveryService(),
            new InventoryRoutingService([new SqliteInventoryProvider()]), new ArtifactFactory(), new GuidArtifactIdGenerator(),
            new Sha256ContentFingerprintService(), f.Ingestion.Physical));
        Assert.Null(await f.Ingestion.Physical.ReadAsync(f.Ingestion.Id));
    }



    [Fact]
    public async Task InventoryOrchestrationService_RejectsUnprotectedContentPublication()
    {
        await using var f = await InventoryMigrationFixture.CreateAsync();
        Assert.Throws<NotSupportedException>(() => new InventoryOrchestrationService(new FileSystemDiscoveryService(),
            new InventoryRoutingService([new SqliteInventoryProvider()]), new ArtifactFactory(), new GuidArtifactIdGenerator(),
            new Sha256ContentFingerprintService(), f.Ingestion.Physical));
        Assert.Null(await f.Ingestion.Physical.ReadAsync(f.Ingestion.Id));
    }


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
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
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

            var service = CreateInventoryService(runtime,
                discovery,
                routing,
                new ArtifactFactory(),
                new GuidArtifactIdGenerator(),
            new Sha256ContentFingerprintService());

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

            Assert.False(string.IsNullOrWhiteSpace(result.Artifact.Id.Value));
            Assert.Equal("test.db", result.Artifact.Name);

            Assert.NotNull(result.Artifact.Fingerprint);
            Assert.Equal("SHA-256", result.Artifact.Fingerprint.Algorithm);

            Assert.Equal(
                result.Artifact.Id,
                result.Provenance.ArtifactId);

            Assert.Equal(
                databasePath,
                result.Provenance.Source);
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
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
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

            var service = CreateInventoryService(runtime,
                discovery,
                routing,
                new ArtifactFactory(),
                new GuidArtifactIdGenerator(),
            new Sha256ContentFingerprintService());

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
    public async Task InventoryOrchestrationService_RejectsInvalidInputBeforeWholePlanAdmission()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        await File.WriteAllTextAsync(Path.Combine(runtime.Source, "b.sqlite"), "invalid SQLite");
        await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(async () =>
        { await foreach (var item in runtime.Service.ExecuteAsync(runtime.Source, new DiscoveryOptions())) { Assert.Fail("No result may be yielded before all compatibility snapshots are prepared."); } });
        await AssertNoAnonymousInventoryStateAsync(runtime);
    }


    private static async Task AssertNoAnonymousInventoryStateAsync(InventoryMigrationFixture runtime)
    {
        await using var c = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = Path.Combine(runtime.Root, "inventory-journal", "parent.sqlite"), Pooling = false }.ToString());
        await c.OpenAsync();
        foreach (var table in new[] { "InventoryParents", "InventoryPlanItems", "InventoryConfirmations", "InventoryRetention" })
        {
            using var command = c.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM " + table;
            Assert.Equal(0L, Convert.ToInt64(await command.ExecuteScalarAsync()));
        }
        Assert.Empty(Directory.EnumerateDirectories(runtime.Workspace.Root, "work-*"));
        Assert.False(Directory.Exists(Path.Combine(runtime.Root, "inventory-retained")));
    }

    [Fact]
    public async Task InventoryPublicEnumeration_CompletesInspectionBeforeYieldWithoutPublicationOrRetention()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        await runtime.SqlAsync(Path.Combine(runtime.Source, "b.sqlite"), "CREATE TABLE second(id INTEGER);");
        var count = 0;
        await foreach (var result in runtime.Service.ExecuteAsync(runtime.Source, new()))
        {
            count++;
            Assert.True(result.Success);
            Assert.Equal(count, runtime.Service.Statistics.InventoriesCompleted);
            Assert.Null(await runtime.Ingestion.Repository.GetArtifactAsync(result.Artifact.Id));
            Assert.Null(await runtime.Ingestion.Physical.ReadAsync(result.Artifact.Id));
            Assert.NotNull(result.Inventory);
        }
        Assert.Equal(2, count);
        await AssertNoAnonymousInventoryStateAsync(runtime);
    }

    [Fact]
    public async Task InventoryPublicEnumeration_DisposalAfterFirstItemReleasesAllEphemeralInputs()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        await runtime.SqlAsync(Path.Combine(runtime.Source, "b.sqlite"), "CREATE TABLE second(id INTEGER);");
        await using (var iterator = runtime.Service.ExecuteAsync(runtime.Source, new()).GetAsyncEnumerator())
        {
            Assert.True(await iterator.MoveNextAsync());
            Assert.Equal(1, runtime.Service.Statistics.InventoriesCompleted);
            // The yielded item's own lease is gone; only the unconsumed item remains.
            Assert.Single(Directory.EnumerateDirectories(runtime.Workspace.Root, "work-*"));
        }
        await AssertNoAnonymousInventoryStateAsync(runtime);
        Assert.Equal(1, runtime.Service.Statistics.InventoriesCompleted);
    }

    [Fact]
    public async Task InventoryPublicEnumeration_CancellationBetweenItemsReleasesInputsWithoutInventingCompletion()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        await runtime.SqlAsync(Path.Combine(runtime.Source, "b.sqlite"), "CREATE TABLE second(id INTEGER);");
        using var cancellation = new CancellationTokenSource();
        await using var iterator = runtime.Service.ExecuteAsync(runtime.Source, new(), cancellation.Token).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => { await iterator.MoveNextAsync(); });
        Assert.Equal(1, runtime.Service.Statistics.InventoriesCompleted);
        await AssertNoAnonymousInventoryStateAsync(runtime);
    }

    private sealed class FailSecondInspectionProvider : EMF.Inventory.Contracts.IInventoryProvider
    {
        private readonly SqliteInventoryProvider _inner = new();
        private int _calls;
        public bool CanHandle(string path) => _inner.CanHandle(path);
        public Task<EMF.Inventory.Models.DatabaseInventory> CreateInventoryAsync(string path, CancellationToken ct = default)
            => ++_calls == 2 ? Task.FromException<EMF.Inventory.Models.DatabaseInventory>(new IOException("Second inspection failed.")) : _inner.CreateInventoryAsync(path, ct);
    }

    [Fact]
    public async Task InventoryPublicEnumeration_LaterFailureCleansInputsAndPreservesExistingAdoption()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        var adopted = await runtime.Ingestion.Service().IngestAsync(runtime.Ingestion.Draft, runtime.Ingestion.Content);
        var before = await runtime.Ingestion.Physical.ReadAsync(adopted.Result!.Artifact.Id);
        Assert.NotNull(before);
        await runtime.SqlAsync(Path.Combine(runtime.Source, "b.sqlite"), "CREATE TABLE second(id INTEGER);");
        var service = CreateInventoryService(runtime, new InventoryBoundedDiscoveryService(runtime.Limits),
            new InventoryRoutingService(new[] { new FailSecondInspectionProvider() }), new ArtifactFactory(), new GuidArtifactIdGenerator(), runtime.Ingestion.Fingerprints);
        await using var iterator = service.ExecuteAsync(runtime.Source, new()).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        await Assert.ThrowsAsync<IOException>(async () => { await iterator.MoveNextAsync(); });
        Assert.Equal(1, service.Statistics.InventoriesCompleted);
        Assert.Equal(1, service.Statistics.ItemsFailed);
        await AssertNoAnonymousInventoryStateAsync(runtime);
        Assert.Equal(before, await runtime.Ingestion.Physical.ReadAsync(adopted.Result!.Artifact.Id));
        Assert.NotNull(await runtime.Ingestion.Repository.GetArtifactAsync(adopted.Result!.Artifact.Id));
    }

    private static InventoryOrchestrationService CreateRemovalFaultService(InventoryMigrationFixture runtime,
        Func<string, Task> beforeRemove, EMF.Inventory.Contracts.IInventoryProvider? provider = null)
    {
        var workspace = new EMF.Inventory.Storage.LinuxInventorySnapshotWorkspace(runtime.Workspace.Root, beforeRemove);
        var snapshots = new SqliteInventoryRetainedSnapshotStore(runtime.Journal, runtime.Protection, workspace, runtime.Limits);
        return new(new InventoryBoundedDiscoveryService(runtime.Limits),
            new InventoryRoutingService(new[] { provider ?? new SqliteInventoryProvider(runtime.Limits) }),
            new ArtifactFactory(), new GuidArtifactIdGenerator(), runtime.Ingestion.Fingerprints,
            snapshots, runtime.Journal, workspace, runtime.Limits);
    }

    [Fact]
    public async Task InventoryPublicEnumeration_FirstRemovalFailureDoesNotYieldOrCountAndFinallyRetriesSameLease()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        var attempts = new List<string>();
        var service = CreateRemovalFaultService(runtime, path =>
        {
            attempts.Add(path);
            return attempts.Count == 1 ? Task.FromException(new IOException("First release failed.")) : Task.CompletedTask;
        });
        await using var iterator = service.ExecuteAsync(runtime.Source, new()).GetAsyncEnumerator();
        var failure = await Assert.ThrowsAsync<IOException>(async () => { await iterator.MoveNextAsync(); });
        Assert.Equal("First release failed.", failure.Message);
        Assert.Equal(0, service.Statistics.InventoriesCompleted);
        Assert.Equal(2, attempts.Count);
        Assert.Equal(attempts[0], attempts[1]);
        await AssertNoAnonymousInventoryStateAsync(runtime);
    }

    [Fact]
    public async Task InventoryPublicEnumeration_EarlyDisposalReportsCleanupFailureAndFreshRecoveryRemovesOwnerMarkedInput()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        await runtime.SqlAsync(Path.Combine(runtime.Source, "b.sqlite"), "CREATE TABLE second(id INTEGER);");
        await runtime.SqlAsync(Path.Combine(runtime.Source, "c.sqlite"), "CREATE TABLE third(id INTEGER);");
        var attempts = new List<string>();
        var service = CreateRemovalFaultService(runtime, path =>
        {
            attempts.Add(path);
            return attempts.Count == 2 ? Task.FromException(new IOException("Unconsumed cleanup failed.")) : Task.CompletedTask;
        });
        var iterator = service.ExecuteAsync(runtime.Source, new()).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        var failure = await Assert.ThrowsAsync<AggregateException>(async () => { await iterator.DisposeAsync(); });
        Assert.Equal("Unconsumed cleanup failed.", Assert.IsType<IOException>(Assert.Single(failure.InnerExceptions)).Message);
        Assert.Equal(1, service.Statistics.InventoriesCompleted);
        Assert.Equal(3, attempts.Count);
        Assert.True(File.Exists(attempts[1]));
        Assert.False(File.Exists(attempts[2]));
        Assert.Single(Directory.EnumerateDirectories(runtime.Workspace.Root, "work-*"));
        Assert.True(File.Exists(Path.Combine(Path.GetDirectoryName(attempts[1])!, "owner.json")));
        var restarted = new EMF.Inventory.Storage.LinuxInventorySnapshotWorkspace(runtime.Workspace.Root);
        await restarted.RecoverAsync(runtime.Journal, 10);
        await AssertNoAnonymousInventoryStateAsync(runtime);
    }

    [Fact]
    public async Task InventoryPublicEnumeration_CancellationStillAttemptsCleanupAndReportsBothFailures()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        await runtime.SqlAsync(Path.Combine(runtime.Source, "b.sqlite"), "CREATE TABLE second(id INTEGER);");
        var attempts = new List<string>();
        var service = CreateRemovalFaultService(runtime, path =>
        {
            attempts.Add(path);
            return attempts.Count > 1 ? Task.FromException(new IOException("Cancelled cleanup failed.")) : Task.CompletedTask;
        });
        using var cancellation = new CancellationTokenSource();
        await using var iterator = service.ExecuteAsync(runtime.Source, new(), cancellation.Token).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        cancellation.Cancel();
        var failure = await Assert.ThrowsAsync<AggregateException>(async () => { await iterator.MoveNextAsync(); });
        Assert.Equal(2, failure.InnerExceptions.Count);
        Assert.IsAssignableFrom<OperationCanceledException>(failure.InnerExceptions[0]);
        Assert.Equal("Cancelled cleanup failed.", Assert.IsType<IOException>(failure.InnerExceptions[1]).Message);
        Assert.Equal(2, attempts.Count);
        Assert.True(File.Exists(attempts[1]));
        Assert.Equal(1, service.Statistics.InventoriesCompleted);
        await new EMF.Inventory.Storage.LinuxInventorySnapshotWorkspace(runtime.Workspace.Root).RecoverAsync(runtime.Journal, 10);
        await AssertNoAnonymousInventoryStateAsync(runtime);
    }

    [Fact]
    public async Task InventoryPublicEnumeration_MultipleCleanupFailuresPreserveOriginalAndAttemptEveryRemainingLease()
    {
        await using var runtime = await InventoryMigrationFixture.CreateAsync();
        await runtime.SqlAsync(Path.Combine(runtime.Source, "b.sqlite"), "CREATE TABLE second(id INTEGER);");
        await runtime.SqlAsync(Path.Combine(runtime.Source, "c.sqlite"), "CREATE TABLE third(id INTEGER);");
        var attempts = new List<string>();
        var service = CreateRemovalFaultService(runtime, path =>
        {
            attempts.Add(path);
            return attempts.Count > 1 ? Task.FromException(new IOException("Cleanup failed: " + path)) : Task.CompletedTask;
        }, new FailSecondInspectionProvider());
        await using var iterator = service.ExecuteAsync(runtime.Source, new()).GetAsyncEnumerator();
        Assert.True(await iterator.MoveNextAsync());
        var failure = await Assert.ThrowsAsync<AggregateException>(async () => { await iterator.MoveNextAsync(); });
        Assert.Equal(3, failure.InnerExceptions.Count);
        Assert.Equal("Second inspection failed.", Assert.IsType<IOException>(failure.InnerExceptions[0]).Message);
        Assert.Equal(3, attempts.Count);
        Assert.NotEqual(attempts[1], attempts[2]);
        Assert.Equal("Cleanup failed: " + attempts[1], Assert.IsType<IOException>(failure.InnerExceptions[1]).Message);
        Assert.Equal("Cleanup failed: " + attempts[2], Assert.IsType<IOException>(failure.InnerExceptions[2]).Message);
        Assert.True(File.Exists(attempts[1])); Assert.True(File.Exists(attempts[2]));
        Assert.Equal(1, service.Statistics.InventoriesCompleted);
        Assert.Equal(1, service.Statistics.ItemsFailed);
        await new EMF.Inventory.Storage.LinuxInventorySnapshotWorkspace(runtime.Workspace.Root).RecoverAsync(runtime.Journal, 10);
        await AssertNoAnonymousInventoryStateAsync(runtime);
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
                [ArtifactMetadataKeys.FileExtension] = ".db"
            }
        };

        var factory = new ArtifactFactory();

        var result = factory.Create(
    item,
    new ArtifactId("artifact-oscar-001"),
    new EMF.Core.Models.Integrity.ContentFingerprint
    {
        Algorithm = "SHA-256",
        Value = "test"
    });

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
            result.Artifact.Metadata[
                ArtifactMetadataKeys.FileExtension]);

        Assert.Equal(
            1276899328L,
            result.Artifact.Metadata["sizeBytes"]);
    }


    [Fact]
    public void ArtifactFactory_DerivesFileExtension()
    {
        var item = new DiscoveredItem
        {
            Name = "claim-evidence.pdf",
            SourcePath = "/evidence/claim-evidence.pdf",
            SourceType = "file"
        };

        var result =
            new ArtifactFactory().Create(
                item,
                new ArtifactId("artifact-pdf-001"),
                null);

        Assert.Equal(
            ".pdf",
            result.Artifact.Metadata[
                ArtifactMetadataKeys.FileExtension]);
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

    [Fact]
    public async Task EvidencePersistenceService_PersistsArtifactAndProvenance()
    {
        var repository = new InMemoryEvidenceRepository();
        var service = new EvidencePersistenceService(repository);

        var artifactId = new ArtifactId("artifact-persist-001");

        var result = new InventoryOrchestrationResult
        {
            DiscoveredItem = new DiscoveredItem
            {
                Name = "evidence.db",
                SourcePath = "/data/evidence.db",
                SourceType = "file"
            },
            Artifact = new Artifact
            {
                Id = artifactId,
                Name = "evidence.db",
                ArtifactType = "file"
            },
            Provenance = new Provenance
            {
                ArtifactId = artifactId,
                Source = "/data/evidence.db",
                RecordedBy = "EMF.Discovery"
            },
            Success = true
        };

        await service.PersistAsync(result);

        var storedArtifact =
            await repository.GetArtifactAsync(artifactId);

        var storedProvenance =
            await repository.GetProvenanceAsync(artifactId);

        Assert.NotNull(storedArtifact);
        Assert.Equal("evidence.db", storedArtifact!.Name);

        var provenance = Assert.Single(storedProvenance);
        Assert.Equal("/data/evidence.db", provenance.Source);
        Assert.Equal("EMF.Discovery", provenance.RecordedBy);
    }



}

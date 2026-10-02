using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ArtifactContentMigrationTests
{
    private static readonly ArtifactContentOfflineMigrationOptions Offline = new(true);

    [Fact]
    public async Task OfflineAttestationIsRequiredBeforeAnyMutation()
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1 });
        var before = f.RootEntries();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new FileSystemArtifactContentMigration().MigrateAsync(f.Root, new(false)));
        Assert.Equal(before, f.RootEntries()); Assert.False(Directory.Exists(f.Workspace));
    }

    [Fact]
    public async Task OneArtifactImportsExactBytesWithMigrationOriginAndNoInventedReceipt()
    {
        using var f = new Fixture(); var bytes = Enumerable.Range(0, 256).Select(value => (byte)value).ToArray(); f.Add("legacy", bytes);
        var sourceIdentity = ContentStoragePlatform.Select().InspectSourceFile(Path.Combine(f.Root, "legacy"));
        await Assert.ThrowsAsync<ArtifactContentMigrationRequiredException>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
        var result = await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        Assert.Equal(1, result.ArtifactCount); Assert.False(result.AlreadyCompleted);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(result.RetainedDirectory, "legacy")));
        Assert.Equal(sourceIdentity, ContentStoragePlatform.Select().InspectSourceFile(Path.Combine(result.RetainedDirectory, "legacy")));
        var store = new FileSystemArtifactContentStore(f.Root);
        var original = await store.ReadVersionedAsync(new("legacy")); Assert.Equal(bytes, original!.Content);
        Assert.Empty(await store.ReadAuditObligationsAsync(null, 10));
        Assert.Equal(0L, f.Scalar("SELECT COUNT(*) FROM ContentReceipts"));
        Assert.Equal(1L, f.Scalar("SELECT COUNT(*) FROM ContentMigrationArtifacts WHERE Status='Retained'"));
        Assert.Equal(1L, f.Scalar("SELECT COUNT(*) FROM ContentState WHERE Owner IS NULL"));
        var mutation = await store.ReplaceIfRevisionMatchesAsync(new("legacy"), original.Revision, new byte[] { 9 }, new(ArtifactContentOperationId.New()));
        Assert.Equal(original.Revision, mutation.PriorRevision); Assert.Equal(ArtifactContentMutationOutcome.Replaced, mutation.Outcome);
        Assert.Equal(1L, f.Scalar("SELECT COUNT(*) FROM ContentReceipts"));
        Assert.Equal(new byte[] { 9 }, await new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
        var repeated = await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        Assert.True(repeated.AlreadyCompleted);
        Assert.Equal(new byte[] { 9 }, await store.ReadAsync(new("legacy")));
    }

    [Fact]
    public async Task MultipleUnicodeAndIdenticalPayloadArtifactsKeepExactIdentityAndDistinctRevisions()
    {
        using var f = new Fixture(); var names = new[] { "資料 café", "same-one", "same-two", "empty" };
        foreach (var name in names) f.Add(name, name == "empty" ? Array.Empty<byte>() : new byte[] { 1, 2, 3 });
        var result = await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        Assert.Equal(names.Length, result.ArtifactCount);
        var revisions = new HashSet<ArtifactContentRevision>();
        foreach (var name in names)
        {
            var snapshot = await new FileSystemArtifactContentStore(f.Root).ReadVersionedAsync(new(name));
            Assert.Equal(File.ReadAllBytes(Path.Combine(result.RetainedDirectory, name)), snapshot!.Content);
            Assert.True(revisions.Add(snapshot.Revision)); Assert.Equal(32, snapshot.Revision.Value.Length);
        }
        Assert.Equal(0L, f.Scalar("SELECT COUNT(*) FROM ContentReceipts"));
    }

    [Theory]
    [InlineData(".content-coordination")]
    [InlineData(".content-store-format")]
    [InlineData(".content-format")]
    [InlineData(".content-catalog.sqlite")]
    [InlineData(".content-generations")]
    [InlineData(".content-catalog.sqlite-journal")]
    [InlineData(".content-catalog.sqlite-wal")]
    [InlineData(".content-catalog.sqlite-shm")]
    public async Task ReservedLegacyFilenameIsRetainedWithoutOverwrite(string name)
    {
        using var f = new Fixture(); var bytes = new byte[] { 6, 5, 4, 3 }; f.Add(name, bytes);
        var result = await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(result.RetainedDirectory, name)));
        Assert.Equal(bytes, await new FileSystemArtifactContentStore(f.Root).ReadAsync(new(name)));
        Assert.Equal(0L, f.Scalar("SELECT COUNT(*) FROM ContentReceipts"));
    }

    [Theory]
    [InlineData("InventoryPersisted")]
    [InlineData("CandidateWritten")]
    [InlineData("GenerationDurable")]
    [InlineData("BeforeArtifactCommit")]
    [InlineData("ArtifactCommitted")]
    [InlineData("VerificationRecorded")]
    [InlineData("ReadyForCutover")]
    [InlineData("LegacyRelocated")]
    [InlineData("ProtocolPublished")]
    [InlineData("catalog-publicationCandidateDurable")]
    [InlineData("catalog-publicationPublished")]
    [InlineData("gate-publicationCandidateDurable")]
    [InlineData("gate-publicationPublished")]
    [InlineData(".completedCandidateDurable")]
    [InlineData(".completedPublished")]
    [InlineData("BeforeCompletionMarker")]
    [InlineData("MigrationCompleted")]
    public async Task CrashResumeRecognizesCheckpointsAndDoesNotMintAnotherRevision(string point)
    {
        using var f = new Fixture(); f.Add("one", new byte[] { 1 }); f.Add("two", new byte[] { 2 });
        var interrupted = new FileSystemArtifactContentMigration { Checkpoint = at => at == point ? throw new IOException("synthetic interruption") : Task.CompletedTask };
        await Assert.ThrowsAsync<IOException>(() => interrupted.MigrateAsync(f.Root, Offline));
        var revisions = f.CheckpointRevisions();
        if (point is not ("MigrationCompleted" or ".completedPublished"))
            await Assert.ThrowsAsync<ArtifactContentMigrationInProgressException>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("one")));
        var result = await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        Assert.Equal(revisions, f.CheckpointRevisions());
        Assert.Equal(2L, f.Scalar("SELECT COUNT(*) FROM ContentState")); Assert.Equal(0L, f.Scalar("SELECT COUNT(*) FROM ContentReceipts"));
        Assert.Equal(new byte[] { 1 }, await new FileSystemArtifactContentStore(f.Root).ReadAsync(new("one")));
        Assert.Equal(new byte[] { 2 }, File.ReadAllBytes(Path.Combine(result.RetainedDirectory, "two")));
    }

    [Theory]
    [InlineData("bytes")]
    [InlineData("missing")]
    [InlineData("new-entry")]
    public async Task ChangedSourceOnResumeFailsClosed(string damage)
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1 });
        var interrupted = new FileSystemArtifactContentMigration { Checkpoint = point => point == "InventoryPersisted" ? throw new IOException("synthetic interruption") : Task.CompletedTask };
        await Assert.ThrowsAsync<IOException>(() => interrupted.MigrateAsync(f.Root, Offline));
        if (damage == "bytes") File.WriteAllBytes(Path.Combine(f.Root, "legacy"), new byte[] { 2 });
        if (damage == "missing") File.Delete(Path.Combine(f.Root, "legacy"));
        if (damage == "new-entry") f.Add("unexpected", new byte[] { 2 });
        if (damage == "new-entry") await Assert.ThrowsAsync<InvalidDataException>(() => new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline));
        else await Assert.ThrowsAsync<IOException>(() => new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline));
        await Assert.ThrowsAsync<ArtifactContentMigrationInProgressException>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
    }

    [Theory]
    [InlineData("directory")]
    [InlineData("symlink")]
    [InlineData("dangling")]
    [InlineData("backslash")]
    [InlineData("control")]
    [InlineData("fifo")]
    public async Task InvalidFlatLayoutIsRejectedWithoutProtocolChanges(string damage)
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1 });
        var path = Path.Combine(f.Root, "invalid");
        if (damage == "directory") Directory.CreateDirectory(path);
        if (damage is "symlink" or "dangling") File.CreateSymbolicLink(path, damage == "symlink" ? Path.Combine(f.Root, "legacy") : Path.Combine(f.Parent, "missing"));
        if (damage == "backslash") f.Add(@"bad\name", new byte[] { 2 });
        if (damage == "control") f.Add("bad\tname", new byte[] { 2 });
        if (damage == "fifo")
        {
            var info = new System.Diagnostics.ProcessStartInfo("mkfifo") { UseShellExecute = false }; info.ArgumentList.Add(path);
            using var process = System.Diagnostics.Process.Start(info)!; await process.WaitForExitAsync(); Assert.Equal(0, process.ExitCode);
        }
        var before = f.RootEntries();
        await Assert.ThrowsAnyAsync<Exception>(() => new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline));
        Assert.Equal(before, f.RootEntries()); Assert.False(Directory.Exists(f.Workspace));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(f.Root, "legacy")));
    }

    [Theory]
    [InlineData("catalog")]
    [InlineData("checkpoint")]
    [InlineData("manifest")]
    [InlineData("state")]
    public async Task DamagedCheckpointFailsClosed(string damage)
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1 });
        var interrupted = new FileSystemArtifactContentMigration { Checkpoint = point => point == "ArtifactCommitted" ? throw new IOException("synthetic interruption") : Task.CompletedTask };
        await Assert.ThrowsAsync<IOException>(() => interrupted.MigrateAsync(f.Root, Offline));
        if (damage == "catalog") File.Delete(f.CatalogPath);
        else if (damage == "manifest") File.WriteAllText(Path.Combine(f.Workspace, "manifest.json"), "broken");
        else f.Sql(damage == "checkpoint" ? "DELETE FROM ContentMigrationArtifacts" : "DELETE FROM ContentState");
        await Assert.ThrowsAnyAsync<Exception>(() => new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline));
        await Assert.ThrowsAnyAsync<Exception>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
        Assert.False(File.Exists(Path.Combine(f.Root, ".content-format")));
    }

    [Fact]
    public async Task RetainedMutationAndMissingCompletedMarkerOrCatalogFailAdmission()
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1 });
        var result = await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        File.WriteAllBytes(Path.Combine(result.RetainedDirectory, "legacy"), new byte[] { 2 });
        await Assert.ThrowsAnyAsync<Exception>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
    }

    [Theory]
    [InlineData(".content-format")]
    [InlineData(".content-catalog.sqlite")]
    public async Task MissingCompletedProtocolObjectIsDamageNotFreshBootstrap(string name)
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1 });
        await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        File.Delete(Path.Combine(f.Root, name)); var before = f.RootEntries();
        await Assert.ThrowsAnyAsync<Exception>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
        Assert.Equal(before, f.RootEntries());
    }

    [Fact]
    public async Task EmptyLegacyRootCanCompleteWithoutInventingArtifacts()
    {
        using var f = new Fixture();
        var result = await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        Assert.Equal(0, result.ArtifactCount);
        Assert.Empty(Directory.GetFileSystemEntries(result.RetainedDirectory));
        Assert.Null(await new FileSystemArtifactContentStore(f.Root).ReadAsync(new("absent")));
    }

    [Fact]
    public async Task MissingStagedGateNeverReinterpretsReservedLegacyArtifact()
    {
        using var f = new Fixture(); f.Add(".content-coordination", new byte[] { 4, 5, 6 });
        var interrupted = new FileSystemArtifactContentMigration { Checkpoint = point => point == "InventoryPersisted" ? throw new IOException("synthetic interruption") : Task.CompletedTask };
        await Assert.ThrowsAsync<IOException>(() => interrupted.MigrateAsync(f.Root, Offline));
        File.Delete(Path.Combine(f.Workspace, "versioned", ".content-coordination"));
        var path = Path.Combine(f.Root, ".content-coordination");
        var identity = ContentStoragePlatform.Select().InspectSourceFile(path);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline));
        Assert.Equal(identity, ContentStoragePlatform.Select().InspectSourceFile(path));
        Assert.Equal(new byte[] { 4, 5, 6 }, File.ReadAllBytes(path));
        Assert.Equal(new[] { ".content-coordination" }, f.RootEntries());
    }

    [Fact]
    public async Task ReintroducedOriginalSourceDuringCutoverFailsClosed()
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1 });
        var interrupted = new FileSystemArtifactContentMigration { Checkpoint = point => point == "LegacyRelocated" ? throw new IOException("synthetic interruption") : Task.CompletedTask };
        await Assert.ThrowsAsync<IOException>(() => interrupted.MigrateAsync(f.Root, Offline));
        f.Add("legacy", new byte[] { 2 });
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline));
        await Assert.ThrowsAsync<ArtifactContentMigrationInProgressException>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
    }

    [Fact]
    public async Task IndependentMigratorsSerializeAndProcessDeathResumesOriginalInventory()
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1, 2 });
        File.WriteAllText(Path.Combine(f.Parent, "worker-allowed"), f.Root);
        var info = new System.Diagnostics.ProcessStartInfo("dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("vstest"); info.ArgumentList.Add(typeof(ArtifactContentMigrationTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ArtifactContentMigrationTests.MigrationProcessWorker");
        info.Environment["EMF_MIGRATION_CHILD_ROOT"] = f.Root;
        info.Environment["EMF_AZURE_OPENAI_LIVE"] = "false"; info.Environment["EMF_AZURE_OPENAI_LIVE_TESTS"] = "false";
        using var process = System.Diagnostics.Process.Start(info)!;
        var output = process.StandardOutput.ReadToEndAsync(); var error = process.StandardError.ReadToEndAsync();
        Task<ArtifactContentMigrationResult>? second = null;
        try
        {
            var wait = System.Diagnostics.Stopwatch.StartNew();
            while (!File.Exists(Path.Combine(f.Parent, "worker-ready")))
            {
                Assert.False(process.HasExited, await (process.HasExited ? output : Task.FromResult("Worker exited early.")));
                if (wait.Elapsed > TimeSpan.FromSeconds(15)) throw new TimeoutException("Synthetic migration worker did not start.");
                await Task.Delay(20);
            }
            var revisions = f.CheckpointRevisions();
            await Assert.ThrowsAsync<ArtifactContentMigrationInProgressException>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
            second = Task.Run(() => new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline));
            await Task.Delay(150); Assert.False(second.IsCompleted);
            process.Kill(entireProcessTree: true); await process.WaitForExitAsync();
            await second.WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Equal(revisions, f.CheckpointRevisions());
            Assert.Equal(new byte[] { 1, 2 }, await new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); await process.WaitForExitAsync(); }
            if (second is not null) await second;
            await output; await error;
        }
    }

    // Only the controlled child vstest process receives this synthetic root.
    [Fact]
    public async Task MigrationProcessWorker()
    {
        var root = Environment.GetEnvironmentVariable("EMF_MIGRATION_CHILD_ROOT");
        if (root is null) return;
        var parent = Path.GetDirectoryName(root)!;
        Assert.StartsWith(Path.Combine(Path.GetTempPath(), "emf-migration-test-"), parent);
        Assert.Equal(root, File.ReadAllText(Path.Combine(parent, "worker-allowed")));
        var migration = new FileSystemArtifactContentMigration
        {
            Checkpoint = async point =>
            {
                if (point == "InventoryPersisted")
                {
                    File.WriteAllText(Path.Combine(parent, "worker-ready"), "ready");
                    await Task.Delay(TimeSpan.FromSeconds(30));
                }
            }
        };
        await migration.MigrateAsync(root, Offline);
    }

    [Theory]
    [InlineData("origin-missing")]
    [InlineData("origin-revision")]
    [InlineData("origin-digest")]
    [InlineData("trusted-owner")]
    public async Task CompletedMigrationRejectsDamagedBootstrapLineage(string damage)
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1, 2, 3 });
        await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        f.Sql(damage switch
        {
            "origin-missing" => "DELETE FROM ContentMigrationArtifacts",
            "origin-revision" => "UPDATE ContentMigrationArtifacts SET Revision='another-valid-revision'",
            "origin-digest" => "UPDATE ContentMigrationArtifacts SET Digest='" + new string('0', 64) + "'",
            _ => "UPDATE ContentState SET Owner='invented-trusted-owner'"
        });
        await Assert.ThrowsAnyAsync<Exception>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
        Assert.Equal(0L, f.Scalar("SELECT COUNT(*) FROM ContentReceipts"));
    }

    [Theory]
    [InlineData("artifact-count")]
    [InlineData("per-artifact-bytes")]
    [InlineData("total-bytes")]
    public async Task InventoryBudgetsRejectBeforePublishingCheckpoint(string budget)
    {
        using var f = new Fixture(); f.Add("one", new byte[] { 1, 2 }); f.Add("two", new byte[] { 3, 4 });
        var before = f.RootEntries();
        var options = budget switch
        {
            "artifact-count" => Offline with { MaxArtifacts = 1 },
            "per-artifact-bytes" => Offline with { MaxStoredBytes = 1 },
            _ => Offline with { MaxTotalBytes = 3 }
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSystemArtifactContentMigration().MigrateAsync(f.Root, options));
        Assert.Equal(before, f.RootEntries()); Assert.False(Directory.Exists(f.Workspace));
        Assert.Equal(new byte[] { 1, 2 }, File.ReadAllBytes(Path.Combine(f.Root, "one")));
        Assert.Equal(new byte[] { 3, 4 }, File.ReadAllBytes(Path.Combine(f.Root, "two")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompletedWorkspacePreventsDeletedOrEmptyRootBootstrap(bool recreateEmpty)
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1 });
        await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        var workspaceFiles = Directory.GetFiles(f.Workspace, "*", SearchOption.AllDirectories)
            .ToDictionary(path => Path.GetRelativePath(f.Workspace, path), File.ReadAllBytes);
        var workspaceEntries = Directory.GetFileSystemEntries(f.Workspace, "*", SearchOption.AllDirectories).Order().ToArray();
        Directory.Delete(f.Root, true);
        if (recreateEmpty) ContentStoragePlatform.Select().CreatePrivateDirectory(f.Root);
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
        Assert.Equal(recreateEmpty, Directory.Exists(f.Root));
        if (recreateEmpty) Assert.Empty(Directory.GetFileSystemEntries(f.Root));
        Assert.Equal(workspaceEntries, Directory.GetFileSystemEntries(f.Workspace, "*", SearchOption.AllDirectories).Order().ToArray());
        foreach (var (relative, bytes) in workspaceFiles) Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(f.Workspace, relative)));
    }

    [Theory]
    [InlineData("missing-predecessor")]
    [InlineData("unrelated-created")]
    [InlineData("cycle")]
    [InlineData("branch")]
    public async Task MigratedCurrentStateRequiresOneContinuousCompleteLineage(string damage)
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1 });
        await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        var store = new FileSystemArtifactContentStore(f.Root);
        var imported = await store.ReadVersionedAsync(new("legacy"));
        var first = await store.ReplaceIfRevisionMatchesAsync(new("legacy"), imported!.Revision, new byte[] { 2 }, new(ArtifactContentOperationId.New()));
        if (damage == "unrelated-created")
            f.Sql("UPDATE ContentReceipts SET Receipt=json_set(Receipt,'$.PriorRevision',NULL,'$.Kind',0,'$.Outcome',0)");
        else if (damage == "missing-predecessor")
            f.Sql("UPDATE ContentReceipts SET Receipt=json_set(Receipt,'$.PriorRevision.Value','missing-valid-revision')");
        else
        {
            var second = await store.ReplaceIfRevisionMatchesAsync(new("legacy"), first.CurrentRevision!.Value, new byte[] { 3 }, new(ArtifactContentOperationId.New()));
            if (damage == "cycle")
                f.Sql("UPDATE ContentReceipts SET Receipt=json_set(Receipt,'$.PriorRevision.Value','" + second.CurrentRevision!.Value.Value + "') WHERE OperationId='" + first.Receipt.OperationId.Value + "'");
            else
                f.Sql("UPDATE ContentReceipts SET Receipt=json_set(Receipt,'$.PriorRevision.Value','" + imported.Revision.Value + "') WHERE OperationId='" + second.Receipt.OperationId.Value + "'");
        }
        await Assert.ThrowsAsync<InvalidDataException>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
    }

    [Fact]
    public async Task MigrationOriginAndMutationRevisionCannotCollideAcrossTables()
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1 });
        await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        var origin = (string)f.Scalar("SELECT Revision FROM ContentMigrationArtifacts")!;
        using (var connection = f.Connection())
        using (var transaction = connection.BeginTransaction())
        using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = "UPDATE ContentState SET Revision=$revision; INSERT INTO ContentReceipts(OperationId,Request,Receipt,MutationRevision) VALUES('collision', 'request', '{}', $revision)";
            command.Parameters.AddWithValue("$revision", origin);
            Assert.Throws<SqliteException>(() => command.ExecuteNonQuery());
            transaction.Rollback();
        }
        Assert.Equal(0L, f.Scalar("SELECT COUNT(*) FROM ContentReceipts"));
        var store = new FileSystemArtifactContentStore(f.Root);
        var first = await store.ReplaceIfRevisionMatchesAsync(new("legacy"), new(origin), new byte[] { 2 }, new(ArtifactContentOperationId.New()));
        Assert.Throws<SqliteException>(() => f.Sql("UPDATE ContentMigrationArtifacts SET Revision='" + first.CurrentRevision!.Value.Value + "'"));
        Assert.Equal(new byte[] { 2 }, await new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
    }

    [Fact]
    public async Task FullMigrationEvidenceScanIsOncePerSuccessfulStoreAdmission()
    {
        using var f = new Fixture();
        foreach (var name in new[] { "one", "two", "three" }) f.Add(name, new byte[] { 1, 2, 3 });
        var result = await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        var platform = new CountingSourcePlatform();
        var store = new FileSystemArtifactContentStore(f.Root, FileSystemArtifactContentStore.DefaultMaxStoredBytes, platform);
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => store.ReadAsync(new("one")))));
        Assert.Equal(6, platform.SourceReads);
        await store.ReadAsync(new("two"));
        var prior = await store.ReadVersionedAsync(new("one"));
        await store.ReplaceIfRevisionMatchesAsync(new("one"), prior!.Revision, new byte[] { 4 }, new(ArtifactContentOperationId.New()));
        Assert.Equal(6, platform.SourceReads);
        await new FileSystemArtifactContentStore(f.Root, FileSystemArtifactContentStore.DefaultMaxStoredBytes, platform).ReadAsync(new("one"));
        Assert.Equal(12, platform.SourceReads);
        File.WriteAllBytes(Path.Combine(result.RetainedDirectory, "two"), new byte[] { 9, 9, 9 });
        // Existing admission does not promise ongoing retained-evidence reconciliation.
        Assert.Equal(new byte[] { 4 }, await store.ReadAsync(new("one")));
        Assert.Equal(12, platform.SourceReads);
        await Assert.ThrowsAsync<IOException>(() => new FileSystemArtifactContentStore(f.Root).ReadAsync(new("one")));
    }

    [Fact]
    public async Task AdmittedStoreRejectsChangedImmutableProvenanceWithoutPayloadRescan()
    {
        using var f = new Fixture(); f.Add("legacy", new byte[] { 1 });
        await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, Offline);
        var platform = new CountingSourcePlatform();
        var store = new FileSystemArtifactContentStore(f.Root, FileSystemArtifactContentStore.DefaultMaxStoredBytes, platform);
        await store.ReadAsync(new("legacy")); Assert.Equal(2, platform.SourceReads);
        f.Sql("UPDATE ContentMigrationArtifacts SET Digest='" + new string('0', 64) + "'");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadAsync(new("legacy")));
        Assert.Equal(2, platform.SourceReads);
    }

    private sealed class CountingSourcePlatform : IContentStoragePlatform
    {
        private readonly IContentStoragePlatform _inner = ContentStoragePlatform.Select();
        private int _sourceReads;
        public int SourceReads => Volatile.Read(ref _sourceReads);
        public void RequirePlatform() => _inner.RequirePlatform();
        public void RequireSameFileSystem(string rootPath, string stagingParentPath) => _inner.RequireSameFileSystem(rootPath, stagingParentPath);
        public ContentSourceIdentity InspectSourceFile(string path) => _inner.InspectSourceFile(path);
        public FileStream OpenSourceFile(string path) { Interlocked.Increment(ref _sourceReads); return _inner.OpenSourceFile(path); }
        public Task<IDisposable> AcquireAdmissionAsync(string path, CancellationToken ct) => _inner.AcquireAdmissionAsync(path, ct);
        public Task<IDisposable> AcquireAsync(string path, bool exclusive, CancellationToken ct) => _inner.AcquireAsync(path, exclusive, ct);
        public void FlushDirectory(string path, bool verifyFileSystem = false) => _inner.FlushDirectory(path, verifyFileSystem);
        public void CreatePrivateDirectory(string path) => _inner.CreatePrivateDirectory(path);
        public FileStream CreatePrivateFile(string path, bool asynchronous = false) => _inner.CreatePrivateFile(path, asynchronous);
        public void ValidatePrivatePermissions(string path) => _inner.ValidatePrivatePermissions(path);
    }

    private sealed class Fixture : IDisposable
    {
        public string Parent { get; } = Path.Combine(Path.GetTempPath(), "emf-migration-test-" + Guid.NewGuid());
        public string Root => Path.Combine(Parent, "content");
        public string Workspace => FileSystemArtifactContentMigration.WorkspaceFor(Root);
        public string CatalogPath => File.Exists(Path.Combine(Workspace, "versioned", ".content-catalog.sqlite"))
            ? Path.Combine(Workspace, "versioned", ".content-catalog.sqlite") : Path.Combine(Root, ".content-catalog.sqlite");
        public Fixture() { ContentStoragePlatform.Select().CreatePrivateDirectory(Parent); ContentStoragePlatform.Select().CreatePrivateDirectory(Root); }
        public void Add(string name, byte[] bytes) => File.WriteAllBytes(Path.Combine(Root, name), bytes);
        public string[] RootEntries() => Directory.GetFileSystemEntries(Root).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal).ToArray()!;
        public SqliteConnection Connection()
        {
            var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = CatalogPath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString()); connection.Open(); return connection;
        }
        public object? Scalar(string sql) { using var c = Connection(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; return cmd.ExecuteScalar(); }
        public void Sql(string sql) { using var c = Connection(); using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        public string[] CheckpointRevisions()
        {
            using var c = Connection(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT ArtifactId || ':' || Revision FROM ContentMigrationArtifacts ORDER BY ArtifactId";
            using var reader = cmd.ExecuteReader(); var values = new List<string>(); while (reader.Read()) values.Add(reader.GetString(0)); return values.ToArray();
        }
        public void Dispose() { if (Directory.Exists(Parent)) Directory.Delete(Parent, true); }
    }
}

using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;

namespace EMF.Tests;

public sealed class VersionedArtifactContentStoreTests
{
    [Fact]
    public async Task RevisionsPreventBothFormsOfAbaAndReplaySurvivesLaterMutation()
    {
        var root = Path.Combine(Path.GetTempPath(), "emf-versioned-" + Guid.NewGuid());
        try
        {
            IVersionedArtifactContentStore store = new FileSystemArtifactContentStore(root);
            var id = new ArtifactId("synthetic");
            var operation = new ArtifactContentMutationContext(ArtifactContentOperationId.New());
            var a = await store.CreateIfAbsentAsync(id, new byte[] { 1 }, operation);
            var b = await store.ReplaceIfRevisionMatchesAsync(id, a.CurrentRevision!.Value, new byte[] { 2 }, Context());
            var c = await store.ReplaceIfRevisionMatchesAsync(id, b.CurrentRevision!.Value, new byte[] { 1 }, Context());
            Assert.NotEqual(a.CurrentRevision, c.CurrentRevision);
            Assert.Equal(a.Receipt, (await store.CreateIfAbsentAsync(id, new byte[] { 1 }, operation)).Receipt);
            var conflict = await store.ReplaceIfRevisionMatchesAsync(id, a.CurrentRevision.Value, new byte[] { 3 }, Context());
            Assert.Equal(ArtifactContentMutationOutcome.VersionConflict, conflict.Outcome);
            var deleted = await store.DeleteIfRevisionMatchesAsync(id, c.CurrentRevision!.Value, Context());
            Assert.NotEqual(c.CurrentRevision, deleted.CurrentRevision);
            Assert.Null(await store.ReadVersionedAsync(id));
            var recreated = await store.CreateIfAbsentAsync(id, new byte[] { 1 }, Context());
            Assert.NotEqual(a.CurrentRevision, recreated.CurrentRevision);
            Assert.NotEqual(deleted.CurrentRevision, recreated.CurrentRevision);
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }
    [Fact]
    public async Task ConditionalOutcomesAreDurableAndNeverOverwriteOrRecreate()
    {
        using var f = new Fixture();
        Assert.Null(await f.Store.ReadVersionedAsync(f.Id));
        var missingContext = Context();
        var missing = await f.Store.ReplaceIfRevisionMatchesAsync(f.Id, new("nonexistent"), new byte[] { 2 }, missingContext);
        Assert.Equal(ArtifactContentMutationOutcome.Missing, missing.Outcome);
        var created = await f.Create();
        Assert.Equal(missing.Receipt, (await f.Store.ReplaceIfRevisionMatchesAsync(f.Id, new("nonexistent"), new byte[] { 2 }, missingContext)).Receipt);
        var existsContext = Context();
        var exists = await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 9 }, existsContext);
        Assert.Equal(ArtifactContentMutationOutcome.AlreadyExists, exists.Outcome);
        var conflictContext = Context();
        var conflict = await f.Store.DeleteIfRevisionMatchesAsync(f.Id, new("stale"), conflictContext);
        Assert.Equal(ArtifactContentMutationOutcome.VersionConflict, conflict.Outcome);
        Assert.Equal(conflict.Receipt, (await f.Store.DeleteIfRevisionMatchesAsync(f.Id, new("stale"), conflictContext)).Receipt);
        Assert.Equal(new byte[] { 1 }, (await f.Store.ReadVersionedAsync(f.Id))!.Content);
        await f.Store.DeleteIfRevisionMatchesAsync(f.Id, created.CurrentRevision!.Value, Context());
        Assert.Equal(exists.Receipt, (await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 9 }, existsContext)).Receipt);
        var deleteContext = Context();
        var absent = await f.Store.DeleteIfRevisionMatchesAsync(f.Id, new("stale"), deleteContext);
        Assert.Equal(ArtifactContentMutationOutcome.Missing, absent.Outcome);
        Assert.Equal(absent.Receipt, (await f.Store.DeleteIfRevisionMatchesAsync(f.Id, new("stale"), deleteContext)).Receipt);
    }

    [Theory]
    [InlineData("artifact")]
    [InlineData("payload")]
    [InlineData("kind")]
    [InlineData("revision")]
    [InlineData("owner")]
    [InlineData("audit")]
    public async Task OperationIdentityCannotBeReusedForChangedRequest(string change)
    {
        using var f = new Fixture();
        var context = Context();
        var created = await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, context);
        Task conflict = change switch
        {
            "artifact" => f.Store.CreateIfAbsentAsync(new("other"), new byte[] { 1 }, context),
            "payload" => f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 2 }, context),
            "kind" => f.Store.DeleteIfRevisionMatchesAsync(f.Id, created.CurrentRevision!.Value, context),
            "revision" => f.Store.ReplaceIfRevisionMatchesAsync(f.Id, new("different"), new byte[] { 1 }, context),
            "owner" => f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, context with { OwnershipToken = new("other-owner") }),
            _ => f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, context with { AuditEventId = new("event") })
        };
        await Assert.ThrowsAsync<ArtifactContentIdempotencyException>(() => conflict);
        Assert.Equal(created.CurrentRevision, (await f.Store.ReadVersionedAsync(f.Id))!.Revision);
    }

    [Fact]
    public async Task ChangedExpectedRevisionUnderSameOperationConflicts()
    {
        using var f = new Fixture();
        var created = await f.Create(); var context = Context();
        await f.Store.ReplaceIfRevisionMatchesAsync(f.Id, created.CurrentRevision!.Value, new byte[] { 2 }, context);
        await Assert.ThrowsAsync<ArtifactContentIdempotencyException>(() => f.Store.ReplaceIfRevisionMatchesAsync(f.Id, new("different"), new byte[] { 2 }, context));
    }

    [Fact]
    public async Task LegacyMethodsUseCatalogAndMaintainTombstoneLineage()
    {
        using var f = new Fixture();
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        var a = await f.Store.ReadVersionedAsync(f.Id);
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        var b = await f.Store.ReadVersionedAsync(f.Id);
        Assert.NotEqual(a!.Revision, b!.Revision);
        await f.Store.DeleteAsync(f.Id);
        Assert.Null(await f.Store.ReadVersionedAsync(f.Id));
        Assert.False(File.Exists(Path.Combine(f.Root, f.Id.Value)));
        Assert.Equal(2, Directory.GetFiles(Path.Combine(f.Root, ".content-generations")).Length);
        await f.Store.WriteAsync(f.Id, new byte[] { 1 });
        Assert.NotEqual(a.Revision, (await f.Store.ReadVersionedAsync(f.Id))!.Revision);
        using var conn = Catalog(f.Root);
        using var cmd = conn.CreateCommand(); cmd.CommandText = "SELECT COUNT(*) FROM ContentReceipts";
        Assert.Equal(4L, cmd.ExecuteScalar());
    }

    [Fact]
    public async Task IndependentConnectionsRaceCreateAndExactRevisionReplacement()
    {
        using var f = new Fixture(); await f.Store.ReadVersionedAsync(f.Id);
        var other = new FileSystemArtifactContentStore(f.Root);
        var results = await Task.WhenAll(Task.Run(() => f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, Context())),
            Task.Run(() => other.CreateIfAbsentAsync(f.Id, new byte[] { 2 }, Context())));
        Assert.Single(results.Where(x => x.Outcome == ArtifactContentMutationOutcome.Created));
        Assert.Single(results.Where(x => x.Outcome == ArtifactContentMutationOutcome.AlreadyExists));
        var revision = (await f.Store.ReadVersionedAsync(f.Id))!.Revision;
        results = await Task.WhenAll(Task.Run(() => f.Store.ReplaceIfRevisionMatchesAsync(f.Id, revision, new byte[] { 3 }, Context())),
            Task.Run(() => other.ReplaceIfRevisionMatchesAsync(f.Id, revision, new byte[] { 4 }, Context())));
        Assert.Single(results.Where(x => x.Outcome == ArtifactContentMutationOutcome.Replaced));
        Assert.Single(results.Where(x => x.Outcome == ArtifactContentMutationOutcome.VersionConflict));
    }

    [Theory]
    [InlineData("BeforeGenerationRename")]
    [InlineData("GenerationPublished")]
    [InlineData("CandidateDurable")]
    [InlineData("BeforeCommit")]
    public async Task PrecommitInterruptionPreservesPriorContentAndUnreferencedGenerationIsNotCurrent(string point)
    {
        using var f = new Fixture(); var original = await f.Create(); var context = Context();
        f.Store.Checkpoint = p => p == point ? throw new IOException("synthetic interruption") : Task.CompletedTask;
        await Assert.ThrowsAsync<IOException>(() => f.Store.ReplaceIfRevisionMatchesAsync(f.Id, original.CurrentRevision!.Value, new byte[] { 9 }, context));
        f.Store.Checkpoint = null;
        Assert.Equal(original.CurrentRevision, (await f.Store.ReadVersionedAsync(f.Id))!.Revision);
        var reopened = new FileSystemArtifactContentStore(f.Root);
        Assert.Null(await reopened.GetMutationOutcomeAsync(context.OperationId));
        Assert.Equal(original.CurrentRevision, (await reopened.ReadVersionedAsync(f.Id))!.Revision);
        Assert.Equal(new byte[] { 1 }, (await reopened.ReadVersionedAsync(f.Id))!.Content);
        Assert.True(Directory.GetFiles(Path.Combine(f.Root, ".content-generations")).Length > 1);
    }

    [Fact]
    public async Task CommittedResponseLossRecoversOriginalReceiptAfterReopen()
    {
        using var f = new Fixture(); var context = Context();
        f.Store.Checkpoint = p => p == "Committed" ? throw new IOException("synthetic response loss") : Task.CompletedTask;
        await Assert.ThrowsAsync<IOException>(() => f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, context));
        var reopened = new FileSystemArtifactContentStore(f.Root);
        var receipt = await reopened.GetMutationOutcomeAsync(context.OperationId);
        Assert.NotNull(receipt);
        Assert.Equal(receipt, (await reopened.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, context)).Receipt);
    }

    [Fact]
    public async Task InterruptedDeleteDoesNotUnlinkAuthoritativeGeneration()
    {
        using var f = new Fixture(); var original = await f.Create();
        f.Store.Checkpoint = p => p == "BeforeCommit" ? throw new IOException("synthetic delete interruption") : Task.CompletedTask;
        await Assert.ThrowsAsync<IOException>(() => f.Store.DeleteIfRevisionMatchesAsync(f.Id, original.CurrentRevision!.Value, Context()));
        f.Store.Checkpoint = null;
        Assert.Equal(original.CurrentRevision, (await f.Store.ReadVersionedAsync(f.Id))!.Revision);
    }

    [Theory]
    [InlineData("owner", ArtifactContentMutationOutcome.Deleted)]
    [InlineData("wrong-owner", ArtifactContentMutationOutcome.VersionConflict)]
    [InlineData(null, ArtifactContentMutationOutcome.VersionConflict)]
    public async Task OwnedConditionalCleanupRequiresRevisionAndMatchingOwner(string? owner, ArtifactContentMutationOutcome expected)
    {
        using var f = new Fixture();
        var created = await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 8 }, Context() with { OwnershipToken = new("owner") });
        var context = Context() with { OwnershipToken = owner is null ? null : new ArtifactContentOwnershipToken(owner) };
        var result = await f.Store.DeleteIfRevisionMatchesAsync(f.Id, created.CurrentRevision!.Value, context);
        Assert.Equal(expected, result.Outcome);
        Assert.Equal(result.Receipt, await new FileSystemArtifactContentStore(f.Root).GetMutationOutcomeAsync(context.OperationId));
        if (expected == ArtifactContentMutationOutcome.Deleted) Assert.Null(await f.Store.ReadAsync(f.Id));
        else
        {
            var snapshot = await f.Store.ReadVersionedAsync(f.Id);
            Assert.Equal(created.CurrentRevision, snapshot!.Revision);
            Assert.Equal(new byte[] { 8 }, snapshot.Content);
        }
    }

    [Fact]
    public async Task ForwardCursorIncludesLaterCommitWithEarlierLexicalOperationIdentity()
    {
        using var f = new Fixture();
        var firstContext = new ArtifactContentMutationContext(new("zz-first"), AuditEventId: new("audit-first"));
        var first = await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, firstContext);
        var page1 = Assert.Single(await f.Store.ReadAuditObligationsAsync(null, 1));
        Assert.Equal(first.Receipt, page1.Receipt);
        var secondContext = new ArtifactContentMutationContext(new("aa-later"), AuditEventId: new("audit-later"));
        var other = new FileSystemArtifactContentStore(f.Root);
        var second = await other.ReplaceIfRevisionMatchesAsync(f.Id, first.CurrentRevision!.Value, new byte[] { 2 }, secondContext);
        Assert.True(string.CompareOrdinal(secondContext.OperationId.Value, firstContext.OperationId.Value) < 0);
        var page2 = Assert.Single(await new FileSystemArtifactContentStore(f.Root).ReadAuditObligationsAsync(page1.Cursor, 1));
        Assert.Equal(second.Receipt, page2.Receipt);
        Assert.True(page2.Cursor.Value > page1.Cursor.Value);
        Assert.Equal(second.Receipt, (await other.ReplaceIfRevisionMatchesAsync(f.Id, first.CurrentRevision.Value, new byte[] { 2 }, secondContext)).Receipt);
        Assert.Empty(await other.ReadAuditObligationsAsync(page2.Cursor, 1));
        Assert.Equal(page2, Assert.Single(await other.ReadAuditObligationsAsync(page1.Cursor, 1)));
    }

    [Fact]
    public async Task ReceiptCursorBoundsAreIndependentOfOtherIdentities()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArtifactContentReceiptCursor(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new ArtifactContentReceiptCursor(-1));
        using var f = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.ReadAuditObligationsAsync(default(ArtifactContentReceiptCursor), 1));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => f.Store.ReadAuditObligationsAsync(null, 1001));
        Assert.False(Directory.Exists(f.Root));
    }

    [Fact]
    public async Task OwnershipAndAuditObligationAreBoundedAndPersistedIndependently()
    {
        using var f = new Fixture();
        var context = new ArtifactContentMutationContext(ArtifactContentOperationId.New(), new("owner"), new("audit-event"));
        var created = await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, context);
        Assert.Equal(context.OwnershipToken, created.Receipt.OwnershipToken);
        var denied = await f.Store.DeleteIfRevisionMatchesAsync(f.Id, created.CurrentRevision!.Value,
            new(ArtifactContentOperationId.New(), new("wrong-owner")));
        Assert.Equal(ArtifactContentMutationOutcome.VersionConflict, denied.Outcome);
        var obligation = Assert.Single(await f.Store.ReadAuditObligationsAsync(null, 10));
        Assert.Equal(created.Receipt, obligation.Receipt);
        Assert.Empty(await f.Store.ReadAuditObligationsAsync(obligation.Cursor, 10));
    }

    [Fact]
    public async Task ActiveReaderReturnsItsExactGenerationAndRevision()
    {
        using var f = new Fixture(); var original = await f.Create();
        var selected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.Checkpoint = async p => { if (p == "ReaderSelected") { selected.TrySetResult(); await release.Task; } };
        var read = f.Store.ReadVersionedAsync(f.Id); await selected.Task.WaitAsync(TimeSpan.FromSeconds(5));
        var other = new FileSystemArtifactContentStore(f.Root);
        var staged = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        other.Checkpoint = p => { if (p == "CandidateDurable") staged.TrySetResult(); return Task.CompletedTask; };
        var write = Task.Run(() => other.WriteAsync(f.Id, new byte[] { 2 }));
        try { await staged.Task.WaitAsync(TimeSpan.FromSeconds(5)); }
        finally { release.TrySetResult(); }
        var snapshot = await read; await write;
        Assert.Equal(original.CurrentRevision, snapshot!.Revision); Assert.Equal(new byte[] { 1 }, snapshot.Content);
        f.Store.Checkpoint = null;
        Assert.Equal(new byte[] { 2 }, (await f.Store.ReadVersionedAsync(f.Id))!.Content);
    }

    [Theory]
    [InlineData("marker")]
    [InlineData("catalog")]
    [InlineData("generation")]
    [InlineData("schema")]
    [InlineData("journal")]
    public async Task DamagedOrUnsupportedStoreFailsClosed(string damage)
    {
        using var f = new Fixture(); await f.Create();
        if (damage == "marker") File.Delete(Path.Combine(f.Root, ".content-format"));
        else if (damage == "catalog") File.Delete(Path.Combine(f.Root, ".content-catalog.sqlite"));
        else if (damage == "generation") File.Delete(Directory.GetFiles(Path.Combine(f.Root, ".content-generations")).Single());
        else
        {
            using var conn = Catalog(f.Root); using var cmd = conn.CreateCommand();
            cmd.CommandText = damage == "schema" ? "PRAGMA user_version=999" : "PRAGMA journal_mode=WAL"; cmd.ExecuteNonQuery();
        }
        await Assert.ThrowsAnyAsync<Exception>(() => new FileSystemArtifactContentStore(f.Root).ReadVersionedAsync(f.Id));
    }

    private static string[] EntrySnapshot(string root)
        => Directory.EnumerateFileSystemEntries(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path)).OrderBy(path => path, StringComparer.Ordinal).ToArray();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LegacyRootRequiresExplicitMigrationWithoutChangingItsBytes(bool multipleEntries)
    {
        using var f = new Fixture(); Directory.CreateDirectory(f.Root);
        var legacy = Path.Combine(f.Root, f.Id.Value); await File.WriteAllBytesAsync(legacy, new byte[] { 1 });
        if (multipleEntries)
        {
            Directory.CreateDirectory(Path.Combine(f.Root, "nested"));
            await File.WriteAllBytesAsync(Path.Combine(f.Root, "nested", "second"), new byte[] { 2 });
        }
        var before = EntrySnapshot(f.Root);
        var allFiles = Directory.GetFiles(f.Root, "*", SearchOption.AllDirectories);
        var bytesBefore = allFiles.ToDictionary(path => path, File.ReadAllBytes);
        var paths = Directory.GetFileSystemEntries(f.Root, "*", SearchOption.AllDirectories).Prepend(f.Root).ToArray();
        var metadataBefore = paths.ToDictionary(path => path, path => (File.GetLastWriteTimeUtc(path), File.GetAttributes(path)));
        await Assert.ThrowsAsync<ArtifactContentMigrationRequiredException>(() => f.Store.ReadAsync(f.Id));
        Assert.Equal(before, EntrySnapshot(f.Root));
        foreach (var (path, bytes) in bytesBefore) Assert.Equal(bytes, File.ReadAllBytes(path));
        foreach (var (path, metadata) in metadataBefore)
            Assert.Equal(metadata, (File.GetLastWriteTimeUtc(path), File.GetAttributes(path)));
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(legacy));
        foreach (var name in new[] { ".content-coordination", ".content-format", ".content-catalog.sqlite", ".content-generations" })
            Assert.DoesNotContain(name, EntrySnapshot(f.Root));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreexistingGateOnlyLegacyRootRemainsCompletelyUnchanged(bool appearsAfterPreflight)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture(); Directory.CreateDirectory(f.Root);
        var gate = Path.Combine(f.Root, ".content-coordination");
        var payload = new byte[] { 6, 7, 8, 9 };
        void CreateLegacyGate()
        {
            File.WriteAllBytes(gate, payload);
            File.SetUnixFileMode(gate, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead);
        }
        if (!appearsAfterPreflight) CreateLegacyGate();
        string[]? entries = null;
        (DateTime Access, DateTime Write, DateTime Created, UnixFileMode Mode)? gateMetadata = null;
        (DateTime Write, UnixFileMode Mode)? rootMetadata = null;
        void Snapshot()
        {
            entries = EntrySnapshot(f.Root);
            gateMetadata = (File.GetLastAccessTimeUtc(gate), File.GetLastWriteTimeUtc(gate), File.GetCreationTimeUtc(gate), File.GetUnixFileMode(gate));
            rootMetadata = (Directory.GetLastWriteTimeUtc(f.Root), File.GetUnixFileMode(f.Root));
        }
        if (appearsAfterPreflight)
            f.Store.Checkpoint = point =>
            {
                if (point == "BeforeAdmissionCoordination") { CreateLegacyGate(); Snapshot(); }
                return Task.CompletedTask;
            };
        else Snapshot();
        await Assert.ThrowsAsync<ArtifactContentMigrationRequiredException>(() => f.Store.ReadAsync(f.Id));
        Assert.Equal(entries, EntrySnapshot(f.Root));
        // Compare metadata before reading the payload, which can update atime.
        Assert.Equal(gateMetadata!.Value, (File.GetLastAccessTimeUtc(gate), File.GetLastWriteTimeUtc(gate), File.GetCreationTimeUtc(gate), File.GetUnixFileMode(gate)));
        Assert.Equal(rootMetadata!.Value, (Directory.GetLastWriteTimeUtc(f.Root), File.GetUnixFileMode(f.Root)));
        Assert.Equal(payload, File.ReadAllBytes(gate));
        Assert.Equal(new[] { ".content-coordination" }, EntrySnapshot(f.Root));
        Assert.False(File.Exists(Path.Combine(f.Root, ".content-format")));
        Assert.False(File.Exists(Path.Combine(f.Root, ".content-catalog.sqlite")));
        Assert.False(Directory.Exists(Path.Combine(f.Root, ".content-generations")));
    }

    [Fact]
    public async Task AdmissionRechecksLegacyEntriesAfterPreflight()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture();
        f.Store.Checkpoint = point =>
        {
            if (point == "AdmissionPreflight" && OperatingSystem.IsLinux())
            {
                Directory.CreateDirectory(f.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
                File.WriteAllBytes(Path.Combine(f.Root, "late-legacy"), new byte[] { 7 });
            }
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<ArtifactContentMigrationRequiredException>(() => f.Store.ReadAsync(f.Id));
        Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(Path.Combine(f.Root, "late-legacy")));
        Assert.False(File.Exists(Path.Combine(f.Root, ".content-coordination")));
        Assert.False(File.Exists(Path.Combine(f.Root, ".content-format")));
        Assert.False(File.Exists(Path.Combine(f.Root, ".content-catalog.sqlite")));
    }

    [Theory]
    [InlineData("root")]
    [InlineData(".content-coordination")]
    [InlineData(".content-format")]
    [InlineData(".content-catalog.sqlite")]
    [InlineData(".content-generations")]
    [InlineData("generation")]
    public async Task UnsafeExistingPermissionsFailWithoutChmod(string target)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture(); await f.Create();
        var path = target == "root" ? f.Root : target == "generation"
            ? Directory.GetFiles(Path.Combine(f.Root, ".content-generations")).Single() : Path.Combine(f.Root, target);
        var unsafeMode = File.GetUnixFileMode(path) | UnixFileMode.GroupRead;
        File.SetUnixFileMode(path, unsafeMode);
        await Assert.ThrowsAsync<IOException>(() => f.Store.ReadAsync(f.Id));
        Assert.Equal(unsafeMode, File.GetUnixFileMode(path));
    }

    [Theory]
    [InlineData("-journal", false)]
    [InlineData("-wal", false)]
    [InlineData("-shm", false)]
    [InlineData("-journal", true)]
    [InlineData("-wal", true)]
    [InlineData("-shm", true)]
    public async Task UnsafeSqliteSidecarIsRejectedBeforeCatalogOpen(string suffix, bool walCatalog)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture(); await f.Create();
        if (walCatalog)
        {
            using var conn = Catalog(f.Root); using var cmd = conn.CreateCommand();
            cmd.CommandText = "PRAGMA journal_mode=WAL"; Assert.Equal("wal", cmd.ExecuteScalar());
        }
        var outside = Path.Combine(Path.GetDirectoryName(f.Root)!, "emf-sidecar-target-" + Guid.NewGuid());
        try
        {
            await File.WriteAllBytesAsync(outside, new byte[] { 1, 2, 3, 4 });
            var sidecar = Path.Combine(f.Root, ".content-catalog.sqlite" + suffix);
            File.Delete(sidecar);
            File.CreateSymbolicLink(sidecar, outside);
            var entriesBefore = EntrySnapshot(f.Root);
            var catalogBefore = File.ReadAllBytes(Path.Combine(f.Root, ".content-catalog.sqlite"));
            await Assert.ThrowsAsync<IOException>(() => f.Store.ReadAsync(f.Id));
            Assert.Equal(entriesBefore, EntrySnapshot(f.Root));
            Assert.Equal(outside, new FileInfo(sidecar).LinkTarget);
            Assert.Equal(new byte[] { 1, 2, 3, 4 }, File.ReadAllBytes(outside));
            Assert.Equal(catalogBefore, File.ReadAllBytes(Path.Combine(f.Root, ".content-catalog.sqlite")));
        }
        finally { File.Delete(outside); }
    }

    [Theory]
    [InlineData("-journal")]
    [InlineData("-wal")]
    [InlineData("-shm")]
    public async Task UnsafeSqliteSidecarModeFailsWithoutRepair(string suffix)
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture(); await f.Create();
        var sidecar = Path.Combine(f.Root, ".content-catalog.sqlite" + suffix);
        File.WriteAllBytes(sidecar, new byte[] { 9 });
        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.OtherWrite;
        File.SetUnixFileMode(sidecar, mode);
        await Assert.ThrowsAsync<IOException>(() => f.Store.ReadAsync(f.Id));
        Assert.Equal(mode, File.GetUnixFileMode(sidecar));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(sidecar));
    }

    [Fact]
    public async Task UnsafeEmptyRootFailsBeforeGateCreation()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture(); Directory.CreateDirectory(f.Root);
        File.SetUnixFileMode(f.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.OtherRead);
        await Assert.ThrowsAsync<IOException>(() => f.Store.ReadAsync(f.Id));
        Assert.Empty(EntrySnapshot(f.Root));
    }

    [Fact]
    public async Task SqliteJournalUsesPrivatePermissionsDuringTransaction()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture();
        f.Store.Checkpoint = point =>
        {
            if (point == "BeforeCommit" && OperatingSystem.IsLinux())
                Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                    File.GetUnixFileMode(Path.Combine(f.Root, ".content-catalog.sqlite-journal")));
            return Task.CompletedTask;
        };
        await f.Create();
    }

    [Fact]
    public async Task CatalogHasExplicitVersionAndRequiredJournalMode()
    {
        using var f = new Fixture(); await f.Create();
        using var conn = Catalog(f.Root); using var cmd = conn.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode"; Assert.Equal("delete", cmd.ExecuteScalar());
        cmd.CommandText = "PRAGMA user_version"; Assert.Equal(2L, cmd.ExecuteScalar());
        // synchronous is connection-local; provider verifies EXTRA on each open.
        cmd.CommandText = "SELECT Receipt FROM ContentReceipts";
        Assert.DoesNotContain("Payload", (string)cmd.ExecuteScalar()!);
    }

    [Theory]
    [InlineData("資料 café Δ")]
    [InlineData("resource with spaces")]
    public async Task CoreArtifactIdRoundTripsCreateReplaceAndDurableReceipts(string value)
    {
        var id = new ArtifactId(value);
        Assert.Throws<ArgumentException>(() => ArtifactContentIdentity.Validate(id.Value));
        using var f = new Fixture();
        var createContext = Context() with { AuditEventId = new("audit-create") };
        var created = await f.Store.CreateIfAbsentAsync(id, new byte[] { 1 }, createContext);
        Assert.Equal(id, created.Receipt.ArtifactId);
        Assert.Equal(new byte[] { 1 }, await f.Store.ReadAsync(id));
        var reopened = new FileSystemArtifactContentStore(f.Root);
        Assert.Equal(created.Receipt, await reopened.GetMutationOutcomeAsync(createContext.OperationId));
        var replaceContext = Context() with { AuditEventId = new("audit-replace") };
        var replaced = await reopened.ReplaceIfRevisionMatchesAsync(id, created.CurrentRevision!.Value, new byte[] { 2 }, replaceContext);
        var again = new FileSystemArtifactContentStore(f.Root);
        var snapshot = await again.ReadVersionedAsync(id);
        Assert.Equal(new byte[] { 2 }, snapshot!.Content);
        Assert.Equal(replaced.CurrentRevision, snapshot.Revision);
        Assert.Equal(replaced.Receipt, await again.GetMutationOutcomeAsync(replaceContext.OperationId));
        Assert.Equal(value, replaced.Receipt.ArtifactId.Value);
        var obligations = await again.ReadAuditObligationsAsync(null, 10);
        Assert.Equal(new[] { created.Receipt, replaced.Receipt }, obligations.Select(item => item.Receipt));
    }

    [Fact]
    public async Task ArtifactIdHasNoNewProtocolOrFilenameLengthCeiling()
    {
        using var f = new Fixture();
        var id = new ArtifactId(new string('界', 1024));
        var context = Context();
        var created = await f.Store.CreateIfAbsentAsync(id, new byte[] { 3 }, context);
        var reopened = new FileSystemArtifactContentStore(f.Root);
        Assert.Equal(new byte[] { 3 }, await reopened.ReadAsync(id));
        Assert.Equal(created.Receipt, await reopened.GetMutationOutcomeAsync(context.OperationId));
        Assert.Equal(id, (await reopened.GetMutationOutcomeAsync(context.OperationId))!.ArtifactId);
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("/absolute/resource")]
    [InlineData(@"C:\outside\resource")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData(".content-coordination")]
    [InlineData(".content-catalog.sqlite")]
    public async Task PathLikeArtifactIdIsCatalogDataAndNeverAStoragePath(string value)
    {
        using var f = new Fixture();
        var context = Context();
        var id = new ArtifactId(value);
        var created = await f.Store.CreateIfAbsentAsync(id, new byte[] { 4 }, context);
        var reopened = new FileSystemArtifactContentStore(f.Root);
        Assert.Equal(new byte[] { 4 }, await reopened.ReadAsync(id));
        Assert.Equal(id, (await reopened.GetMutationOutcomeAsync(context.OperationId))!.ArtifactId);
        Assert.Equal(created.Receipt, await reopened.GetMutationOutcomeAsync(context.OperationId));
        Assert.Equal(new[] { ".content-catalog.sqlite", ".content-coordination", ".content-format", ".content-generations" },
            Directory.GetFileSystemEntries(f.Root).Select(Path.GetFileName).OrderBy(name => name, StringComparer.Ordinal));
        var generation = Assert.Single(Directory.GetFiles(Path.Combine(f.Root, ".content-generations")));
        Assert.Equal(32, Path.GetFileName(generation).Length);
        Assert.All(Path.GetFileName(generation), character => Assert.True(char.IsAsciiHexDigit(character)));
        Assert.Equal(new byte[] { 4 }, File.ReadAllBytes(generation));
    }

    [Fact]
    public async Task LogicalArtifactIdNeverProbesAnExistingSymlinkPath()
    {
        if (!OperatingSystem.IsLinux()) return;
        using var f = new Fixture(); await f.Store.ReadAsync(f.Id);
        var outside = Path.Combine(Path.GetDirectoryName(f.Root)!, "emf-logical-target-" + Guid.NewGuid());
        try
        {
            File.WriteAllBytes(outside, new byte[] { 7 });
            var logicalLink = Path.Combine(f.Root, "logical-link");
            File.CreateSymbolicLink(logicalLink, outside);
            var id = new ArtifactId("logical-link");
            var created = await f.Store.CreateIfAbsentAsync(id, new byte[] { 9 }, Context());
            Assert.Equal(new byte[] { 9 }, await new FileSystemArtifactContentStore(f.Root).ReadAsync(id));
            Assert.Equal(id, created.Receipt.ArtifactId);
            Assert.Equal(outside, new FileInfo(logicalLink).LinkTarget);
            Assert.Equal(new byte[] { 7 }, File.ReadAllBytes(outside));
        }
        finally { File.Delete(outside); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("space value")]
    [InlineData("slash/value")]
    [InlineData("資料")]
    public void IdentitiesAreBoundedAndDistinct(string invalid)
    {
        Assert.Throws<ArgumentException>(() => new ArtifactContentRevision(invalid));
        Assert.Throws<ArgumentException>(() => new ArtifactContentOperationId(invalid));
        Assert.Throws<ArgumentException>(() => new ArtifactContentOwnershipToken(invalid));
        Assert.Throws<ArgumentException>(() => new ArtifactContentAuditEventId(invalid));
        var unbounded = new string('a', 129);
        Assert.Throws<ArgumentException>(() => new ArtifactContentRevision(unbounded));
        Assert.Throws<ArgumentException>(() => new ArtifactContentOperationId(unbounded));
        Assert.Throws<ArgumentException>(() => new ArtifactContentOwnershipToken(unbounded));
        Assert.Throws<ArgumentException>(() => new ArtifactContentAuditEventId(unbounded));
        Assert.NotEqual(typeof(ArtifactContentOperationId), typeof(ArtifactContentRevision));
    }

    [Fact]
    public async Task CancellationDoesNotCommitMutationOrErasePriorGeneration()
    {
        using var f = new Fixture(); var first = await f.Create();
        using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Store.ReplaceIfRevisionMatchesAsync(f.Id,
            first.CurrentRevision!.Value, new byte[] { 2 }, Context(), cancelled.Token));
        Assert.Equal(first.CurrentRevision, (await f.Store.ReadVersionedAsync(f.Id))!.Revision);
    }

    [Theory]
    [InlineData("gate")]
    [InlineData("marker")]
    [InlineData("catalog")]
    public async Task IndependentProcessCoordinationBlocksMutationAndProcessDeathReleasesLocks(string coordination)
    {
        using var f = new Fixture(); await f.Store.ReadVersionedAsync(f.Id);
        var code = coordination == "gate"
            ? "import sys,fcntl; f=open(sys.argv[1], 'r+'); fcntl.flock(f,fcntl.LOCK_EX); print('READY',flush=True); sys.stdin.readline()"
            : "import sys,sqlite3; c=sqlite3.connect(sys.argv[1]); c.execute('BEGIN EXCLUSIVE'); print('READY',flush=True); sys.stdin.readline()";
        var info = new System.Diagnostics.ProcessStartInfo("python3")
        { RedirectStandardOutput = true, RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("-u"); info.ArgumentList.Add("-c"); info.ArgumentList.Add(code);
        info.ArgumentList.Add(Path.Combine(f.Root, coordination == "gate" ? ".content-coordination" : ".content-catalog.sqlite"));
        using var process = System.Diagnostics.Process.Start(info)!;
        Task<ArtifactContentMutationResult>? mutation = null;
        try
        {
            Assert.Equal("READY", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            mutation = Task.Run(() => f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, Context()));
            await Task.Delay(150);
            Assert.False(mutation.IsCompleted);
            process.Kill(); await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ArtifactContentMutationOutcome.Created, (await mutation.WaitAsync(TimeSpan.FromSeconds(5))).Outcome);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); }
            if (mutation is not null) await mutation;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContendedPlatformCoordinationHasBoundedTimeoutAndReleasesHandle(bool admission)
    {
        using var f = new Fixture(); await f.Store.ReadAsync(f.Id);
        var path = admission ? f.Root : Path.Combine(f.Root, ".content-coordination");
        var info = new System.Diagnostics.ProcessStartInfo("python3")
        { RedirectStandardOutput = true, RedirectStandardInput = true, RedirectStandardError = true, UseShellExecute = false };
        info.ArgumentList.Add("-u"); info.ArgumentList.Add("-c");
        info.ArgumentList.Add("import sys,os,fcntl; fd=os.open(sys.argv[1],os.O_RDONLY); fcntl.flock(fd,fcntl.LOCK_EX); print('READY',flush=True); sys.stdin.readline()");
        info.ArgumentList.Add(path);
        using var process = System.Diagnostics.Process.Start(info)!;
        var platform = ContentStoragePlatform.Select();
        try
        {
            Assert.Equal("READY", await process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)));
            var elapsed = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAsync<TimeoutException>(async () =>
            {
                using var handle = await (admission ? platform.AcquireAdmissionAsync(path, default)
                    : platform.AcquireAsync(path, false, default)).WaitAsync(TimeSpan.FromSeconds(20));
            });
            Assert.InRange(elapsed.Elapsed.TotalSeconds, 9, 20);
        }
        finally { if (!process.HasExited) { process.Kill(); await process.WaitForExitAsync(); } }
        using var acquired = await (admission ? platform.AcquireAdmissionAsync(path, default)
            : platform.AcquireAsync(path, false, default)).WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void CoreContractsHaveNoProviderOrSecurityAssemblyDependencies()
    {
        var names = typeof(IVersionedArtifactContentStore).Assembly.GetReferencedAssemblies().Select(x => x.Name!);
        Assert.DoesNotContain(names, x => x.Contains("Azure") || x.Contains("Sqlite") || x.Contains("EMF.Security"));
        Assert.DoesNotContain(typeof(ArtifactContentMutationContext).GetProperties(), x =>
            x.PropertyType.Assembly.GetName().Name?.StartsWith("EMF.Security") == true);
    }

    [Fact]
    public async Task CallerMemoryCannotChangeCanonicalRequestAfterMutationStarts()
    {
        using var f = new Fixture(); await f.Store.ReadVersionedAsync(f.Id);
        byte[] input = { 1 }; var context = Context();
        var reached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Store.Checkpoint = async p => { if (p == "CandidateDurable") { reached.TrySetResult(); await release.Task; } };
        var mutation = f.Store.CreateIfAbsentAsync(f.Id, input, context);
        try { await reached.Task.WaitAsync(TimeSpan.FromSeconds(5)); input[0] = 9; }
        finally { release.TrySetResult(); }
        var result = await mutation; f.Store.Checkpoint = null;
        Assert.Equal(new byte[] { 1 }, (await f.Store.ReadVersionedAsync(f.Id))!.Content);
        Assert.Equal(result.Receipt, (await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, context)).Receipt);
    }

    [Theory]
    [InlineData("missing-state")]
    [InlineData("missing-lineage")]
    [InlineData("malformed")]
    [InlineData("contradictory")]
    [InlineData("corrupt-receipt")]
    [InlineData("wrong-generation")]
    [InlineData("coordination")]
    public async Task InvalidStoreIsRejectedBeforeAnotherMutationCanBeAccepted(string damage)
    {
        using var f = new Fixture(); await f.Create();
        if (damage == "missing-lineage") await f.Store.WriteAsync(f.Id, new byte[] { 2 });
        if (damage == "coordination") File.Delete(Path.Combine(f.Root, ".content-coordination"));
        else
        {
            using var conn = Catalog(f.Root); using var cmd = conn.CreateCommand();
            cmd.CommandText = damage switch
            {
                "missing-state" => "DELETE FROM ContentState",
                "missing-lineage" => "DELETE FROM ContentReceipts WHERE PublishedGeneration <> (SELECT Generation FROM ContentState LIMIT 1)",
                "malformed" => "DROP TABLE ContentState",
                "contradictory" => "UPDATE ContentState SET Revision='uncommitted-revision'",
                "wrong-generation" => "UPDATE ContentState SET Generation='00000000000000000000000000000000'",
                _ => "UPDATE ContentReceipts SET Receipt='not-json'"
            };
            cmd.ExecuteNonQuery();
        }
        var reopened = new FileSystemArtifactContentStore(f.Root);
        await Assert.ThrowsAnyAsync<Exception>(() => reopened.WriteAsync(new("different-artifact"), new byte[] { 2 }));
        Assert.False(File.Exists(Path.Combine(f.Root, "different-artifact")));
        if (damage == "coordination") Assert.False(File.Exists(Path.Combine(f.Root, ".content-coordination")));
    }

    [Fact]
    public async Task ReceiptMapsToExactCommittedGenerationAndNoPartialRaceState()
    {
        using var f = new Fixture(); var created = await f.Create();
        using var conn = Catalog(f.Root); using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT s.Revision,s.Generation,r.PublishedGeneration FROM ContentState s JOIN ContentReceipts r ON r.OperationId=$op WHERE s.ArtifactId=$id";
        cmd.Parameters.AddWithValue("$op", created.Receipt.OperationId.Value); cmd.Parameters.AddWithValue("$id", f.Id.Value);
        using var reader = cmd.ExecuteReader(); Assert.True(reader.Read());
        Assert.Equal(created.CurrentRevision!.Value.Value, reader.GetString(0)); Assert.Equal(reader.GetString(1), reader.GetString(2));
        Assert.Equal(new byte[] { 1 }, await File.ReadAllBytesAsync(Path.Combine(f.Root, ".content-generations", reader.GetString(1))));
    }

    [Fact]
    public async Task UnvalidatedFilesystemCannotAdvertiseDurableReceipts()
    {
        if (!Directory.Exists("/dev/shm")) return;
        var root = Path.Combine("/dev/shm", "emf-unsupported-" + Guid.NewGuid());
        try
        {
            var store = new FileSystemArtifactContentStore(root);
            await Assert.ThrowsAsync<PlatformNotSupportedException>(() => store.CreateIfAbsentAsync(new("synthetic"), new byte[] { 1 }, Context()));
            Assert.False(File.Exists(Path.Combine(root, ".content-catalog.sqlite")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Fact]
    public async Task DeclaredRequiredAuditIdentityCannotBeOmitted()
    {
        using var f = new Fixture();
        var context = new ArtifactContentMutationContext(ArtifactContentOperationId.New(), RequiresAuditObligation: true);
        await Assert.ThrowsAsync<ArgumentException>(() => f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, context));
        Assert.False(Directory.Exists(f.Root));
    }

    private static Microsoft.Data.Sqlite.SqliteConnection Catalog(string root)
    {
        var conn = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
        { DataSource = Path.Combine(root, ".content-catalog.sqlite"), Pooling = false }.ToString()); conn.Open(); return conn;
    }
    [Fact]
    public async Task ConcurrentFreshRootAdmissionRechecksCompletedBootstrap()
    {
        using var f = new Fixture();
        var second = new FileSystemArtifactContentStore(f.Root);
        f.Store.Checkpoint = async point =>
        {
            if (point == "BeforeAdmissionCoordination") await second.ReadAsync(f.Id);
        };
        Assert.Null(await f.Store.ReadAsync(f.Id));
        Assert.Equal("EMF-CONTENT-1", File.ReadAllText(Path.Combine(f.Root, ".content-format")));
        Assert.Equal(4, Directory.GetFileSystemEntries(f.Root).Length);
    }

    [Fact]
    public async Task MissingMarkerWithProtocolStateIsDamageAndDoesNotRecreateGate()
    {
        using var f = new Fixture(); await f.Create();
        File.Delete(Path.Combine(f.Root, ".content-format"));
        File.Delete(Path.Combine(f.Root, ".content-coordination"));
        var before = EntrySnapshot(f.Root);
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Store.ReadAsync(f.Id));
        Assert.Equal(before, EntrySnapshot(f.Root));
    }

    [Fact]
    public async Task RecognizedVersionedMutationsDoNotUseAdmissionCoordination()
    {
        using var f = new Fixture();
        var platform = new RecordingPlatform();
        var store = new FileSystemArtifactContentStore(f.Root, FileSystemArtifactContentStore.DefaultMaxStoredBytes, platform);
        var created = await store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, Context());
        Assert.Equal(1, platform.AdmissionAcquisitions);
        var sharedBefore = platform.SharedGateAcquisitions;
        var replaced = await store.ReplaceIfRevisionMatchesAsync(f.Id, created.CurrentRevision!.Value, new byte[] { 2 }, Context());
        await store.DeleteIfRevisionMatchesAsync(f.Id, replaced.CurrentRevision!.Value, Context());
        Assert.Equal(1, platform.AdmissionAcquisitions);
        Assert.Equal(sharedBefore + 2, platform.SharedGateAcquisitions);
    }

    // Opt-in integration requires sudo chown on synthetic temporary paths only.
    // Enable with EMF_CONTENT_FOREIGN_OWNER_TESTS=true on a controlled test host.
    [Theory]
    [InlineData("root")]
    [InlineData(".content-coordination")]
    [InlineData(".content-format")]
    [InlineData(".content-catalog.sqlite")]
    [InlineData(".content-catalog.sqlite-journal")]
    [InlineData(".content-catalog.sqlite-wal")]
    [InlineData(".content-catalog.sqlite-shm")]
    [InlineData(".content-generations")]
    [InlineData("generation")]
    public async Task ForeignOwnedProtocolPathFailsClosed(string target)
    {
        if (!OperatingSystem.IsLinux() || Environment.GetEnvironmentVariable("EMF_CONTENT_FOREIGN_OWNER_TESTS") != "true") return;
        using var f = new Fixture(); await f.Create();
        var path = target == "root" ? f.Root : target == "generation"
            ? Directory.GetFiles(Path.Combine(f.Root, ".content-generations")).Single() : Path.Combine(f.Root, target);
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            using var stream = ContentStoragePlatform.Select().CreatePrivateFile(path);
            stream.WriteByte(9);
        }
        var mode = File.GetUnixFileMode(path);
        await ChownSyntheticPath(path, "65534");
        try
        {
            await Assert.ThrowsAnyAsync<Exception>(() => f.Store.ReadAsync(f.Id));
            // Invoke the Linux admission operation directly as well: this proves
            // ownership rejection independently of an earlier directory access failure.
            var error = Assert.Throws<IOException>(() => ContentStoragePlatform.Select().ValidatePrivatePermissions(path));
            Assert.Contains("ownership", error.Message);
        }
        finally { await ChownSyntheticPath(path, Environment.UserName); }
        Assert.Equal(mode, File.GetUnixFileMode(path));
    }

    private static async Task ChownSyntheticPath(string path, string owner)
    {
        var info = new System.Diagnostics.ProcessStartInfo("sudo") { UseShellExecute = false, RedirectStandardError = true };
        foreach (var argument in new[] { "-n", "chown", owner, "--", path }) info.ArgumentList.Add(argument);
        using var process = System.Diagnostics.Process.Start(info)!;
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        Assert.True(process.ExitCode == 0, error);
    }

    private sealed class RecordingPlatform : IContentStoragePlatform
    {
        private readonly IContentStoragePlatform _inner = ContentStoragePlatform.Select();
        public int AdmissionAcquisitions { get; private set; }
        public int SharedGateAcquisitions { get; private set; }
        public void RequirePlatform() => _inner.RequirePlatform();
        public Task<IDisposable> AcquireAdmissionAsync(string rootPath, CancellationToken cancellationToken)
        { AdmissionAcquisitions++; return _inner.AcquireAdmissionAsync(rootPath, cancellationToken); }
        public Task<IDisposable> AcquireAsync(string path, bool exclusive, CancellationToken cancellationToken)
        { if (!exclusive) SharedGateAcquisitions++; return _inner.AcquireAsync(path, exclusive, cancellationToken); }
        public void FlushDirectory(string path, bool verifyFileSystem = false) => _inner.FlushDirectory(path, verifyFileSystem);
        public void CreatePrivateDirectory(string path) => _inner.CreatePrivateDirectory(path);
        public FileStream CreatePrivateFile(string path, bool asynchronous = false) => _inner.CreatePrivateFile(path, asynchronous);
        public void ValidatePrivatePermissions(string path) => _inner.ValidatePrivatePermissions(path);
    }

    [Fact]
    public async Task CommonStoreUsesInjectedPlatformAdmission()
    {
        using var f = new Fixture();
        var platform = new UnsupportedPlatform();
        var store = new FileSystemArtifactContentStore(f.Root, FileSystemArtifactContentStore.DefaultMaxStoredBytes, platform);
        await Assert.ThrowsAsync<PlatformNotSupportedException>(() => store.ReadAsync(f.Id));
        Assert.True(platform.Checked);
        Assert.False(Directory.Exists(f.Root));
    }

    private sealed class UnsupportedPlatform : IContentStoragePlatform
    {
        public bool Checked { get; private set; }
        public void RequirePlatform() { Checked = true; throw new PlatformNotSupportedException("Synthetic unsupported platform."); }
        public Task<IDisposable> AcquireAdmissionAsync(string rootPath, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<IDisposable> AcquireAsync(string path, bool exclusive, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public void FlushDirectory(string path, bool verifyFileSystem = false) => throw new InvalidOperationException();
        public void CreatePrivateDirectory(string path) => throw new InvalidOperationException();
        public FileStream CreatePrivateFile(string path, bool asynchronous = false) => throw new InvalidOperationException();
        public void ValidatePrivatePermissions(string path) => throw new InvalidOperationException();
    }

    private sealed class Fixture : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), "emf-versioned-" + Guid.NewGuid());
        public ArtifactId Id { get; } = new("synthetic");
        public FileSystemArtifactContentStore Store { get; }
        public Fixture() => Store = new(Root);
        public Task<ArtifactContentMutationResult> Create() => Store.CreateIfAbsentAsync(Id, new byte[] { 1 }, Context());
        public void Dispose() { if (Directory.Exists(Root)) Directory.Delete(Root, true); }
    }
    private static ArtifactContentMutationContext Context() => new(ArtifactContentOperationId.New());
}

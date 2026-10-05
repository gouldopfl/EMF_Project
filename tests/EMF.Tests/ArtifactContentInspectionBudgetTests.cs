using System.Runtime.Versioning;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

[SupportedOSPlatform("linux")]
public sealed class ArtifactContentInspectionBudgetTests
{
    private sealed class Fixture : IDisposable
    {
        internal string Root { get; } = Path.Combine(Path.GetTempPath(), "emf-inspection-" + Guid.NewGuid().ToString("N"));
        internal ArtifactId Id { get; } = new("synthetic-inspection");
        internal FileSystemArtifactContentStore Store { get; }
        internal Fixture(ArtifactContentInspectionLimits? limits = null) => Store = new(Root, inspectionLimits: limits);
        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, true);
            var workspace = FileSystemArtifactContentMigration.WorkspaceFor(Root);
            if (Directory.Exists(workspace)) Directory.Delete(workspace, true);
        }
    }
    private static ArtifactContentMutationContext Context() => new(ArtifactContentOperationId.New());

    [Theory]
    [InlineData("rows", 1, false)]
    [InlineData("rows", 2, true)]
    [InlineData("rows", 3, true)]
    [InlineData("entries", 1, false)]
    [InlineData("entries", 2, true)]
    [InlineData("entries", 3, true)]
    public async Task Complete_catalog_and_namespace_inspection_enforces_inclusive_limits_before_promotion(string dimension, int maximum, bool allowed)
    {
        using var f = new Fixture();
        await f.Store.ReadAsync(f.Id); // Establish protocol before measuring the inspection boundary.
        var limits = dimension == "rows" ? new ArtifactContentInspectionLimits { MaximumCatalogRows = maximum }
            : new ArtifactContentInspectionLimits { MaximumNamespaceEntries = maximum };
        var limited = new FileSystemArtifactContentStore(f.Root, inspectionLimits: limits);
        await limited.ReadAsync(f.Id); // Warm an empty admitted instance; no rows/entries yet.
        var original = await limited.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, Context());
        var operation = Context();
        var prepare = limited.PreparePhysicalCreateAsync(new("next"), new byte[] { 2 }, operation);
        if (allowed)
        {
            await using var prepared = await prepare;
            Assert.Equal(ArtifactContentMutationOutcome.Created, (await prepared.ExecuteAsync()).Outcome);
        }
        else await Assert.ThrowsAsync<InvalidDataException>(() => prepare);
        var verifier = new FileSystemArtifactContentStore(f.Root);
        Assert.Equal(original.CurrentRevision, (await verifier.ReadVersionedAsync(f.Id))!.Revision);
        Assert.Equal(new byte[] { 1 }, await verifier.ReadAsync(f.Id));
        Assert.Equal(allowed, await verifier.GetMutationOutcomeAsync(operation.OperationId) is not null);
        Assert.Equal(allowed, await verifier.ReadAsync(new("next")) is not null);
    }

    [Theory]
    [InlineData("NamespaceEntry")]
    [InlineData("CatalogRow")]
    [InlineData("LineageStep")]
    [InlineData("SqlProgress")]
    public async Task Cancellation_inside_inspection_releases_handles_and_preserves_receipts_and_other_connections(string point)
    {
        using var f = new Fixture();
        var original = await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, Context());
        using var cancellation = new CancellationTokenSource();
        f.Store.InspectionCheckpoint = reached => { if (reached == point) cancellation.Cancel(); };
        var operation = Context();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            f.Store.PreparePhysicalCreateAsync(new("cancelled"), new byte[] { 2 }, operation, cancellation.Token));
        f.Store.InspectionCheckpoint = null;
        var independent = new FileSystemArtifactContentStore(f.Root);
        Assert.Equal(original.Receipt, await independent.GetMutationOutcomeAsync(original.Receipt.OperationId));
        Assert.Null(await independent.GetMutationOutcomeAsync(operation.OperationId));
        Assert.Equal(new byte[] { 1 }, await independent.ReadAsync(f.Id));
        var next = await independent.CreateIfAbsentAsync(new("independent"), new byte[] { 3 }, Context());
        Assert.Equal(ArtifactContentMutationOutcome.Created, next.Outcome);
        await new FileSystemArtifactContentGarbageCollector(f.Root).CollectAsync();
        Assert.Equal(new byte[] { 3 }, await independent.ReadAsync(new("independent")));
    }

    [Fact]
    public async Task Deadline_covers_all_repeated_scans_and_does_not_restart_at_catalog_open()
    {
        using var f = new Fixture(); await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, Context());
        var limited = new FileSystemArtifactContentStore(f.Root, inspectionLimits: new() { MaximumDuration = TimeSpan.FromSeconds(1) });
        var delays = 0;
        limited.InspectionCheckpoint = point =>
        {
            if (point == "NamespaceEntry") { delays++; var until = LinuxContentDurability.InspectionProcessingTime() + TimeSpan.FromMilliseconds(600);
                while (LinuxContentDurability.InspectionProcessingTime() < until) { } }
        };
        await Assert.ThrowsAsync<ArtifactContentInspectionTimeoutException>(() => limited.PrepareRevisionValidationAsync(f.Id));
        Assert.InRange(delays, 1, 2);
        // One read-only admission uses several opens; the budget is shared throughout.
        Assert.Equal(new byte[] { 1 }, await f.Store.ReadAsync(f.Id));
    }

    [Theory]
    [InlineData(2, false)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    public void Aggregate_metadata_budget_counts_utf8_across_multiple_values(long maximum, bool allowed)
    {
        using var budget = new ContentInspectionBudget(new() { MaximumMetadataBytes = maximum, MaximumRowBytes = 2 }, default);
        budget.Text("é"); // Two UTF-8 bytes, not character count.
        if (allowed) budget.Text("a");
        else Assert.Throws<InvalidDataException>(() => budget.Text("a"));
    }

    [Fact]
    public async Task Database_and_row_size_limits_reject_before_returning_a_probe()
    {
        using var f = new Fixture(); await f.Store.CreateIfAbsentAsync(f.Id, new byte[] { 1 }, Context());
        var databaseBytes = new FileInfo(Path.Combine(f.Root, ".content-catalog.sqlite")).Length;
        var rejected = new FileSystemArtifactContentStore(f.Root, inspectionLimits: new() { MaximumDatabaseBytes = databaseBytes - 1 });
        await Assert.ThrowsAsync<InvalidDataException>(() => rejected.PrepareRevisionValidationAsync(f.Id));
        var accepted = new FileSystemArtifactContentStore(f.Root, inspectionLimits: new() { MaximumDatabaseBytes = databaseBytes });
        await using (var probe = await accepted.PrepareRevisionValidationAsync(f.Id)) Assert.NotNull(await probe.ReadCurrentRevisionAsync());
        var rowLimited = new FileSystemArtifactContentStore(f.Root, inspectionLimits: new() { MaximumRowBytes = 100 });
        await Assert.ThrowsAsync<InvalidDataException>(() => rowLimited.PrepareRevisionValidationAsync(f.Id));
        Assert.Equal(new byte[] { 1 }, await f.Store.ReadAsync(f.Id));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SQLite_validation_can_be_interrupted_by_work_limit_or_caller_cancellation(bool cancel)
    {
        using var connection = new SqliteConnection("Data Source=:memory:;Pooling=False"); connection.Open();
        using var cancellation = new CancellationTokenSource();
        using var budget = new ContentInspectionBudget(new() { MaximumWorkUnits = 100 }, cancellation.Token,
            point => { if (cancel && point == "SqlProgress") cancellation.Cancel(); });
        using (budget.InspectSql(connection))
        {
            using var command = connection.CreateCommand();
            command.CommandText = "WITH RECURSIVE x(n) AS (VALUES(1) UNION ALL SELECT n+1 FROM x WHERE n<1000000) SELECT sum(n) FROM x";
            Assert.Throws<SqliteException>(() => command.ExecuteScalar());
            if (cancel) Assert.ThrowsAny<OperationCanceledException>(() => budget.ThrowIfStopped());
            else Assert.Throws<InvalidDataException>(() => budget.ThrowIfStopped());
        }
        // The handler and interrupt registration were removed only from their own handle.
        using var subsequent = connection.CreateCommand(); subsequent.CommandText = "SELECT 42";
        Assert.Equal(42L, subsequent.ExecuteScalar());
    }

    [Theory]
    [InlineData(9, false)]
    [InlineData(10, true)]
    [InlineData(11, true)]
    public async Task Migrated_evidence_verification_counts_both_retained_source_and_generation(long maximum, bool allowed)
    {
        using var f = new Fixture();
        Directory.CreateDirectory(f.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(f.Root, "legacy"); await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3, 4, 5 });
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, new(true));
        var limited = new FileSystemArtifactContentStore(f.Root, inspectionLimits: new() { MaximumEvidenceBytes = maximum });
        if (allowed) Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, await limited.ReadAsync(new("legacy")));
        else await Assert.ThrowsAsync<InvalidDataException>(() => limited.ReadAsync(new("legacy")));
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5 }, await new FileSystemArtifactContentStore(f.Root).ReadAsync(new("legacy")));
    }

    [Theory]
    [InlineData("NamespaceEntry")]
    [InlineData("CatalogRow")]
    public async Task Ingestion_cancellation_during_detached_inspection_holds_no_metadata_write_transaction(string point)
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await f.Physical.ReadAsync(f.Id); // Complete bootstrap before the inspected work.
        if (point == "CatalogRow") await f.Physical.CreateIfAbsentAsync(new("other"), new byte[] { 8 }, Context());
        using var cancellation = new CancellationTokenSource();
        var reached = false;
        f.Physical.InspectionCheckpoint = inspected =>
        {
            if (inspected != point || reached) return;
            reached = true;
            f.SqlAsync("CREATE TABLE InspectionWriter(Value INTEGER); INSERT INTO InspectionWriter VALUES(1)").GetAwaiter().GetResult();
            cancellation.Cancel();
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Service().IngestAsync(f.Draft, f.Content, cancellation.Token));
        Assert.True(reached);
        f.Physical.InspectionCheckpoint = null;
        Assert.Null(await f.Physical.GetMutationOutcomeAsync(f.SecurityContext.Operation.OperationId));
        Assert.Null(await f.Repository.GetArtifactAsync(f.Id));
        // Fresh authorized recovery still handles admitted work from its original identity.
        var recovered = await f.Restart().RecoverOperationAsync(f.SecurityContext.Operation.OperationId);
        Assert.False(recovered.IsAdopted);
    }
    [Fact]
    public async Task Cancellation_during_migrated_evidence_hashing_preserves_admission_and_exact_bytes()
    {
        using var f = new Fixture();
        Directory.CreateDirectory(f.Root, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var bytes = new byte[200_000]; bytes[0] = 7;
        var path = Path.Combine(f.Root, "legacy"); await File.WriteAllBytesAsync(path, bytes);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        var migration = await new FileSystemArtifactContentMigration().MigrateAsync(f.Root, new(true));
        using var cancellation = new CancellationTokenSource();
        f.Store.InspectionCheckpoint = point => { if (point == "EvidenceChunk") cancellation.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => f.Store.PrepareRevisionValidationAsync(new("legacy"), cancellation.Token));
        f.Store.InspectionCheckpoint = null;
        Assert.Equal(bytes, await File.ReadAllBytesAsync(Path.Combine(migration.RetainedDirectory, "legacy")));
        // An interrupted admission never sets _admitted and bypasses the remaining verification.
        Assert.Equal(bytes, await f.Store.ReadAsync(new("legacy")));
    }

    [Theory]
    [InlineData("entries")]
    [InlineData("rows")]
    [InlineData("metadata")]
    [InlineData("rowbytes")]
    [InlineData("database")]
    [InlineData("evidence")]
    [InlineData("work")]
    [InlineData("duration")]
    public void Invalid_admission_profiles_are_rejected_before_root_creation(string dimension)
    {
        var defaults = new ArtifactContentInspectionLimits();
        var invalid = dimension switch
        {
            "entries" => defaults with { MaximumNamespaceEntries = 0 },
            "rows" => defaults with { MaximumCatalogRows = 0 },
            "metadata" => defaults with { MaximumMetadataBytes = 0 },
            "rowbytes" => defaults with { MaximumRowBytes = 0 },
            "database" => defaults with { MaximumDatabaseBytes = 0 },
            "evidence" => defaults with { MaximumEvidenceBytes = 0 },
            "work" => defaults with { MaximumWorkUnits = 0 },
            _ => defaults with { MaximumDuration = TimeSpan.Zero }
        };
        var root = Path.Combine(Path.GetTempPath(), "emf-invalid-inspection-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<ArgumentOutOfRangeException>(() => new FileSystemArtifactContentStore(root, inspectionLimits: invalid));
        Assert.False(Directory.Exists(root));
    }

    [Fact]
    public async Task Work_limit_failure_becomes_durable_review_without_promotion_or_rebinding_on_restart()
    {
        await using var f = await ArtifactIngestionFixture.CreateAsync();
        await f.Physical.ReadAsync(f.Id);
        var root = Path.Combine(f.Root, "content");
        var limits = new ArtifactContentInspectionLimits { MaximumNamespaceEntries = 1 };
        var limited = new FileSystemArtifactContentStore(root, inspectionLimits: limits);
        await limited.ReadAsync(f.Id); // Admit the empty root under its declared profile.
        await limited.CreateIfAbsentAsync(new("unrelated"), new byte[] { 7 }, Context());
        await Assert.ThrowsAsync<InvalidDataException>(() => f.Service(physical: limited).IngestAsync(f.Draft, f.Content));
        var original = await f.IntentAsync();
        var candidate = await f.Staging.ReadAsync(original.OperationId); Assert.NotNull(candidate);
        Assert.NotNull(original.CandidateHash);
        Assert.Null(await f.Repository.GetArtifactAsync(f.Id));
        var recovered = await f.Restart(physical: new FileSystemArtifactContentStore(root, inspectionLimits: limits))
            .RecoverOperationAsync(original.OperationId);
        Assert.Equal(EMF.Core.Contracts.Ingestion.ArtifactIngestionState.RequiresReview, recovered.State);
        Assert.Equal("RecoveryEvidenceFailure", recovered.SafeFailureCategory);
        var review = await f.Persistence.ReadReviewAuditObligationAsync(original.OperationId); Assert.NotNull(review);
        await f.Restart(physical: new FileSystemArtifactContentStore(root, inspectionLimits: limits))
            .RecoverOperationAsync(original.OperationId);
        Assert.Equal(review, await f.Persistence.ReadReviewAuditObligationAsync(original.OperationId));
        Assert.Equal(candidate, await f.Staging.ReadAsync(original.OperationId));
        Assert.Equal(original.CandidateHash, (await f.IntentAsync()).CandidateHash);
        var verifier = new FileSystemArtifactContentStore(root);
        Assert.Null(await verifier.GetMutationOutcomeAsync(original.OperationId));
        Assert.Null(await verifier.GetMutationOutcomeAsync(original.CleanupOperationId));
        Assert.Equal(new byte[] { 7 }, await verifier.ReadAsync(new("unrelated")));
        Assert.Null(await f.Repository.GetArtifactAsync(f.Id));
        Assert.Equal(1, f.Encryption.Encryptions);
    }

}

using System.Text;
using System.Text.Json;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ReviewerOperationCoordinatorStage2B2Tests
{
    private static readonly Lazy<Task<byte[]>> Template = new(CreateTemplateAsync);

    private static async Task<byte[]> CreateTemplateAsync()
    {
        var path = Path.Combine(Path.GetTempPath(), "emf-stage2b2-template-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            using var connection = VeteransClaimsSqliteConnectionFactory.Create(path);
            SqliteConnection.ClearPool(connection);
            return await File.ReadAllBytesAsync(path);
        }
        finally { foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix); }
    }

    private static ReviewerCapturedBundle Bundle(OperationSnapshotId id)
    {
        var bytes = Encoding.UTF8.GetBytes("captured synthetic plaintext");
        var hash = ReviewerRetainedValidator.Hash(bytes);
        var representation = JsonSerializer.Serialize(new
        {
            Artifact = JsonSerializer.Serialize(new[] { new { Id = "artifact", ArtifactType = "text/plain",
                FingerprintAlgorithm = "SHA-256", FingerprintValue = hash } }),
            Authority = JsonSerializer.Serialize(new[] { new { IsAdopted = 1, ClassificationId = "synthetic",
                ClassificationRevision = "classification-1" } }),
            Provenance = "[]", Relationships = "[]"
        });
        return new(new(1, id, ReviewerRetainedValidator.Profile, ["artifact"],
            ["artifact:provenance", "artifact:relationships"],
            [new("artifact", representation, ReviewerRetainedValidator.Hash(Encoding.UTF8.GetBytes(representation)))],
            [new("artifact", "physical-1", bytes.Length, hash, "SHA-256", hash, "synthetic", "classification-1")]),
            new() { ["artifact"] = bytes });
    }

    private sealed class Capture(ReviewerCapturedBundle bundle, List<string> events) : IReviewerCaptureSession
    {
        public int Calls { get; private set; }
        public int Disposals { get; private set; }
        public Exception? Error { get; set; }
        public Exception? DisposeError { get; set; }
        public Func<Task>? BeforeCapture { get; set; }
        public async Task<ReviewerCapturedBundle> CaptureAsync(CancellationToken ct = default)
        {
            Calls++; events.Add("capture"); ct.ThrowIfCancellationRequested();
            if (BeforeCapture is not null) await BeforeCapture();
            if (Error is not null) throw Error;
            return bundle;
        }
        public ValueTask DisposeAsync()
        {
            Disposals++; events.Add("dispose");
            return DisposeError is null ? ValueTask.CompletedTask : ValueTask.FromException(DisposeError);
        }
    }

    private sealed class CaptureFactory(Capture session, List<string> events) : IReviewerCaptureSessionFactory
    {
        public int Opens { get; private set; }
        public Func<Task>? BeforeOpen { get; set; }
        public Exception? Error { get; set; }
        public async Task<IReviewerCaptureSession> OpenAsync(OperationSnapshotId snapshotId, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); Opens++; events.Add("open");
            if (BeforeOpen is not null) await BeforeOpen();
            if (Error is not null) throw Error;
            return session;
        }
    }

    private sealed class Store(List<string> events) : IReviewerRetainedMaterialStore
    {
        public byte[]? Stored { get; set; }
        public ReviewerRetainedReference? Reference { get; private set; }
        public int Retains { get; private set; }
        public int Reads { get; private set; }
        public Exception? RetainError { get; set; }
        public Exception? ReadError { get; set; }
        public Func<Task>? BeforeRead { get; set; }
        public Func<ReviewerCapturedBundle, ReviewerCapturedBundle>? ReadTransform { get; set; }
        public List<byte[]> Decoded { get; } = [];
        public List<ReviewerRetainedReference> Requested { get; } = [];
        public Task<ReviewerRetainedReference> RetainAsync(ReviewerCapturedBundle bundle, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested(); Retains++; events.Add("retain");
            Stored = ReviewerRetainedValidator.Encode(bundle);
            Reference = new(bundle.Manifest.SnapshotId, ReviewerRetainedValidator.Hash(Stored));
            if (RetainError is not null) throw RetainError; // Publication with a lost receipt.
            return Task.FromResult(Reference!);
        }
        public async Task<ReviewerCapturedBundle> ReadValidatedAsync(ReviewerRetainedReference reference,
            CancellationToken ct = default)
        {
            Reads++; Requested.Add(reference); events.Add("validate"); ct.ThrowIfCancellationRequested();
            if (BeforeRead is not null) await BeforeRead();
            if (ReadError is not null) throw ReadError;
            if (Stored is null || reference != Reference || ReviewerRetainedValidator.Hash(Stored) != reference.BundleSha256)
                throw new InvalidDataException("Missing/corrupt/mismatched retained bytes.");
            var bundle = JsonSerializer.Deserialize<ReviewerCapturedBundle>(Stored)!;
            if (ReadTransform is not null) bundle = ReadTransform(bundle);
            lock (Decoded) Decoded.AddRange(bundle.Content.Values);
            return bundle;
        }
    }

    private sealed class Authorization(List<string> events) : IReviewerCurrentConsumptionAuthorization
    {
        public int Calls { get; private set; }
        public bool Allow { get; set; } = true;
        public Exception? Error { get; set; }
        public Func<Task>? BeforeDecision { get; set; }
        public ReviewerConsumptionAuthorizationRequest? Request { get; private set; }
        public async Task<bool> AuthorizeAsync(ReviewerConsumptionAuthorizationRequest request, CancellationToken ct = default)
        {
            Calls++; Request = request; events.Add("authorize"); ct.ThrowIfCancellationRequested();
            if (BeforeDecision is not null) await BeforeDecision();
            if (Error is not null) throw Error;
            return Allow;
        }
    }

    // Faults wrap the real durable SQLite implementation, including committed
    // transitions whose response is lost. No coordinator lifecycle is mocked.
    private sealed class Repository(IReviewerOperationSnapshotRepository inner, List<string> events)
        : IReviewerOperationSnapshotRepository
    {
        public bool LoseBindResponse { get; set; }
        public bool LoseReadyResponse { get; set; }
        public Func<ReviewerOperationSnapshotRecord, Task>? AfterBind { get; set; }
        public Func<ReviewerOperationSnapshotRecord, Task>? AfterReady { get; set; }
        public int BindCalls { get; private set; }
        public int ReadyCalls { get; private set; }
        public Task<ReviewerOperationSnapshotRecord?> ReadAsync(OperationSnapshotId id, CancellationToken ct = default) => inner.ReadAsync(id, ct);
        public Task<ReviewerOperationSnapshotRecord?> ReadByReviewerOperationAsync(ReviewerOperationId id, CancellationToken ct = default) => inner.ReadByReviewerOperationAsync(id, ct);
        public Task<ReviewerOperationSnapshotRecord> CreateCapturingAsync(OperationSnapshotId id, ReviewerOperationId operation,
            ReviewerSnapshotOwnerToken owner, string profile = ReviewerRetainedValidator.Profile, int representationVersion = 1,
            CancellationToken cancellationToken = default)
        { events.Add("create"); return inner.CreateCapturingAsync(id, operation, owner, profile, representationVersion, cancellationToken); }
        public async Task<ReviewerOperationSnapshotRecord> BindAsync(OperationSnapshotId id, ReviewerOperationId operation,
            long revision, ReviewerSnapshotOwnerToken owner, ReviewerSnapshotCandidate candidate, CancellationToken ct = default)
        {
            BindCalls++; events.Add("bind"); var row = await inner.BindAsync(id, operation, revision, owner, candidate, ct);
            if (AfterBind is not null) await AfterBind(row);
            if (LoseBindResponse) throw new IOException("Lost committed Bind response.");
            return row;
        }
        public async Task<ReviewerOperationSnapshotRecord> ReadyAsync(OperationSnapshotId id, ReviewerOperationId operation,
            long revision, ReviewerSnapshotOwnerToken owner, ReviewerSnapshotReadyEvidence evidence, CancellationToken ct = default)
        {
            ReadyCalls++; events.Add("ready"); var row = await inner.ReadyAsync(id, operation, revision, owner, evidence, ct);
            if (AfterReady is not null) await AfterReady(row);
            if (LoseReadyResponse) throw new IOException("Lost committed Ready response.");
            return row;
        }
        public Task<ReviewerOperationSnapshotRecord> TransferOwnershipAsync(OperationSnapshotId id, ReviewerOperationId operation,
            long revision, ReviewerSnapshotOwnerToken owner, ReviewerSnapshotOwnerToken replacement, CancellationToken ct = default) =>
            inner.TransferOwnershipAsync(id, operation, revision, owner, replacement, ct);
        public Task<ReviewerOperationSnapshotRecord> RecordReviewOrFailureAsync(OperationSnapshotId id, ReviewerOperationId operation,
            ReviewerOperationSnapshotState state, long revision, ReviewerSnapshotOwnerToken owner,
            ReviewerOperationSnapshotFailureCategory category, CancellationToken ct = default) =>
            inner.RecordReviewOrFailureAsync(id, operation, state, revision, owner, category, ct);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Database { get; } = Path.Combine(Path.GetTempPath(), "emf-stage2b2-" + Guid.NewGuid().ToString("N") + ".db");
        public OperationSnapshotId Id { get; } = OperationSnapshotId.New();
        public ReviewerOperationId Operation { get; } = ReviewerOperationId.New();
        public ReviewerSnapshotOwnerToken Owner { get; } = ReviewerSnapshotOwnerToken.New();
        public List<string> Events { get; } = [];
        public Repository Repo { get; }
        public Store Store { get; }
        public Authorization Auth { get; }
        public ReviewerCapturedBundle Captured { get; }
        public Capture Session { get; }
        public CaptureFactory Factory { get; }
        public ReviewerOperationCoordinator Coordinator => new(Repo, Store, Auth);
        private Fixture()
        {
            Repo = new(new SqliteReviewerOperationSnapshotRepository(Database), Events);
            Store = new(Events); Auth = new(Events); Captured = Bundle(Id); Session = new(Captured, Events);
            Factory = new(Session, Events);
        }
        public static async Task<Fixture> Create()
        {
            var f = new Fixture(); await File.WriteAllBytesAsync(f.Database, await Template.Value); return f;
        }
        public Task<ReviewerOperationSnapshotRecord> Start() => Coordinator.StartAsync(Id, Operation, Owner, Factory);
        public async Task<ReviewerOperationSnapshotRecord> Row() => (await Repo.ReadAsync(Id))!;
        public async Task<ReviewerOperationSnapshotRecord> Seed(ReviewerOperationSnapshotState state)
        {
            var row = await Repo.CreateCapturingAsync(Id, Operation, Owner);
            if (state == ReviewerOperationSnapshotState.Capturing) return row;
            var reference = await Store.RetainAsync(Captured);
            var candidate = new ReviewerSnapshotCandidate(reference, row.Profile, row.RepresentationVersion);
            row = await Repo.BindAsync(Id, Operation, row.Revision, Owner, candidate);
            if (state == ReviewerOperationSnapshotState.Ready)
                row = await Repo.ReadyAsync(Id, Operation, row.Revision, Owner, new(candidate, 1));
            Events.Clear(); return row;
        }
        public Task<ReviewerOperationSnapshotRecord> Recover() => Coordinator.RecoverAsync(Id, Operation, Owner);
        public Task<ReviewerRetainedBundleLease> Acquire() => Coordinator.AcquireAsync(Id, Operation, Owner);
        public ValueTask DisposeAsync()
        {
            using var connection = VeteransClaimsSqliteConnectionFactory.Create(Database);
            SqliteConnection.ClearPool(connection);
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(Database + suffix);
            return ValueTask.CompletedTask;
        }
    }

    private static void Zeroed(IEnumerable<byte[]> buffers) =>
        Assert.All(buffers, bytes => Assert.All(bytes, value => Assert.Equal((byte)0, value)));

    [Fact]
    public async Task Fresh_start_rereads_exact_receipt_before_ready_and_clears_owned_plaintext()
    {
        await using var f = await Fixture.Create(); var row = await f.Start();
        Assert.Equal(ReviewerOperationSnapshotState.Ready, row.State); Assert.Equal(3, row.Revision);
        Assert.Equal(ReviewerRetainedValidationContract.Version, row.ReadyValidationVersion);
        Assert.Equal(new[] { "create", "open", "capture", "dispose", "retain", "bind", "validate", "ready" }, f.Events);
        Assert.Equal(f.Owner, row.OwnerToken); Assert.Equal(1, f.Factory.Opens);
        Assert.Equal(f.Store.Reference, Assert.Single(f.Store.Requested));
        Zeroed(f.Captured.Content.Values); Zeroed(f.Store.Decoded); Assert.Equal(1, f.Session.Disposals);
    }

    [Fact]
    public async Task Definite_capture_failure_is_rejected_and_session_disposed()
    {
        await using var f = await Fixture.Create(); f.Session.Error = new InvalidDataException("Rejected capture.");
        await Assert.ThrowsAsync<InvalidDataException>(f.Start);
        var row = await f.Row(); Assert.Equal(ReviewerOperationSnapshotFailureCategory.CaptureRejected, row.FailureCategory);
        Assert.Equal(ReviewerOperationSnapshotDisposition.Failed, row.Disposition);
        Assert.Equal(0, f.Store.Retains); Assert.Equal(1, f.Session.Disposals);
    }

    [Fact]
    public async Task Lost_retention_receipt_leaves_capturing_and_recovery_requires_review_without_recapture()
    {
        await using var f = await Fixture.Create(); f.Store.RetainError = new IOException("Published; receipt lost.");
        await Assert.ThrowsAsync<IOException>(f.Start);
        var before = await f.Row(); Assert.Equal(ReviewerOperationSnapshotState.Capturing, before.State);
        Assert.Null(before.BundleSha256); Assert.NotNull(f.Store.Stored); Zeroed(f.Captured.Content.Values);
        var row = await f.Recover();
        Assert.Equal(ReviewerOperationSnapshotDisposition.RequiresReview, row.Disposition);
        Assert.Equal(ReviewerOperationSnapshotFailureCategory.CaptureRecoveryAmbiguous, row.FailureCategory);
        Assert.Equal(1, f.Session.Calls); Assert.Equal(0, f.Store.Reads); Assert.Equal(0, f.Repo.BindCalls);
    }

    [Fact]
    public async Task Materializing_recovery_validates_durable_receipt_and_advances_without_capture()
    {
        await using var f = await Fixture.Create(); await f.Seed(ReviewerOperationSnapshotState.Materializing);
        var row = await f.Recover(); Assert.Equal(ReviewerOperationSnapshotState.Ready, row.State);
        Assert.Equal(0, f.Session.Calls); Assert.Equal(0, f.Factory.Opens);
        Assert.Equal(f.Store.Reference, Assert.Single(f.Store.Requested));
        Zeroed(f.Store.Decoded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Missing_or_corrupt_materializing_data_requires_review(bool corrupt)
    {
        await using var f = await Fixture.Create(); await f.Seed(ReviewerOperationSnapshotState.Materializing);
        if (corrupt) f.Store.Stored![0] ^= 1; else f.Store.Stored = null;
        var row = await f.Recover(); Assert.Equal(ReviewerOperationSnapshotDisposition.RequiresReview, row.Disposition);
        Assert.Equal(ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid, row.FailureCategory);
        Assert.Equal(0, f.Repo.ReadyCalls); Assert.Equal(0, f.Auth.Calls);
    }

    [Fact]
    public async Task Ready_recovery_is_read_only_even_with_foreign_owner()
    {
        await using var f = await Fixture.Create(); var before = await f.Seed(ReviewerOperationSnapshotState.Ready);
        Assert.Equal(before, await f.Coordinator.RecoverAsync(f.Id, f.Operation, ReviewerSnapshotOwnerToken.New()));
        Assert.Equal(before, await f.Recover()); Assert.Empty(f.Events); Assert.Equal(0, f.Store.Reads);
    }

    [Theory]
    [InlineData(ReviewerOperationSnapshotFailureCategory.CaptureRejected)]
    [InlineData(ReviewerOperationSnapshotFailureCategory.CaptureRecoveryAmbiguous)]
    public async Task Terminal_rows_stay_terminal(ReviewerOperationSnapshotFailureCategory category)
    {
        await using var f = await Fixture.Create(); var initial = await f.Seed(ReviewerOperationSnapshotState.Capturing);
        var terminal = await f.Repo.RecordReviewOrFailureAsync(f.Id, f.Operation, initial.State, initial.Revision, f.Owner, category);
        Assert.Equal(terminal, await f.Recover()); Assert.Equal(terminal, await f.Row());
        Assert.Equal(0, f.Store.Reads); Assert.Equal(0, f.Session.Calls);
    }

    [Theory]
    [InlineData(ReviewerOperationSnapshotState.Capturing)]
    [InlineData(ReviewerOperationSnapshotState.Materializing)]
    public async Task Acquire_rejects_nonready_without_reading_or_authorizing(ReviewerOperationSnapshotState state)
    {
        await using var f = await Fixture.Create(); await f.Seed(state);
        await Assert.ThrowsAsync<ReviewerOperationSnapshotConflictException>(f.Acquire);
        Assert.Equal(0, f.Store.Reads); Assert.Equal(0, f.Auth.Calls);
    }

    [Fact]
    public async Task Acquire_validates_before_authorization_and_returns_exact_retained_bundle()
    {
        await using var f = await Fixture.Create(); await f.Seed(ReviewerOperationSnapshotState.Ready);
        await using var lease = await f.Acquire();
        Assert.Equal(new[] { "validate", "authorize" }, f.Events);
        Assert.Equal(f.Store.Stored, ReviewerRetainedValidator.Encode(lease.Bundle));
        Assert.Same(Assert.Single(f.Store.Decoded), lease.Bundle.Content["artifact"]);
        Assert.Equal(f.Store.Reference, Assert.Single(f.Store.Requested));
    }

    [Fact]
    public async Task Ready_corruption_requires_review_and_never_authorizes()
    {
        await using var f = await Fixture.Create(); await f.Seed(ReviewerOperationSnapshotState.Ready);
        f.Store.Stored![0] ^= 1; await Assert.ThrowsAsync<InvalidDataException>(f.Acquire);
        var row = await f.Row(); Assert.Equal(ReviewerOperationSnapshotState.Ready, row.State);
        Assert.Equal(ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid, row.FailureCategory);
        Assert.Equal(ReviewerOperationSnapshotDisposition.RequiresReview, row.Disposition); Assert.Equal(0, f.Auth.Calls);
    }

    [Fact]
    public async Task Authorization_denial_preserves_ready_and_zeroes_decoded_plaintext()
    {
        await using var f = await Fixture.Create(); var before = await f.Seed(ReviewerOperationSnapshotState.Ready);
        f.Auth.Allow = false; await Assert.ThrowsAsync<UnauthorizedAccessException>(f.Acquire);
        Assert.Equal(before, await f.Row()); Zeroed(f.Store.Decoded);
    }

    [Fact]
    public async Task Authorization_exception_preserves_ready_and_zeroes_decoded_plaintext()
    {
        await using var f = await Fixture.Create(); var before = await f.Seed(ReviewerOperationSnapshotState.Ready);
        f.Auth.Error = new IOException("Authorization unavailable."); await Assert.ThrowsAsync<IOException>(f.Acquire);
        Assert.Equal(before, await f.Row()); Zeroed(f.Store.Decoded);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Lease_disposal_zeroes_even_buffers_removed_by_consumer(bool asynchronous)
    {
        await using var f = await Fixture.Create(); await f.Seed(ReviewerOperationSnapshotState.Ready);
        var lease = await f.Acquire(); var bytes = lease.Bundle.Content["artifact"];
        Assert.Equal(Encoding.UTF8.GetBytes("captured synthetic plaintext"), bytes);
        lease.Bundle.Content.Clear();
        var replacement = Encoding.UTF8.GetBytes("consumer replacement");
        lease.Bundle.Content["artifact"] = replacement;
        if (asynchronous) await lease.DisposeAsync(); else lease.Dispose();
        Zeroed([bytes, replacement]); lease.Dispose(); await lease.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => lease.Bundle);
    }

    [Theory]
    [InlineData(ReviewerOperationSnapshotState.Materializing)]
    [InlineData(ReviewerOperationSnapshotState.Ready)]
    public async Task Validation_cancellation_is_not_corruption(ReviewerOperationSnapshotState state)
    {
        await using var f = await Fixture.Create(); var before = await f.Seed(state);
        f.Store.ReadError = new OperationCanceledException();
        if (state == ReviewerOperationSnapshotState.Ready) await Assert.ThrowsAsync<OperationCanceledException>(f.Acquire);
        else await Assert.ThrowsAsync<OperationCanceledException>(f.Recover);
        Assert.Equal(before, await f.Row()); Assert.Equal(0, f.Auth.Calls);
    }

    [Fact]
    public async Task Lost_bind_response_reconciles_exact_durable_candidate_without_second_mutation()
    {
        await using var f = await Fixture.Create(); f.Repo.LoseBindResponse = true;
        var row = await f.Start(); Assert.Equal(ReviewerOperationSnapshotState.Ready, row.State);
        Assert.Equal(3, row.Revision); Assert.Equal(1, f.Repo.BindCalls); Assert.Equal(1, f.Store.Retains);
        Assert.Equal(f.Store.Reference!.BundleSha256, row.BundleSha256);
    }

    [Fact]
    public async Task Lost_ready_response_reconciles_without_duplicate_advancement()
    {
        await using var f = await Fixture.Create(); f.Repo.LoseReadyResponse = true;
        var row = await f.Start(); Assert.Equal(ReviewerOperationSnapshotState.Ready, row.State);
        Assert.Equal(3, row.Revision); Assert.Equal(1, f.Repo.ReadyCalls); Zeroed(f.Store.Decoded);
    }

    [Fact]
    public async Task Stale_materializing_owner_cannot_advance_after_concurrent_transfer()
    {
        await using var f = await Fixture.Create(); var before = await f.Seed(ReviewerOperationSnapshotState.Materializing);
        var other = ReviewerSnapshotOwnerToken.New();
        f.Store.BeforeRead = async () =>
        {
            await f.Repo.TransferOwnershipAsync(f.Id, f.Operation, before.Revision, f.Owner, other);
        };
        await Assert.ThrowsAsync<ReviewerOperationSnapshotConcurrencyException>(f.Recover);
        var row = await f.Row(); Assert.Equal(ReviewerOperationSnapshotState.Materializing, row.State);
        Assert.Equal(other, row.OwnerToken); Assert.Equal(before.BundleSha256, row.BundleSha256); Zeroed(f.Store.Decoded);
    }

    [Fact]
    public async Task Start_replay_never_captures_or_silently_takes_ownership()
    {
        await using var f = await Fixture.Create(); var before = await f.Seed(ReviewerOperationSnapshotState.Capturing);
        await Assert.ThrowsAsync<ReviewerOperationSnapshotConcurrencyException>(f.Start);
        Assert.Equal(before, await f.Row()); Assert.Equal(0, f.Session.Calls); Assert.Equal(0, f.Session.Disposals);
        Assert.Equal(0, f.Factory.Opens);
        Assert.Equal(0, f.Store.Retains);
    }

    [Fact]
    public async Task Concurrent_starts_capture_once_and_preserve_one_binding()
    {
        await using var f = await Fixture.Create(); var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Session.BeforeCapture = async () => { entered.SetResult(); await release.Task; };
        var first = f.Start(); await entered.Task;
        var secondSession = new Capture(Bundle(f.Id), f.Events);
        var secondFactory = new CaptureFactory(secondSession, f.Events);
        try
        {
            await Assert.ThrowsAsync<ReviewerOperationSnapshotConcurrencyException>(() =>
                f.Coordinator.StartAsync(f.Id, f.Operation, ReviewerSnapshotOwnerToken.New(), secondFactory));
        }
        finally { release.SetResult(); }
        var row = await first; Assert.Equal(3, row.Revision); Assert.Equal(1, f.Session.Calls);
        Assert.Equal(0, secondSession.Calls); Assert.Equal(0, secondSession.Disposals);
        Assert.Equal(0, secondFactory.Opens); Assert.Equal(1, f.Store.Retains);
    }

    [Fact]
    public async Task Returned_manifest_mismatch_is_reviewed_and_decoded_buffers_zeroed()
    {
        await using var f = await Fixture.Create(); await f.Seed(ReviewerOperationSnapshotState.Materializing);
        f.Store.ReadTransform = bundle => bundle with { Manifest = bundle.Manifest with { SnapshotId = OperationSnapshotId.New() } };
        var row = await f.Recover(); Assert.Equal(ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid, row.FailureCategory);
        Zeroed(f.Store.Decoded); Assert.Equal(0, f.Repo.ReadyCalls);
    }

    [Fact]
    public async Task Capture_disposal_failure_prevents_retention_and_clears_capture_buffers()
    {
        await using var f = await Fixture.Create(); f.Session.DisposeError = new IOException("Cleanup failed.");
        await Assert.ThrowsAsync<IOException>(f.Start); Assert.Equal(0, f.Store.Retains);
        Assert.Equal(ReviewerOperationSnapshotFailureCategory.CaptureRejected, (await f.Row()).FailureCategory);
        Zeroed(f.Captured.Content.Values);
    }

    [Fact]
    public async Task Cancellation_after_decode_during_authorization_clears_plaintext_and_preserves_ready()
    {
        await using var f = await Fixture.Create(); var before = await f.Seed(ReviewerOperationSnapshotState.Ready);
        using var cts = new CancellationTokenSource(); f.Auth.BeforeDecision = () => { cts.Cancel(); return Task.CompletedTask; };
        await Assert.ThrowsAsync<OperationCanceledException>(() => f.Coordinator.AcquireAsync(f.Id, f.Operation, f.Owner, cts.Token));
        Assert.Equal(before, await f.Row()); Zeroed(f.Store.Decoded);
    }

    [Fact]
    public async Task Concurrent_ready_revocation_during_authorization_returns_no_plaintext()
    {
        await using var f = await Fixture.Create(); var before = await f.Seed(ReviewerOperationSnapshotState.Ready);
        f.Auth.BeforeDecision = async () =>
        {
            await f.Repo.RecordReviewOrFailureAsync(f.Id, f.Operation, before.State, before.Revision, f.Owner,
                ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid);
        };
        await Assert.ThrowsAsync<ReviewerOperationSnapshotConcurrencyException>(f.Acquire); Zeroed(f.Store.Decoded);
        Assert.Equal(ReviewerOperationSnapshotDisposition.RequiresReview, (await f.Row()).Disposition);
    }

    [Fact]
    public async Task Capture_cancellation_leaves_capturing_and_disposes_without_retention()
    {
        await using var f = await Fixture.Create(); f.Session.Error = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(f.Start);
        var row = await f.Row(); Assert.Equal(ReviewerOperationSnapshotState.Capturing, row.State);
        Assert.Equal(ReviewerOperationSnapshotDisposition.Active, row.Disposition); Assert.Null(row.FailureCategory);
        Assert.Equal(1, f.Session.Disposals); Assert.Equal(0, f.Store.Retains);
    }

    [Fact]
    public async Task Retention_cancellation_after_publication_never_infers_a_receipt()
    {
        await using var f = await Fixture.Create(); f.Store.RetainError = new OperationCanceledException();
        await Assert.ThrowsAsync<OperationCanceledException>(f.Start);
        var row = await f.Row(); Assert.Equal(ReviewerOperationSnapshotState.Capturing, row.State);
        Assert.Null(row.BundleSha256); Assert.Null(row.FailureCategory); Assert.Equal(0, f.Store.Reads);
        Assert.Equal(0, f.Repo.BindCalls); Zeroed(f.Captured.Content.Values);
    }

    [Fact]
    public async Task Lost_bind_response_does_not_adopt_concurrently_transferred_ownership()
    {
        await using var f = await Fixture.Create(); var other = ReviewerSnapshotOwnerToken.New();
        f.Repo.LoseBindResponse = true;
        f.Repo.AfterBind = async row =>
        {
            await f.Repo.TransferOwnershipAsync(f.Id, f.Operation, row.Revision, f.Owner, other);
        };
        await Assert.ThrowsAsync<ReviewerOperationSnapshotConcurrencyException>(f.Start);
        var actual = await f.Row(); Assert.Equal(other, actual.OwnerToken);
        Assert.Equal(ReviewerOperationSnapshotState.Materializing, actual.State);
        Assert.Equal(f.Store.Reference!.BundleSha256, actual.BundleSha256);
        Assert.Equal(0, f.Store.Reads); Assert.Equal(0, f.Repo.ReadyCalls); Zeroed(f.Captured.Content.Values);
    }

    [Fact]
    public async Task Lost_ready_response_never_reactivates_a_concurrently_terminal_row()
    {
        await using var f = await Fixture.Create(); f.Repo.LoseReadyResponse = true;
        f.Repo.AfterReady = async row =>
        {
            await f.Repo.RecordReviewOrFailureAsync(f.Id, f.Operation, row.State, row.Revision, f.Owner,
                ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid);
        };
        await Assert.ThrowsAsync<ReviewerOperationSnapshotConflictException>(f.Start);
        var actual = await f.Row(); Assert.Equal(ReviewerOperationSnapshotDisposition.RequiresReview, actual.Disposition);
        Assert.Equal(ReviewerOperationSnapshotState.Ready, actual.State); Assert.Equal(4, actual.Revision);
        Assert.Equal(1, f.Repo.ReadyCalls); Zeroed(f.Store.Decoded); Zeroed(f.Captured.Content.Values);
    }

    [Fact]
    public async Task Concurrent_materializing_recoveries_recognize_one_ready_advancement()
    {
        await using var f = await Fixture.Create(); await f.Seed(ReviewerOperationSnapshotState.Materializing);
        var bothEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entered = 0;
        f.Store.BeforeRead = () =>
        {
            if (Interlocked.Increment(ref entered) == 2) bothEntered.SetResult();
            return bothEntered.Task;
        };
        var results = await Task.WhenAll(f.Recover(), f.Recover());
        Assert.All(results, row => Assert.Equal(3, row.Revision));
        Assert.Equal(3, (await f.Row()).Revision); Assert.Equal(ReviewerOperationSnapshotState.Ready, (await f.Row()).State);
        Assert.Equal(0, f.Session.Calls); Assert.Equal(2, f.Store.Reads); Zeroed(f.Store.Decoded);
    }

    [Fact]
    public async Task Durable_capturing_with_independent_owner_precedes_factory_open()
    {
        await using var f = await Fixture.Create();
        f.Factory.BeforeOpen = async () =>
        {
            // A separate repository read sees the committed row before any live
            // capture acquisition, so this also detects an uncommitted fence.
            var row = await f.Row();
            Assert.Equal(f.Owner, row.OwnerToken); Assert.Equal(1, row.Revision);
            Assert.Equal(f.Operation, row.ReviewerOperationId); Assert.Equal(f.Id, row.SnapshotId);
            Assert.Equal(ReviewerOperationSnapshotState.Capturing, row.State);
            Assert.Equal(0, f.Session.Calls); Assert.Equal(0, f.Store.Retains);
        };
        var ready = await f.Start(); Assert.Equal(f.Owner, ready.OwnerToken);
        Assert.True(f.Events.IndexOf("create") < f.Events.IndexOf("open"));
    }

    [Theory]
    [InlineData(ReviewerOperationSnapshotState.Capturing)]
    [InlineData(ReviewerOperationSnapshotState.Materializing)]
    public async Task Reading_durable_owner_does_not_grant_recovery_to_a_different_worker(
        ReviewerOperationSnapshotState state)
    {
        await using var f = await Fixture.Create(); var before = await f.Seed(state);
        var observed = await f.Row(); Assert.Equal(before, observed);
        var uncredentialedWorker = ReviewerSnapshotOwnerToken.New();
        await Assert.ThrowsAsync<ReviewerOperationSnapshotConcurrencyException>(() =>
            f.Coordinator.RecoverAsync(f.Id, f.Operation, uncredentialedWorker));
        Assert.Equal(before, await f.Row()); Assert.Equal(0, f.Factory.Opens); Assert.Equal(0, f.Store.Reads);
        // Recovery receives the independently held composition credential, not
        // observed.OwnerToken. Reading an exact bearer value is not authentication.
        var recovered = await f.Recover(); Assert.Equal(f.Owner, recovered.OwnerToken);
    }

    [Fact]
    public async Task Wrong_start_owner_never_opens_a_capture_session()
    {
        await using var f = await Fixture.Create(); var before = await f.Seed(ReviewerOperationSnapshotState.Capturing);
        await Assert.ThrowsAsync<ReviewerOperationSnapshotConcurrencyException>(() =>
            f.Coordinator.StartAsync(f.Id, f.Operation, ReviewerSnapshotOwnerToken.New(), f.Factory));
        Assert.Equal(before, await f.Row()); Assert.Equal(0, f.Factory.Opens); Assert.Equal(0, f.Session.Calls);
    }

    [Fact]
    public async Task Definite_opener_failure_records_rejection_without_retention()
    {
        await using var f = await Fixture.Create(); f.Factory.Error = new IOException("Open failed before acquisition.");
        await Assert.ThrowsAsync<IOException>(f.Start);
        var row = await f.Row(); Assert.Equal(ReviewerOperationSnapshotFailureCategory.CaptureRejected, row.FailureCategory);
        Assert.Equal(ReviewerOperationSnapshotDisposition.Failed, row.Disposition);
        Assert.Equal(0, f.Session.Calls); Assert.Equal(0, f.Session.Disposals); Assert.Equal(0, f.Store.Retains);
    }

    [Fact]
    public async Task Authorization_request_excludes_owner_and_contains_validated_classification_facts()
    {
        await using var f = await Fixture.Create(); await f.Seed(ReviewerOperationSnapshotState.Ready);
        await using var lease = await f.Acquire(); var request = Assert.IsType<ReviewerConsumptionAuthorizationRequest>(f.Auth.Request);
        Assert.Equal(f.Operation, request.ReviewerOperationId); Assert.Equal(f.Id, request.SnapshotId);
        Assert.Equal(ReviewerRetainedValidator.Profile, request.Profile); Assert.Equal(1, request.RepresentationVersion);
        var member = Assert.Single(request.Members); var validated = Assert.Single(lease.Bundle.Manifest.Members);
        Assert.Equal(validated.ArtifactId, member.ArtifactId); Assert.Equal(validated.ClassificationId, member.ClassificationId);
        Assert.Equal(validated.ClassificationRevision, member.ClassificationRevision);
        foreach (var type in new[] { request.GetType(), member.GetType() })
            Assert.DoesNotContain(type.GetProperties(), property => property.Name.Contains("Owner", StringComparison.Ordinal) ||
                property.PropertyType == typeof(ReviewerSnapshotOwnerToken) || property.PropertyType == typeof(ReviewerOperationSnapshotRecord));
        var parameter = typeof(IReviewerCurrentConsumptionAuthorization).GetMethod("AuthorizeAsync")!.GetParameters()[0];
        Assert.Equal(typeof(ReviewerConsumptionAuthorizationRequest), parameter.ParameterType);
        Assert.True(f.Events.IndexOf("validate") < f.Events.IndexOf("authorize"));
    }

    [Fact]
    public void Authorization_request_copies_and_bounds_member_facts()
    {
        var member = new ReviewerConsumptionAuthorizationMember("artifact", "synthetic", "classification-1");
        var input = new[] { member };
        var request = new ReviewerConsumptionAuthorizationRequest(ReviewerOperationId.New(), OperationSnapshotId.New(),
            ReviewerRetainedValidator.Profile, 1, input);
        input[0] = new("other", "other", "other"); Assert.Equal(member, Assert.Single(request.Members));
        Assert.Throws<NotSupportedException>(() => ((IList<ReviewerConsumptionAuthorizationMember>)request.Members)[0] = input[0]);
        Assert.Throws<InvalidDataException>(() => new ReviewerConsumptionAuthorizationRequest(ReviewerOperationId.New(),
            OperationSnapshotId.New(), ReviewerRetainedValidator.Profile, 1,
            Enumerable.Repeat(member, ReviewerConsumptionAuthorizationRequest.MaximumMembers + 1)));
    }

    [Fact]
    public async Task Wrong_independent_owner_cannot_terminalize_corrupt_ready_material()
    {
        await using var f = await Fixture.Create(); var before = await f.Seed(ReviewerOperationSnapshotState.Ready);
        f.Store.Stored![0] ^= 1;
        await Assert.ThrowsAsync<ReviewerOperationSnapshotConcurrencyException>(() =>
            f.Coordinator.AcquireAsync(f.Id, f.Operation, ReviewerSnapshotOwnerToken.New()));
        Assert.Equal(before, await f.Row()); Assert.Equal(0, f.Auth.Calls);
    }
}

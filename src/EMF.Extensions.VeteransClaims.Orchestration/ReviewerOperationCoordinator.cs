using System.Security.Cryptography;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// Coordinates short repository mutations around external work. This proof-profile
// coordinator does not authorize or invoke full reviewer package assembly.
public sealed class ReviewerOperationCoordinator
{
    // Prevent same-repository starts from racing within this process. Trusted
    // composition must independently retain credentials and use a distinct token
    // per worker; sharing one credential across processes is not a fencing grant.
    private static readonly HashSet<(IReviewerOperationSnapshotRepository, OperationSnapshotId)> ActiveStarts = [];
    private readonly IReviewerOperationSnapshotRepository repository;
    private readonly IReviewerRetainedMaterialStore retained;
    private readonly IReviewerCurrentConsumptionAuthorization authorization;

    public ReviewerOperationCoordinator(IReviewerOperationSnapshotRepository repository,
        IReviewerRetainedMaterialStore retained, IReviewerCurrentConsumptionAuthorization authorization)
    {
        this.repository = repository ?? throw new ArgumentNullException(nameof(repository));
        this.retained = retained ?? throw new ArgumentNullException(nameof(retained));
        this.authorization = authorization ?? throw new ArgumentNullException(nameof(authorization));
    }

    public async Task<ReviewerOperationSnapshotRecord> StartAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, ReviewerSnapshotOwnerToken owner,
        IReviewerCaptureSessionFactory captureFactory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(captureFactory);
        var reservation = (repository, snapshotId);
        lock (ActiveStarts)
            if (!ActiveStarts.Add(reservation))
                throw new ReviewerOperationSnapshotConcurrencyException("A start for this snapshot is already active.");
        ReviewerOperationSnapshotRecord? row = null;
        ReviewerCapturedBundle? captured = null;
        try
        {
            // A preexisting Capturing row is ambiguous even for the same owner.
            // Read recognition must never permit another capture attempt.
            if (await repository.ReadAsync(snapshotId, cancellationToken) is not null)
                throw new ReviewerOperationSnapshotConcurrencyException("Snapshot already exists; use explicit recovery.");
            row = await repository.CreateCapturingAsync(snapshotId, operationId, owner,
                cancellationToken: cancellationToken);
            RequireOwnedActive(row, snapshotId, operationId, owner);
            if (row.State != ReviewerOperationSnapshotState.Capturing || row.Revision != 1)
                throw new ReviewerOperationSnapshotConflictException("Start requires a newly created Capturing row.");
            try
            {
                // The durable Capturing mutation has finished before even the
                // opener runs, including a real SQLite read-view acquisition.
                await using (var capture = await captureFactory.OpenAsync(snapshotId, cancellationToken))
                {
                    captured = await capture.CaptureAsync(cancellationToken);
                    ValidateBundle(captured, row, cancellationToken);
                }
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                // No retention has begun, so rejection is definite. Disposal has
                // already run; a cleanup failure also prevents retention.
                await RecordAsync(row!, owner, ReviewerOperationSnapshotFailureCategory.CaptureRejected,
                    cancellationToken);
                throw;
            }

            ReviewerRetainedReference reference;
            try { reference = await retained.RetainAsync(captured!, cancellationToken); }
            finally { Clear(captured); }
            // Only the receipt actually returned by Retain is a candidate. Any
            // exception above leaves Capturing, even if storage published bytes.
            if (reference.SnapshotId != snapshotId)
                throw new InvalidDataException("Retained receipt belongs to another snapshot.");
            var candidate = new ReviewerSnapshotCandidate(reference, row!.Profile, row.RepresentationVersion);
            row = await BindAsync(row, owner, candidate, cancellationToken);
            return await CompleteMaterializingAsync(row, owner, candidate, cancellationToken);
        }
        finally
        {
            Clear(captured);
            lock (ActiveStarts) ActiveStarts.Remove(reservation);
        }
    }

    // Recovery uses an explicitly supplied lifecycle token, never an ownership
    // token adopted from a repository read. Ready/terminal recognition is read-only.
    public async Task<ReviewerOperationSnapshotRecord> RecoverAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, ReviewerSnapshotOwnerToken ownerToken,
        CancellationToken cancellationToken = default)
    {
        var row = await ReadRequiredAsync(snapshotId, operationId, cancellationToken);
        if (row.Disposition != ReviewerOperationSnapshotDisposition.Active ||
            row.State == ReviewerOperationSnapshotState.Ready) return row;
        RequireOwnedActive(row, snapshotId, operationId, ownerToken);
        if (row.State == ReviewerOperationSnapshotState.Capturing)
            return await RecordAsync(row, ownerToken,
                ReviewerOperationSnapshotFailureCategory.CaptureRecoveryAmbiguous, cancellationToken);
        ReviewerSnapshotCandidate candidate;
        try { candidate = Candidate(row); }
        catch (InvalidDataException)
        {
            return await RecordAsync(row, ownerToken,
                ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid, cancellationToken);
        }
        return await CompleteMaterializingAsync(row, ownerToken, candidate, cancellationToken);
    }

    public async Task<ReviewerRetainedBundleLease> AcquireAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, ReviewerSnapshotOwnerToken ownerToken,
        CancellationToken cancellationToken = default)
    {
        var row = await ReadRequiredAsync(snapshotId, operationId, cancellationToken);
        RequireConsumable(row);
        ReviewerCapturedBundle? bundle = null;
        try
        {
            try
            {
                var candidate = Candidate(row);
                bundle = await retained.ReadValidatedAsync(candidate.Reference, cancellationToken);
                ValidateBundle(bundle, row, cancellationToken);
            }
            catch (Exception error) when (IsInvalidRetained(error))
            {
                await RecordAsync(row, ownerToken,
                    ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid, cancellationToken);
                throw;
            }
            // Reject a concurrent terminal/revision change before consulting the
            // current authorization provider. No transaction spans validation.
            var current = await ReadRequiredAsync(snapshotId, operationId, cancellationToken);
            if (current != row)
                throw new ReviewerOperationSnapshotConcurrencyException("Snapshot changed during retained validation.");
            cancellationToken.ThrowIfCancellationRequested();
            var request = new ReviewerConsumptionAuthorizationRequest(row.ReviewerOperationId,
                row.SnapshotId, row.Profile, row.RepresentationVersion,
                bundle.Manifest.Members.Select(member => new ReviewerConsumptionAuthorizationMember(
                    member.ArtifactId, member.ClassificationId, member.ClassificationRevision)));
            if (!await authorization.AuthorizeAsync(request, cancellationToken))
                throw new UnauthorizedAccessException("Current reviewer consumption authorization was denied.");
            cancellationToken.ThrowIfCancellationRequested();
            current = await ReadRequiredAsync(snapshotId, operationId, cancellationToken);
            if (current != row)
                throw new ReviewerOperationSnapshotConcurrencyException("Snapshot changed during authorization.");
            var lease = new ReviewerRetainedBundleLease(bundle);
            bundle = null; // Ownership transfers only after validation and allow.
            return lease;
        }
        finally { Clear(bundle); }
    }

    private async Task<ReviewerOperationSnapshotRecord> CompleteMaterializingAsync(
        ReviewerOperationSnapshotRecord row, ReviewerSnapshotOwnerToken owner,
        ReviewerSnapshotCandidate candidate, CancellationToken ct)
    {
        RequireExactCandidate(row, candidate);
        if (row.State == ReviewerOperationSnapshotState.Ready)
        {
            RequireConsumable(row);
            return row;
        }
        RequireOwnedActive(row, row.SnapshotId, row.ReviewerOperationId, owner);
        if (row.State != ReviewerOperationSnapshotState.Materializing)
            throw new ReviewerOperationSnapshotConflictException("Retained validation requires Materializing.");
        ReviewerCapturedBundle? bundle = null;
        try
        {
            try
            {
                bundle = await retained.ReadValidatedAsync(candidate.Reference, ct);
                ValidateBundle(bundle, row, ct);
                Clear(bundle);
                bundle = null;
            }
            catch (Exception error) when (IsInvalidRetained(error))
            {
                return await RecordAsync(row, owner,
                    ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid, ct);
            }
            ct.ThrowIfCancellationRequested();
            var evidence = new ReviewerSnapshotReadyEvidence(candidate, ReviewerRetainedValidationContract.Version);
            ReviewerOperationSnapshotRecord result;
            try
            {
                result = await repository.ReadyAsync(row.SnapshotId, row.ReviewerOperationId,
                    row.Revision, owner, evidence, ct);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                var actual = await repository.ReadAsync(row.SnapshotId, ct);
                if (actual is null || !ExactCandidate(actual, row.ReviewerOperationId, candidate) ||
                    actual.State != ReviewerOperationSnapshotState.Ready ||
                    actual.ReadyValidationVersion != evidence.ValidationVersion) throw;
                result = actual; // Recognize an exact durable lost-response result.
            }
            RequireExactCandidate(result, candidate);
            if (result.ReviewerOperationId != row.ReviewerOperationId)
                throw new ReviewerOperationSnapshotConflictException("Ready reviewer operation changed.");
            RequireConsumable(result);
            return result;
        }
        finally { Clear(bundle); }
    }

    private async Task<ReviewerOperationSnapshotRecord> BindAsync(ReviewerOperationSnapshotRecord row,
        ReviewerSnapshotOwnerToken owner, ReviewerSnapshotCandidate candidate, CancellationToken ct)
    {
        ReviewerOperationSnapshotRecord result;
        try
        {
            result = await repository.BindAsync(row.SnapshotId, row.ReviewerOperationId,
                row.Revision, owner, candidate, ct);
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            var actual = await repository.ReadAsync(row.SnapshotId, ct);
            if (actual is null || !ExactCandidate(actual, row.ReviewerOperationId, candidate) ||
                actual.State is not (ReviewerOperationSnapshotState.Materializing or ReviewerOperationSnapshotState.Ready)) throw;
            result = actual;
        }
        RequireExactCandidate(result, candidate);
        if (result.ReviewerOperationId != row.ReviewerOperationId)
            throw new ReviewerOperationSnapshotConflictException("Bound reviewer operation changed.");
        return result;
    }

    private Task<ReviewerOperationSnapshotRecord> RecordAsync(ReviewerOperationSnapshotRecord row,
        ReviewerSnapshotOwnerToken owner, ReviewerOperationSnapshotFailureCategory category, CancellationToken ct) =>
        repository.RecordReviewOrFailureAsync(row.SnapshotId, row.ReviewerOperationId, row.State,
            row.Revision, owner, category, ct);

    private async Task<ReviewerOperationSnapshotRecord> ReadRequiredAsync(OperationSnapshotId id,
        ReviewerOperationId operation, CancellationToken ct)
    {
        var row = await repository.ReadAsync(id, ct)
            ?? throw new ReviewerOperationSnapshotConflictException("Reviewer snapshot does not exist.");
        if (row.SnapshotId != id || row.ReviewerOperationId != operation)
            throw new ReviewerOperationSnapshotConflictException("Reviewer operation identity mismatch.");
        return row;
    }

    private static ReviewerSnapshotCandidate Candidate(ReviewerOperationSnapshotRecord row)
    {
        if (row.BundleSha256 is null || row.BundleSha256.Length != 64 ||
            row.BundleSha256.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new InvalidDataException("Missing or invalid durable retained receipt.");
        return new(new(row.SnapshotId, row.BundleSha256), row.Profile, row.RepresentationVersion);
    }

    private static bool ExactCandidate(ReviewerOperationSnapshotRecord row, ReviewerOperationId operation,
        ReviewerSnapshotCandidate candidate) => row.ReviewerOperationId == operation &&
        row.SnapshotId == candidate.Reference.SnapshotId && row.BundleSha256 == candidate.Reference.BundleSha256 &&
        row.Profile == candidate.Profile && row.RepresentationVersion == candidate.RepresentationVersion;

    private static void RequireExactCandidate(ReviewerOperationSnapshotRecord row, ReviewerSnapshotCandidate candidate)
    {
        if (!ExactCandidate(row, row.ReviewerOperationId, candidate))
            throw new ReviewerOperationSnapshotConflictException("Durable row differs from the exact retained receipt.");
    }

    private static void RequireOwnedActive(ReviewerOperationSnapshotRecord row, OperationSnapshotId id,
        ReviewerOperationId operation, ReviewerSnapshotOwnerToken owner)
    {
        if (row.SnapshotId != id || row.ReviewerOperationId != operation || row.OwnerToken != owner)
            throw new ReviewerOperationSnapshotConcurrencyException("No ownership was granted by the durable row.");
        if (row.Disposition != ReviewerOperationSnapshotDisposition.Active)
            throw new ReviewerOperationSnapshotConflictException("Reviewer snapshot is terminal.");
    }

    private static void RequireConsumable(ReviewerOperationSnapshotRecord row)
    {
        if (!row.IsConsumable || row.ReadyValidationVersion != ReviewerRetainedValidationContract.Version ||
            row.Profile != ReviewerRetainedValidator.Profile || row.RepresentationVersion != 1)
            throw new ReviewerOperationSnapshotConflictException("Snapshot is not active Ready with supported retained validation.");
    }

    private static void ValidateBundle(ReviewerCapturedBundle bundle, ReviewerOperationSnapshotRecord row, CancellationToken ct)
    {
        ReviewerRetainedValidator.Validate(bundle, row.SnapshotId, ct);
        if (bundle.Manifest.Profile != row.Profile || bundle.Manifest.Version != row.RepresentationVersion)
            throw new InvalidDataException("Retained profile or representation differs from the durable row.");
    }

    private static bool IsInvalidRetained(Exception error) => error is InvalidDataException or
        FileNotFoundException or DirectoryNotFoundException or CryptographicException;

    private static void Clear(ReviewerCapturedBundle? bundle)
    {
        if (bundle?.Content is null) return;
        foreach (var bytes in bundle.Content.Values)
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
    }
}

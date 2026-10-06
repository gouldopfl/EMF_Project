using EMF.Core.Contracts.Storage;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteReviewerOperationSnapshotRepository : IReviewerOperationSnapshotRepository
{
    private const string Table = "VeteransClaims_ReviewerOperationSnapshots";
    private const string Columns = "OperationSnapshotId, ReviewerOperationId, State, Revision, OwnerToken, Profile, RepresentationVersion, BundleSha256, ReadyValidationVersion, Disposition, FailureCategory";
    private readonly string databasePath;

    public SqliteReviewerOperationSnapshotRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        this.databasePath = databasePath;
    }

    private SqliteConnection CreateConnection() => VeteransClaimsSqliteConnectionFactory.Create(databasePath);

    public Task<ReviewerOperationSnapshotRecord?> ReadAsync(OperationSnapshotId snapshotId,
        CancellationToken cancellationToken = default) => ReadPublicAsync("OperationSnapshotId", snapshotId.Value, cancellationToken);

    public Task<ReviewerOperationSnapshotRecord?> ReadByReviewerOperationAsync(ReviewerOperationId operationId,
        CancellationToken cancellationToken = default) => ReadPublicAsync("ReviewerOperationId", operationId.Value, cancellationToken);

    private async Task<ReviewerOperationSnapshotRecord?> ReadPublicAsync(string column, string value, CancellationToken ct)
    {
        ArtifactContentIdentity.Validate(value);
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        return await ReadRowAsync(connection, null, column, value, ct);
    }

    private static async Task<ReviewerOperationSnapshotRecord?> ReadRowAsync(SqliteConnection connection,
        SqliteTransaction? transaction, string column, string value, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"SELECT {Columns} FROM {Table} WHERE {column} = $id;";
        command.Parameters.AddWithValue("$id", value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        return new(new(reader.GetString(0)), new(reader.GetString(1)),
            Enum.Parse<ReviewerOperationSnapshotState>(reader.GetString(2)), reader.GetInt64(3),
            new(reader.GetString(4)), reader.GetString(5), reader.GetInt32(6),
            reader.IsDBNull(7) ? null : reader.GetString(7), reader.IsDBNull(8) ? null : reader.GetInt32(8),
            Enum.Parse<ReviewerOperationSnapshotDisposition>(reader.GetString(9)),
            reader.IsDBNull(10) ? null : (ReviewerOperationSnapshotFailureCategory)reader.GetInt32(10));
    }

    private static void ValidateIdentities(OperationSnapshotId snapshotId, ReviewerOperationId operationId,
        ReviewerSnapshotOwnerToken ownerToken)
    {
        ArtifactContentIdentity.Validate(snapshotId.Value);
        ArtifactContentIdentity.Validate(operationId.Value);
        ArtifactContentIdentity.Validate(ownerToken.Value);
    }

    private static void ValidateProfile(string profile, int version)
    {
        if (profile != ReviewerRetainedValidator.Profile || version != 1)
            throw new ReviewerOperationSnapshotConflictException("Unsupported reviewer capture profile or representation version.");
    }

    private static void ValidateCandidate(ReviewerSnapshotCandidate candidate)
    {
        ArgumentNullException.ThrowIfNull(candidate);
        ArgumentNullException.ThrowIfNull(candidate.Reference);
        ArtifactContentIdentity.Validate(candidate.Reference.SnapshotId.Value);
        ValidateProfile(candidate.Profile, candidate.RepresentationVersion);
        var hash = candidate.Reference.BundleSha256;
        if (hash is null || hash.Length != 64 || hash.Any(c => !(c is >= '0' and <= '9' or >= 'A' and <= 'F')))
            throw new ReviewerOperationSnapshotConflictException("Invalid exact retained receipt hash.");
    }

    private static void RequireIdentity(ReviewerOperationSnapshotRecord row, ReviewerOperationId operationId)
    {
        if (row.ReviewerOperationId != operationId)
            throw new ReviewerOperationSnapshotConflictException("Reviewer operation identity conflicts with the snapshot binding.");
    }

    private static void RequireCandidate(ReviewerOperationSnapshotRecord row, ReviewerSnapshotCandidate candidate)
    {
        if (candidate.Reference.SnapshotId != row.SnapshotId || candidate.Profile != row.Profile ||
            candidate.RepresentationVersion != row.RepresentationVersion ||
            (row.BundleSha256 is not null && candidate.Reference.BundleSha256 != row.BundleSha256))
            throw new ReviewerOperationSnapshotConflictException("Candidate conflicts with the immutable snapshot binding.");
    }

    private static void RequireActive(ReviewerOperationSnapshotRecord row)
    {
        if (row.Disposition != ReviewerOperationSnapshotDisposition.Active)
            throw new ReviewerOperationSnapshotConflictException("Reviewer snapshot disposition is terminal.");
    }

    public async Task<ReviewerOperationSnapshotRecord> CreateCapturingAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, ReviewerSnapshotOwnerToken ownerToken,
        string profile = ReviewerRetainedValidator.Profile, int representationVersion = 1,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentities(snapshotId, operationId, ownerToken);
        ValidateProfile(profile, representationVersion);
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var existing = await ReadRowAsync(connection, transaction, "OperationSnapshotId", snapshotId.Value, cancellationToken);
        if (existing is not null)
        {
            RequireIdentity(existing, operationId);
            if (existing.Profile != profile || existing.RepresentationVersion != representationVersion)
                throw new ReviewerOperationSnapshotConflictException("Creation profile conflicts with the durable row.");
            return existing; // Owner is not immutable creation equivalence; never transfer it here.
        }
        if (await ReadRowAsync(connection, transaction, "ReviewerOperationId", operationId.Value, cancellationToken) is not null)
            throw new ReviewerOperationSnapshotConflictException("Reviewer operation already binds another snapshot.");
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"INSERT INTO {Table} ({Columns}) VALUES ($id, $operation, 'Capturing', 1, $owner, $profile, $version, NULL, NULL, 'Active', NULL);";
        command.Parameters.AddWithValue("$id", snapshotId.Value);
        command.Parameters.AddWithValue("$operation", operationId.Value);
        command.Parameters.AddWithValue("$owner", ownerToken.Value);
        command.Parameters.AddWithValue("$profile", profile);
        command.Parameters.AddWithValue("$version", representationVersion);
        await command.ExecuteNonQueryAsync(cancellationToken);
        var result = await ReadRowAsync(connection, transaction, "OperationSnapshotId", snapshotId.Value, cancellationToken)
            ?? throw new InvalidDataException("Created reviewer snapshot row is missing.");
        await transaction.CommitAsync(cancellationToken);
        return result;
    }

    private sealed record Mutation(string Assignments, params (string Name, object Value)[] Parameters);

    // A null decision recognizes an existing committed result, without a write or an ownership grant.
    private async Task<ReviewerOperationSnapshotRecord> MutateAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, long expectedRevision, ReviewerSnapshotOwnerToken ownerToken,
        Func<ReviewerOperationSnapshotRecord, Mutation?> decide, CancellationToken ct)
    {
        ValidateIdentities(snapshotId, operationId, ownerToken);
        await using var connection = CreateConnection();
        await connection.OpenAsync(ct);
        await using var transaction = connection.BeginTransaction(deferred: false);
        var row = await ReadRowAsync(connection, transaction, "OperationSnapshotId", snapshotId.Value, ct)
            ?? throw new ReviewerOperationSnapshotConflictException("Reviewer snapshot does not exist.");
        RequireIdentity(row, operationId);
        var mutation = decide(row);
        if (mutation is null) return row;
        if (row.Revision != expectedRevision || row.OwnerToken != ownerToken)
            throw new ReviewerOperationSnapshotConcurrencyException("Reviewer snapshot revision or owner token is stale.");
        var revision = checked(row.Revision + 1);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = $"UPDATE {Table} SET {mutation.Assignments}, Revision = $next WHERE OperationSnapshotId = $id AND ReviewerOperationId = $operation AND State = $state AND Disposition = 'Active' AND Revision = $expected AND OwnerToken = $owner;";
        command.Parameters.AddWithValue("$next", revision);
        command.Parameters.AddWithValue("$id", snapshotId.Value);
        command.Parameters.AddWithValue("$operation", operationId.Value);
        command.Parameters.AddWithValue("$state", row.State.ToString());
        command.Parameters.AddWithValue("$expected", expectedRevision);
        command.Parameters.AddWithValue("$owner", ownerToken.Value);
        foreach (var parameter in mutation.Parameters)
            command.Parameters.AddWithValue(parameter.Name, parameter.Value);
        if (await command.ExecuteNonQueryAsync(ct) != 1)
            throw new ReviewerOperationSnapshotConcurrencyException("Reviewer snapshot conditional transition lost authority.");
        var result = await ReadRowAsync(connection, transaction, "OperationSnapshotId", snapshotId.Value, ct)
            ?? throw new InvalidDataException("Updated reviewer snapshot row is missing.");
        await transaction.CommitAsync(ct);
        return result;
    }

    public Task<ReviewerOperationSnapshotRecord> BindAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, long expectedRevision, ReviewerSnapshotOwnerToken ownerToken,
        ReviewerSnapshotCandidate candidate, CancellationToken cancellationToken = default)
    {
        ValidateCandidate(candidate);
        return MutateAsync(snapshotId, operationId, expectedRevision, ownerToken, row =>
        {
            RequireCandidate(row, candidate);
            if (row.BundleSha256 is not null) return null;
            RequireActive(row);
            if (row.State != ReviewerOperationSnapshotState.Capturing)
                throw new ReviewerOperationSnapshotConflictException("Bind requires Capturing.");
            return new("State = 'Materializing', BundleSha256 = $hash", ("$hash", candidate.Reference.BundleSha256));
        }, cancellationToken);
    }

    public Task<ReviewerOperationSnapshotRecord> ReadyAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, long expectedRevision, ReviewerSnapshotOwnerToken ownerToken,
        ReviewerSnapshotReadyEvidence evidence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        ValidateCandidate(evidence.Candidate);
        if (evidence.ValidationVersion != ReviewerRetainedValidationContract.Version)
            throw new ReviewerOperationSnapshotConflictException("Unsupported retained validation contract version.");
        return MutateAsync(snapshotId, operationId, expectedRevision, ownerToken, row =>
        {
            RequireCandidate(row, evidence.Candidate);
            if (row.State == ReviewerOperationSnapshotState.Ready && row.ReadyValidationVersion == evidence.ValidationVersion)
                return null; // Actual disposition is preserved, including RequiresReview.
            RequireActive(row);
            if (row.State != ReviewerOperationSnapshotState.Materializing || row.BundleSha256 is null)
                throw new ReviewerOperationSnapshotConflictException("Ready requires the complete bound Materializing candidate.");
            return new("State = 'Ready', ReadyValidationVersion = $validation", ("$validation", evidence.ValidationVersion));
        }, cancellationToken);
    }

    public Task<ReviewerOperationSnapshotRecord> TransferOwnershipAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, long expectedRevision, ReviewerSnapshotOwnerToken ownerToken,
        ReviewerSnapshotOwnerToken replacementToken, CancellationToken cancellationToken = default)
    {
        ArtifactContentIdentity.Validate(replacementToken.Value);
        if (replacementToken == ownerToken)
            throw new ReviewerOperationSnapshotConflictException("Ownership transfer requires a different token.");
        return MutateAsync(snapshotId, operationId, expectedRevision, ownerToken, row =>
        {
            RequireActive(row);
            if (row.State is not (ReviewerOperationSnapshotState.Capturing or ReviewerOperationSnapshotState.Materializing))
                throw new ReviewerOperationSnapshotConflictException("Ownership transfer is illegal for this lifecycle state.");
            // Only the exact immediately observable transfer result is reconcilable.
            if (expectedRevision > 0 && expectedRevision < long.MaxValue &&
                row.Revision == expectedRevision + 1 && row.OwnerToken == replacementToken)
                return null;
            return new("OwnerToken = $replacement", ("$replacement", replacementToken.Value));
        }, cancellationToken);
    }

    public Task<ReviewerOperationSnapshotRecord> RecordReviewOrFailureAsync(OperationSnapshotId snapshotId,
        ReviewerOperationId operationId, ReviewerOperationSnapshotState expectedState,
        long expectedRevision, ReviewerSnapshotOwnerToken ownerToken,
        ReviewerOperationSnapshotFailureCategory category, CancellationToken cancellationToken = default)
    {
        var disposition = category switch
        {
            ReviewerOperationSnapshotFailureCategory.CaptureRecoveryAmbiguous => ReviewerOperationSnapshotDisposition.RequiresReview,
            ReviewerOperationSnapshotFailureCategory.CaptureRejected => ReviewerOperationSnapshotDisposition.Failed,
            ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid => ReviewerOperationSnapshotDisposition.RequiresReview,
            _ => throw new ReviewerOperationSnapshotConflictException("Unknown reviewer snapshot failure category.")
        };
        if ((category == ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid &&
                expectedState is not (ReviewerOperationSnapshotState.Materializing or ReviewerOperationSnapshotState.Ready)) ||
            (category != ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid &&
                expectedState != ReviewerOperationSnapshotState.Capturing))
            throw new ReviewerOperationSnapshotConflictException("Failure category is illegal for the expected lifecycle state.");
        return MutateAsync(snapshotId, operationId, expectedRevision, ownerToken, row =>
        {
            if (row.State != expectedState)
                throw new ReviewerOperationSnapshotConflictException("Failure source lifecycle state does not match.");
            if (row.Disposition == disposition && row.FailureCategory == category) return null;
            RequireActive(row);
            return new("Disposition = $disposition, FailureCategory = $category",
                ("$disposition", disposition.ToString()), ("$category", (int)category));
        }, cancellationToken);
    }
}

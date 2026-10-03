using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Security.Auditing.Models;
using EMF.Security.Storage;
using Microsoft.Data.Sqlite;
namespace EMF.Security.Persistence.Sqlite;

public sealed class SqliteArtifactRewrapJournal : IArtifactRewrapJournal
{
    private readonly string _path;
    public SqliteArtifactRewrapJournal(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath); _path = Path.GetFullPath(databasePath);
    }
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        PrivateSecuritySqliteDatabase.Prepare(_path);
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS RewrapIntents(OperationId TEXT PRIMARY KEY,Payload TEXT NOT NULL,PayloadHash TEXT NOT NULL,State INTEGER NOT NULL); CREATE TABLE IF NOT EXISTS RewrapReview(OperationId TEXT PRIMARY KEY,Payload TEXT NOT NULL);";
        await command.ExecuteNonQueryAsync(ct);
        // SQLite FULL + DELETE synchronizes the journal, database and containing directory at commit.
        using var transaction = connection.BeginTransaction(deferred: false); transaction.Commit();
    }
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _path, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA synchronous=FULL; PRAGMA journal_mode=DELETE;"; command.ExecuteNonQuery(); return connection;
    }
    private static string Encode(ArtifactRewrapIntent intent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.OriginalActorId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.ClassificationId.Value);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.ArtifactId.Value);
        SecurityAuditIdentity.Validate(intent.OperationId.Value); SecurityAuditIdentity.Validate(intent.AuditEventId.Value);
        if (intent.OriginalActorId.Length is < 1 or > 256 || intent.ClassificationId.Value.Length > 128 ||
            intent.PreviousKeyId?.Length > 1024 || intent.CurrentKeyId?.Length > 1024) throw new ArgumentException("Intent exceeds bounds.");
        if (!Enum.IsDefined(intent.State)) throw new ArgumentException("Unknown rewrap state.");
        SecurityAuditIdentity.Validate(intent.ClassificationRevision.Value);
        EMF.Core.Contracts.Storage.ArtifactContentIdentity.Validate(intent.ExpectedRevision.Value);
        if (intent.Event is not null)
        {
            _ = EMF.Security.Auditing.SecurityAuditCanonicalEvent.Encode(intent.Event);
            if (intent.Event.AuditEventId != intent.AuditEventId || intent.Event.OperationId != intent.OperationId ||
                intent.Event.ResourceId != intent.ArtifactId.Value || intent.Event.OriginalActorId != intent.OriginalActorId)
                throw new InvalidDataException("Journal event identity mismatch.");
        }
        var payload = JsonSerializer.Serialize(intent); if (payload.Length > 16384) throw new ArgumentException("Intent exceeds bounds."); return payload;
    }
    private static string Hash(string payload) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
    private static ArtifactRewrapIntent Decode(string payload, string hash)
    {
        if (payload.Length > 16384 || Hash(payload) != hash) throw new InvalidDataException("Rewrap journal integrity failure.");
        try
        {
            var intent = JsonSerializer.Deserialize<ArtifactRewrapIntent>(payload) ?? throw new InvalidDataException("Rewrap intent is missing.");
            _ = Encode(intent); return intent;
        }
        catch (Exception error) when (error is ArgumentException or JsonException or InvalidDataException)
        { throw new InvalidDataException("Malformed rewrap intent.", error); }

    }
    public async Task<ArtifactRewrapIntent?> ReadAsync(SecurityMutationOperationId operationId, CancellationToken cancellationToken = default)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT Payload,PayloadHash,State FROM RewrapIntents WHERE OperationId=$op";
        command.Parameters.AddWithValue("$op", operationId.Value); using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var intent = Decode(reader.GetString(0), reader.GetString(1));
        if ((int)intent.State != reader.GetInt32(2)) throw new InvalidDataException("Journal state index conflicts with intent.");
        return intent;
    }
    public async Task CreateAsync(ArtifactRewrapIntent intent, CancellationToken cancellationToken = default)
    {
        var payload = Encode(intent); using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO RewrapIntents VALUES($op,$payload,$hash,$state)";
        command.Parameters.AddWithValue("$op", intent.OperationId.Value); command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$hash", Hash(payload)); command.Parameters.AddWithValue("$state", (int)intent.State);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task UpdateAsync(ArtifactRewrapIntent expected, ArtifactRewrapIntent updated, CancellationToken cancellationToken = default)
    {
        if (expected.OperationId != updated.OperationId || expected.AuditEventId != updated.AuditEventId || expected.ArtifactId != updated.ArtifactId ||
            expected.OriginalActorId != updated.OriginalActorId || expected.ClassificationId != updated.ClassificationId || expected.ClassificationRevision != updated.ClassificationRevision ||
            expected.ExpectedRevision != updated.ExpectedRevision || expected.AuthorizedUtc != updated.AuthorizedUtc)
            throw new InvalidOperationException("Rewrap intent identity cannot change.");
        if (expected.CandidateHash is not null && expected.CandidateHash != updated.CandidateHash ||
            expected.CurrentKeyId is not null && expected.CurrentKeyId != updated.CurrentKeyId || expected.PreviousKeyId != updated.PreviousKeyId ||
            expected.Receipt is not null && expected.Receipt != updated.Receipt || expected.Event is not null &&
            (updated.Event is null || !EMF.Security.Auditing.SecurityAuditCanonicalEvent.Encode(expected.Event).AsSpan().SequenceEqual(EMF.Security.Auditing.SecurityAuditCanonicalEvent.Encode(updated.Event))))
            throw new InvalidOperationException("Prepared candidate and outcome identity cannot change.");
        var payload = Encode(updated); using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "UPDATE RewrapIntents SET Payload=$payload,PayloadHash=$hash,State=$state WHERE OperationId=$op AND Payload=$expected";
        command.Parameters.AddWithValue("$op", expected.OperationId.Value); command.Parameters.AddWithValue("$expected", Encode(expected));
        command.Parameters.AddWithValue("$payload", payload); command.Parameters.AddWithValue("$hash", Hash(payload)); command.Parameters.AddWithValue("$state", (int)updated.State);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Rewrap journal transition conflicted.");
    }
    public async Task<IReadOnlyList<ArtifactRewrapIntent>> ReadPendingAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT Payload,PayloadHash FROM RewrapIntents WHERE State<>$completed ORDER BY rowid LIMIT $limit";
        command.Parameters.AddWithValue("$completed", (int)ArtifactRewrapState.Completed); command.Parameters.AddWithValue("$limit", limit);
        using var reader = await command.ExecuteReaderAsync(cancellationToken); var result = new List<ArtifactRewrapIntent>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(Decode(reader.GetString(0), reader.GetString(1))); return result;
    }
    public async Task<ArtifactRewrapRecoveryWork?> ReadReviewAsync(SecurityMutationOperationId operationId, CancellationToken cancellationToken = default)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "SELECT Payload FROM RewrapReview WHERE OperationId=$op";
        command.Parameters.AddWithValue("$op", operationId.Value);
        var payload = await command.ExecuteScalarAsync(cancellationToken);
        return payload is string text ? JsonSerializer.Deserialize<ArtifactRewrapRecoveryWork>(text) : null;
    }
    public async Task RecordReviewAsync(ArtifactRewrapRecoveryWork work, CancellationToken cancellationToken = default)
    {
        using var connection = Open(); using var command = connection.CreateCommand(); command.CommandText = "INSERT INTO RewrapReview VALUES($op,$payload) ON CONFLICT(OperationId) DO UPDATE SET Payload=excluded.Payload WHERE json_extract(RewrapReview.Payload,'$.RecoveryEvent') IS NULL";
        command.Parameters.AddWithValue("$op", work.OperationId.Value); command.Parameters.AddWithValue("$payload", JsonSerializer.Serialize(work)); await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<IReadOnlyList<ArtifactRewrapRecoveryWork>> ReadRecoveryWorkAsync(int limit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit)); using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "SELECT Payload FROM RewrapReview ORDER BY rowid LIMIT $limit"; command.Parameters.AddWithValue("$limit", limit);
        using var reader = await command.ExecuteReaderAsync(cancellationToken); var result = new List<ArtifactRewrapRecoveryWork>(); while (await reader.ReadAsync(cancellationToken)) result.Add(JsonSerializer.Deserialize<ArtifactRewrapRecoveryWork>(reader.GetString(0))!); return result;
    }
}

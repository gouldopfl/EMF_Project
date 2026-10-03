using System.Globalization;
using System.Text.Json;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using Microsoft.Data.Sqlite;
namespace EMF.Security.Persistence.Sqlite.Auditing;

internal static class CanonicalSecurityAuditWriter
{
    internal const string Projection = "Id,Operation,ResourceType,ResourceId,SubjectId,PolicyDecision,Destination,Outcome,OccurredUtc,FactsJson,IntegrityVersion,PreviousRecordHash,RecordHash,AuditEventId,OperationId,OriginalActorId,ServiceActorId,RecoveryActorId";
    internal static string? Nullable(SqliteDataReader reader, int i) => reader.IsDBNull(i) ? null : reader.GetString(i);
    internal static SecurityAuditRecord Decode(SqliteDataReader reader)
    {
        var facts = new Dictionary<string, string>(StringComparer.Ordinal);
        using var json = JsonDocument.Parse(reader.GetString(9));
        foreach (var item in json.RootElement.EnumerateObject()) facts.Add(item.Name, item.Value.GetString() ?? throw new ArgumentException("Null fact."));
        var time = reader.GetString(8);
        var occurred = DateTimeOffset.ParseExact(time, "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
        if (SecurityAuditCanonicalEvent.FormatTime(occurred) != time) throw new ArgumentException("Noncanonical audit time.");
        var outcome = Enum.Parse<SecurityAuditOutcome>(reader.GetString(7), false);
        if (outcome.ToString() != reader.GetString(7)) throw new ArgumentException("Noncanonical outcome.");
        AuthorizationDecision? policy = Nullable(reader, 5) is { } text ? Enum.Parse<AuthorizationDecision>(text, false) : null;
        if (policy?.ToString() != Nullable(reader, 5)) throw new ArgumentException("Noncanonical policy.");
        return new SecurityAuditRecord
        {
            AuditEventId = new SecurityAuditEventId(reader.GetString(13)),
            OperationId = Nullable(reader, 14) is { } op ? new SecurityMutationOperationId(op) : null,
            OriginalActorId = Nullable(reader, 15),
            ServiceActorId = Nullable(reader, 16),
            RecoveryActorId = Nullable(reader, 17),
            Operation = reader.GetString(1),
            ResourceType = reader.GetString(2),
            ResourceId = reader.GetString(3),
            SubjectId = reader.GetString(4),
            PolicyDecision = policy,
            Destination = Nullable(reader, 6),
            Outcome = outcome,
            OccurredUtc = occurred,
            Facts = facts
        };
    }
    internal static async Task<VerifiedSecurityAuditEvent?> FindAsync(SqliteConnection connection, SqliteTransaction transaction,
        SecurityAuditEventId id, CancellationToken ct)
    {
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = $"SELECT {Projection} FROM SecurityAuditRecords WHERE AuditEventId=$id";
        command.Parameters.AddWithValue("$id", id.Value);
        await using var reader = await command.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct)) return null;
        var record = Decode(reader);
        return new(record, new(id, reader.GetInt64(0), reader.GetString(12)));
    }
    internal static async Task<SecurityAuditAcknowledgement> AppendAsync(SqliteConnection connection,
        SecurityAuditRecord record, CancellationToken ct)
    {
        var canonical = SecurityAuditCanonicalEvent.Encode(record);
        using var transaction = connection.BeginTransaction(deferred: false);
        var integrity = await SqliteSecurityAuditIntegrityVerifier.VerifyAsync(connection, transaction, ct);
        if (!integrity.IsValid) throw new InvalidOperationException("Canonical audit chain is not verifiable.");
        var existing = await FindAsync(connection, transaction, record.AuditEventId!.Value, ct);
        if (existing is not null)
        {
            if (!canonical.AsSpan().SequenceEqual(SecurityAuditCanonicalEvent.Encode(existing.Record))) throw new SecurityAuditIdentityConflictException();
            transaction.Commit(); return existing.Acknowledgement;
        }
        var hash = SecurityAuditCanonicalEvent.Hash(integrity.ChainHeadHash, canonical);
        await using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO SecurityAuditRecords(Operation,ResourceType,ResourceId,SubjectId,PolicyDecision,Destination,Outcome,OccurredUtc,FactsJson,
                IntegrityVersion,PreviousRecordHash,RecordHash,AuditEventId,OperationId,OriginalActorId,ServiceActorId,RecoveryActorId)
            VALUES($operation,$type,$resource,$subject,$policy,$destination,$outcome,$time,$facts,2,$previous,$hash,$event,$opid,$original,$service,$recovery);
            SELECT last_insert_rowid();
            """;
        void Bind(string name, object? value) => command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        Bind("$operation", record.Operation); Bind("$type", record.ResourceType); Bind("$resource", record.ResourceId); Bind("$subject", record.SubjectId);
        Bind("$policy", record.PolicyDecision?.ToString()); Bind("$destination", record.Destination); Bind("$outcome", record.Outcome.ToString());
        Bind("$time", SecurityAuditCanonicalEvent.FormatTime(record.OccurredUtc)); Bind("$facts", JsonSerializer.Serialize(record.Facts));
        Bind("$previous", integrity.ChainHeadHash); Bind("$hash", hash); Bind("$event", record.AuditEventId.Value.Value); Bind("$opid", record.OperationId?.Value);
        Bind("$original", record.OriginalActorId); Bind("$service", record.ServiceActorId); Bind("$recovery", record.RecoveryActorId);
        var row = (long)(await command.ExecuteScalarAsync(ct))!;
        transaction.Commit(); return new(record.AuditEventId.Value, row, hash);
    }
}

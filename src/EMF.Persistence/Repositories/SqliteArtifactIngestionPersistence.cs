using System.Text.Json;
using System.Text.RegularExpressions;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using Microsoft.Data.Sqlite;
using EMF.Persistence.Storage;

namespace EMF.Persistence.Repositories;

// This database MUST be the evidence metadata database, also used by the governed
// classification adapter. No cross-database classification promotion is supported.
public sealed class SqliteArtifactIngestionPersistence : IArtifactIngestionPersistence
{
    private readonly string _databasePath;
    private readonly IContentStoragePlatform _platform;
    public SqliteArtifactIngestionPersistence(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = Path.GetFullPath(databasePath);
        _platform = ContentStoragePlatform.Select();
        _platform.RequirePlatform();
        for (var path = _databasePath; path is not null; path = Path.GetDirectoryName(path))
            if (new FileInfo(path).LinkTarget is not null) throw new IOException("Ingestion database path contains a symbolic link.");
    }
    private async Task<SqliteConnection> OpenAsync(CancellationToken ct, bool admit = true)
    {
        _platform.ValidatePrivatePermissions(_databasePath);
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false, DefaultTimeout = 5 }.ToString());
        try
        {
            await connection.OpenAsync(ct);
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA synchronous=EXTRA; PRAGMA busy_timeout=5000;";
            await command.ExecuteNonQueryAsync(ct);
            command.CommandText = "PRAGMA journal_mode";
            if (!string.Equals(await command.ExecuteScalarAsync(ct) as string, "delete", StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Ingestion requires DELETE journal durability.");
            command.CommandText = "PRAGMA synchronous";
            if (Convert.ToInt32(await command.ExecuteScalarAsync(ct)) != 3)
                throw new InvalidDataException("Ingestion synchronous durability is unsupported.");
            if (admit) await VerifySchemaAsync(connection, null, ct);
            return connection;
        }
        catch { await connection.DisposeAsync(); throw; }
    }
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        _platform.FlushDirectory(Path.GetDirectoryName(_databasePath)!, verifyFileSystem: true);
        // Existing metadata schema is required; initialization never creates an empty replacement database.
        await using var connection = await OpenAsync(cancellationToken, admit: false);
        using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name IN ('Artifacts','Provenance')";
        if (Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken)) != 2)
            throw new InvalidOperationException("Ingestion requires initialized evidence metadata persistence.");
        command.CommandText = "CREATE TABLE IF NOT EXISTS ArtifactIngestionSchema(Version INTEGER PRIMARY KEY CHECK(Version>0)); SELECT COALESCE(MAX(Version),0) FROM ArtifactIngestionSchema;";
        var version = Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
        if (version > 2) throw new InvalidDataException("Unsupported ingestion schema.");
        if (version == 0)
        {
            command.CommandText = Schema;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await VerifySchemaAsync(connection, transaction, cancellationToken, version == 2 ? 2 : 1);
        if (version < 2)
        {
            command.CommandText = PreparationSchema;
            await command.ExecuteNonQueryAsync(cancellationToken);
            var retained = new List<(string Operation, long Revision, string Draft, string? Candidate, ArtifactIngestionIntent Intent, IngestionOperationBinding Binding)>();
            command.CommandText = "SELECT i.OperationId,i.Revision,d.DraftJson,json_extract(i.IntentJson,'$.CandidateHash'),i.IntentJson,o.BindingJson FROM ArtifactIngestionIntents i JOIN ArtifactIngestionDrafts d ON d.OperationId=i.OperationId JOIN ArtifactIngestionOperations o ON o.OperationId=i.OperationId";
            using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken)) retained.Add((reader.GetString(0), reader.GetInt64(1), reader.GetString(2), reader.IsDBNull(3) ? null : reader.GetString(3), Decode<ArtifactIngestionIntent>(reader.GetString(4)), Decode<IngestionOperationBinding>(reader.GetString(5))));
            foreach (var item in retained)
            {
                command.Parameters.Clear();
                command.CommandText = "INSERT INTO ArtifactIngestionCandidatePreparations VALUES($op,$revision,$draft)";
                command.Parameters.AddWithValue("$op", item.Operation); command.Parameters.AddWithValue("$revision", item.Revision);
                command.Parameters.AddWithValue("$draft", DraftBinding(item.Draft));
                await command.ExecuteNonQueryAsync(cancellationToken);
                if (item.Candidate is not null)
                {
                    command.Parameters.Clear(); command.CommandText = "INSERT INTO ArtifactIngestionCandidates VALUES($op,$candidate,$binding)";
                    command.Parameters.AddWithValue("$op", item.Operation); command.Parameters.AddWithValue("$candidate", item.Candidate);
                    command.Parameters.AddWithValue("$binding", JsonSerializer.Serialize(CandidateBindingFor(item.Intent, item.Binding, DraftBinding(item.Draft))));
                    await command.ExecuteNonQueryAsync(cancellationToken);
                    if (item.Intent.CreateReceipt is { } receipt)
                    {
                        command.Parameters.Clear(); command.CommandText = "INSERT INTO ArtifactIngestionCandidateCreations VALUES($op,$receipt)";
                        command.Parameters.AddWithValue("$op", item.Operation); command.Parameters.AddWithValue("$receipt", JsonSerializer.Serialize(receipt));
                        await command.ExecuteNonQueryAsync(cancellationToken);
                    }
                }
            }
            command.Parameters.Clear(); command.CommandText = "DELETE FROM ArtifactIngestionSchema; INSERT INTO ArtifactIngestionSchema VALUES(2);";
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await VerifySchemaAsync(connection, transaction, cancellationToken);
        transaction.Commit();
    }
    private const string Schema = """
        CREATE TABLE ArtifactIngestionOperations(
            Sequence INTEGER PRIMARY KEY AUTOINCREMENT, OperationId TEXT UNIQUE NOT NULL,
            ArtifactId TEXT UNIQUE NOT NULL, BaselineJson TEXT NOT NULL, BindingJson TEXT NOT NULL);
        CREATE TABLE ArtifactIngestionIntents(
            OperationId TEXT PRIMARY KEY, ArtifactId TEXT UNIQUE NOT NULL, OwnershipToken TEXT NOT NULL,
            Revision INTEGER NOT NULL CHECK(Revision>0), State INTEGER NOT NULL, IntentJson TEXT NOT NULL);
        CREATE TABLE ArtifactIngestionDrafts(OperationId TEXT PRIMARY KEY, DraftJson TEXT NOT NULL);
        CREATE TABLE ArtifactIngestionAdoptions(
            ArtifactId TEXT PRIMARY KEY, OperationId TEXT UNIQUE NOT NULL, OwnershipToken TEXT NOT NULL,
            ProvisionalClassificationRevision TEXT NOT NULL, AdoptedClassificationRevision TEXT NOT NULL,
            AdoptedUtc TEXT NOT NULL);
        CREATE TABLE ArtifactIngestionAuditFacts(EventId TEXT PRIMARY KEY, OperationId TEXT NOT NULL, ObligationJson TEXT NOT NULL);
        CREATE TABLE ArtifactIngestionAudit(EventId TEXT PRIMARY KEY, OperationId TEXT NOT NULL,
            ObligationJson TEXT NOT NULL, Delivered INTEGER NOT NULL DEFAULT 0 CHECK(Delivered IN(0,1)));
        CREATE TABLE ArtifactIngestionCleanupClaims(OperationId TEXT PRIMARY KEY, ClaimJson TEXT NOT NULL);
        CREATE TABLE ArtifactIngestionReviews(OperationId TEXT PRIMARY KEY, Category TEXT NOT NULL, ActorId TEXT NOT NULL, ReviewJson TEXT NULL);
        CREATE TABLE ArtifactIngestionOrphanReceipts(OperationId TEXT PRIMARY KEY, ReceiptJson TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS ArtifactMutationAuthority(
            ArtifactId TEXT PRIMARY KEY, ClassificationId TEXT NOT NULL, ClassificationRevision TEXT NOT NULL,
            IsAdopted INTEGER NOT NULL CHECK(IsAdopted IN(0,1)));
        CREATE TRIGGER ArtifactIngestionAdoptionNoUpdate BEFORE UPDATE ON ArtifactIngestionAdoptions
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion adoption'); END;
        CREATE TRIGGER ArtifactIngestionAdoptionNoDelete BEFORE DELETE ON ArtifactIngestionAdoptions
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion adoption'); END;
        CREATE TRIGGER ArtifactIngestionAdoptionNoReplace BEFORE INSERT ON ArtifactIngestionAdoptions
            WHEN EXISTS(SELECT 1 FROM ArtifactIngestionAdoptions WHERE ArtifactId=NEW.ArtifactId OR OperationId=NEW.OperationId)
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion adoption'); END;
        CREATE TRIGGER ArtifactIngestionOperationNoUpdate BEFORE UPDATE ON ArtifactIngestionOperations
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion operation binding'); END;
        CREATE TRIGGER ArtifactIngestionOperationNoDelete BEFORE DELETE ON ArtifactIngestionOperations
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion operation binding'); END;
        CREATE TRIGGER ArtifactIngestionOperationNoReplace BEFORE INSERT ON ArtifactIngestionOperations
            WHEN EXISTS(SELECT 1 FROM ArtifactIngestionOperations WHERE ArtifactId=NEW.ArtifactId OR OperationId=NEW.OperationId)
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion operation binding'); END;
        CREATE TRIGGER ArtifactIngestionAuditFactNoUpdate BEFORE UPDATE ON ArtifactIngestionAuditFacts
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion audit facts'); END;
        CREATE TRIGGER ArtifactIngestionAuditFactNoDelete BEFORE DELETE ON ArtifactIngestionAuditFacts
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion audit facts'); END;
        CREATE TRIGGER ArtifactIngestionAuditFactNoReplace BEFORE INSERT ON ArtifactIngestionAuditFacts
            WHEN EXISTS(SELECT 1 FROM ArtifactIngestionAuditFacts WHERE EventId=NEW.EventId)
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion audit facts'); END;
        CREATE TRIGGER ArtifactIngestionCleanupClaimNoUpdate BEFORE UPDATE ON ArtifactIngestionCleanupClaims
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion cleanup claim'); END;
        CREATE TRIGGER ArtifactIngestionCleanupClaimNoDelete BEFORE DELETE ON ArtifactIngestionCleanupClaims
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion cleanup claim'); END;
        CREATE TRIGGER ArtifactIngestionCleanupClaimNoReplace BEFORE INSERT ON ArtifactIngestionCleanupClaims
            WHEN EXISTS(SELECT 1 FROM ArtifactIngestionCleanupClaims WHERE OperationId=NEW.OperationId)
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion cleanup claim'); END;
        CREATE TRIGGER ArtifactIngestionReviewNoUpdate BEFORE UPDATE OF ReviewJson ON ArtifactIngestionReviews
            WHEN OLD.ReviewJson IS NOT NULL AND NEW.ReviewJson IS NOT OLD.ReviewJson
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion review event'); END;
        CREATE TRIGGER ArtifactIngestionReviewNoDelete BEFORE DELETE ON ArtifactIngestionReviews
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion review event'); END;
        CREATE TRIGGER ArtifactIngestionReviewNoReplace BEFORE INSERT ON ArtifactIngestionReviews
            WHEN EXISTS(SELECT 1 FROM ArtifactIngestionReviews WHERE OperationId=NEW.OperationId)
            BEGIN SELECT RAISE(ABORT,'Immutable ingestion review event'); END;
        CREATE TRIGGER ArtifactIngestionMetadataInsert BEFORE INSERT ON Artifacts
            WHEN EXISTS(SELECT 1 FROM ArtifactIngestionOperations WHERE ArtifactId=NEW.Id)
            AND NOT EXISTS(SELECT 1 FROM ArtifactIngestionAdoptions WHERE ArtifactId=NEW.Id)
            BEGIN SELECT RAISE(ABORT,'Provisional artifact requires atomic adoption'); END;
        CREATE TRIGGER ArtifactIngestionMetadataUpdate BEFORE UPDATE ON Artifacts
            WHEN EXISTS(SELECT 1 FROM ArtifactIngestionOperations WHERE ArtifactId=NEW.Id)
            AND NOT EXISTS(SELECT 1 FROM ArtifactIngestionAdoptions WHERE ArtifactId=NEW.Id)
            BEGIN SELECT RAISE(ABORT,'Provisional artifact requires atomic adoption'); END;
        CREATE TRIGGER ArtifactIngestionAuthorityAdopt BEFORE UPDATE OF IsAdopted ON ArtifactMutationAuthority
            WHEN NEW.IsAdopted=1 AND EXISTS(SELECT 1 FROM ArtifactIngestionOperations WHERE ArtifactId=NEW.ArtifactId)
            AND NOT EXISTS(SELECT 1 FROM ArtifactIngestionAdoptions WHERE ArtifactId=NEW.ArtifactId)
            BEGIN SELECT RAISE(ABORT,'Provisional classification requires atomic adoption'); END;
        INSERT INTO ArtifactIngestionSchema VALUES(1);
        """;

    private const string PreparationSchema = """
        CREATE TABLE ArtifactIngestionCandidatePreparations(OperationId TEXT PRIMARY KEY, ExpectedRevision INTEGER NOT NULL CHECK(ExpectedRevision>0), DraftHash TEXT NOT NULL CHECK(length(DraftHash)=64));
        CREATE TABLE ArtifactIngestionCandidates(OperationId TEXT PRIMARY KEY, CandidateHash TEXT NOT NULL CHECK(length(CandidateHash)=64), BindingJson TEXT NOT NULL);
        CREATE TABLE ArtifactIngestionCandidateCreations(OperationId TEXT PRIMARY KEY, ReceiptJson TEXT NOT NULL);
        CREATE TRIGGER ArtifactIngestionCandidateCreationNoUpdate BEFORE UPDATE ON ArtifactIngestionCandidateCreations
            BEGIN SELECT RAISE(ABORT,'Immutable candidate creation'); END;
        CREATE TRIGGER ArtifactIngestionCandidateCreationNoDelete BEFORE DELETE ON ArtifactIngestionCandidateCreations
            BEGIN SELECT RAISE(ABORT,'Immutable candidate creation'); END;
        CREATE TRIGGER ArtifactIngestionCandidateCreationNoReplace BEFORE INSERT ON ArtifactIngestionCandidateCreations
            WHEN EXISTS(SELECT 1 FROM ArtifactIngestionCandidateCreations WHERE OperationId=NEW.OperationId)
            BEGIN SELECT RAISE(ABORT,'Immutable candidate creation'); END;
        CREATE TRIGGER ArtifactIngestionCandidateNoUpdate BEFORE UPDATE ON ArtifactIngestionCandidates
            BEGIN SELECT RAISE(ABORT,'Immutable candidate binding'); END;
        CREATE TRIGGER ArtifactIngestionCandidateNoDelete BEFORE DELETE ON ArtifactIngestionCandidates
            BEGIN SELECT RAISE(ABORT,'Immutable candidate binding'); END;
        CREATE TRIGGER ArtifactIngestionCandidateNoReplace BEFORE INSERT ON ArtifactIngestionCandidates
            WHEN EXISTS(SELECT 1 FROM ArtifactIngestionCandidates WHERE OperationId=NEW.OperationId)
            BEGIN SELECT RAISE(ABORT,'Immutable candidate binding'); END;
        CREATE TRIGGER ArtifactIngestionCandidatePreparationNoUpdate BEFORE UPDATE ON ArtifactIngestionCandidatePreparations
            BEGIN SELECT RAISE(ABORT,'Immutable candidate preparation'); END;
        CREATE TRIGGER ArtifactIngestionCandidatePreparationNoDelete BEFORE DELETE ON ArtifactIngestionCandidatePreparations
            BEGIN SELECT RAISE(ABORT,'Immutable candidate preparation'); END;
        CREATE TRIGGER ArtifactIngestionCandidatePreparationNoReplace BEFORE INSERT ON ArtifactIngestionCandidatePreparations
            WHEN EXISTS(SELECT 1 FROM ArtifactIngestionCandidatePreparations WHERE OperationId=NEW.OperationId)
            BEGIN SELECT RAISE(ABORT,'Immutable candidate preparation'); END;
        """;

    public async Task<IDisposable> AcquireExecutionAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default)
    {
        ArtifactContentIdentity.Validate(operationId.Value);
        var root = _databasePath + ".ingestion-execution";
        if (new DirectoryInfo(root).LinkTarget is not null) throw new IOException("Execution coordination contains a symbolic link.");
        _platform.CreatePrivateDirectory(root); _platform.ValidatePrivatePermissions(root);
        var name = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(operationId.Value)));
        return await _platform.AcquireAsync(Path.Combine(root, name), true, cancellationToken);
    }

    // Versioned exact persisted UTF-8 representation binding; never a physical revision.
    private static string DraftBinding(string json) => Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes("EMF.IngestionDraftJson.v1\0" + json)));

    private static IngestionCandidateBinding CandidateBindingFor(ArtifactIngestionIntent intent, IngestionOperationBinding binding, string requestHash)
        => new(intent.OperationId, binding.ParentOperationId, intent.ArtifactId,
            "candidate." + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.ASCII.GetBytes(intent.OperationId.Value))),
            intent.CandidateHash ?? throw new InvalidDataException("Missing candidate identity."), requestHash,
            intent.OwnershipToken, intent.ClassificationId, intent.ClassificationRevision, intent.AuthorizedOperationId,
            intent.CreateEventId, intent.AdoptionEventId, intent.CleanupEventId);

    private static string NormalizeSchema(string sql) => Regex.Replace(
        Regex.Replace(sql.Trim().TrimEnd(';'), "IF NOT EXISTS\\s+", "", RegexOptions.IgnoreCase), "\\s+", "");
    private static async Task VerifySchemaAsync(SqliteConnection connection, SqliteTransaction? transaction, CancellationToken ct, int expectedVersion = 2)
    {
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        try
        {
            command.CommandText = "SELECT COUNT(*),MAX(Version) FROM ArtifactIngestionSchema";
            using (var reader = await command.ExecuteReaderAsync(ct))
                if (!await reader.ReadAsync(ct) || reader.GetInt32(0) != 1 || reader.GetInt32(1) != expectedVersion)
                    throw new InvalidDataException("Ingestion migration ledger is unsupported or damaged.");
            var definitions = Regex.Matches(Schema + (expectedVersion == 2 ? PreparationSchema : ""), @"CREATE TABLE (?:IF NOT EXISTS )?(\w+).*?;|CREATE TRIGGER (\w+).*?END;", RegexOptions.Singleline);
            foreach (Match definition in definitions)
            {
                var name = definition.Groups[1].Success ? definition.Groups[1].Value : definition.Groups[2].Value;
                command.CommandText = "SELECT sql FROM sqlite_master WHERE name=$name";
                command.Parameters.Clear(); command.Parameters.AddWithValue("$name", name);
                var actual = await command.ExecuteScalarAsync(ct) as string;
                if (actual is null || NormalizeSchema(actual) != NormalizeSchema(definition.Value))
                    throw new InvalidDataException("Ingestion persistence schema or invariant protection is damaged.");
            }
        }
        catch (SqliteException e) { throw new InvalidDataException("Ingestion schema admission failed.", e); }
    }

    public async Task<IArtifactIngestionSession> AcquireAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default)
    {
        ArtifactContentIdentity.Validate(operationId.Value);
        var connection = await OpenAsync(cancellationToken);
        SqliteTransaction? transaction = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            transaction = connection.BeginTransaction(deferred: false);
            var session = new Session(connection, transaction, operationId);
            await session.LoadAsync(cancellationToken);
            return session;
        }
        catch { transaction?.Dispose(); await connection.DisposeAsync(); throw; }
    }
    public async Task<IReadOnlyList<IngestionRecoveryWork>> ReadRecoveryWorkAsync(long afterCursor, int limit, CancellationToken cancellationToken = default)
    {
        if (afterCursor < 0 || limit is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(limit));
        await using var connection = await OpenAsync(cancellationToken);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT Sequence,OperationId FROM ArtifactIngestionOperations
            WHERE Sequence>$after ORDER BY Sequence LIMIT $limit
            """;
        command.Parameters.AddWithValue("$after", afterCursor); command.Parameters.AddWithValue("$limit", limit);
        // Terminal/delivered flags cannot exclude independent acknowledgement
        // reconciliation. Hosts cycle this bounded immutable-operation cursor.
        using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<IngestionRecoveryWork>();
        while (await reader.ReadAsync(cancellationToken)) result.Add(new(reader.GetInt64(0), new(reader.GetString(1))));
        return result;
    }
    public async Task<IReadOnlyList<IngestionAuditObligation>> ReadAuditObligationsAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken); using var command = connection.CreateCommand();
        // Delivered flags are bookkeeping hints: callers must reverify canonical acknowledgement.
        command.CommandText = """
            SELECT a.ObligationJson,f.ObligationJson FROM ArtifactIngestionAudit a
            LEFT JOIN ArtifactIngestionAuditFacts f ON f.EventId=a.EventId AND f.OperationId=a.OperationId
            WHERE a.OperationId=$op ORDER BY a.rowid LIMIT 100
            """;
        command.Parameters.AddWithValue("$op", operationId.Value); using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var result = new List<IngestionAuditObligation>();
        while (await reader.ReadAsync(cancellationToken))
        {
            if (reader.IsDBNull(1) || reader.GetString(0) != reader.GetString(1)) throw new InvalidDataException("Audit delivery has no matching immutable event facts.");
            var obligation = Decode<IngestionAuditObligation>(reader.GetString(0));
            if (obligation.OperationId != operationId) throw new InvalidDataException("Audit operation identity conflict.");
            result.Add(obligation);
        }
        return result;
    }
    public async Task<IngestionAuditObligation?> ReadReceiptAuditObligationAsync(ArtifactContentMutationReceipt receipt, CancellationToken cancellationToken = default)
    {
        if (receipt.AuditEventId is null || receipt.AuditObligationVersion != 1)
            throw new InvalidDataException("Receipt audit obligation is unsupported.");
        await using var connection = await OpenAsync(cancellationToken); using var command = connection.CreateCommand();
        command.CommandText = "SELECT ObligationJson FROM ArtifactIngestionAuditFacts WHERE EventId=$event";
        command.Parameters.AddWithValue("$event", receipt.AuditEventId.Value.Value);
        if (await command.ExecuteScalarAsync(cancellationToken) is not string json) return null;
        var obligation = Decode<IngestionAuditObligation>(json);
        var action = receipt.Kind == ArtifactContentMutationKind.Create
            ? receipt.Outcome == ArtifactContentMutationOutcome.Created ? IngestionAuditAction.Created : IngestionAuditAction.CreationRejected
            : receipt.Outcome == ArtifactContentMutationOutcome.Deleted ? IngestionAuditAction.Deleted : IngestionAuditAction.CleanupRejected;
        if (obligation.EventId != receipt.AuditEventId || (obligation.MutationOperationId ?? obligation.OperationId) != receipt.OperationId
            || obligation.ArtifactId != receipt.ArtifactId || obligation.Action != action || obligation.OccurredUtc != receipt.OccurredUtc
            || obligation.SchemaVersion != 1)
            throw new InvalidDataException("Immutable event facts conflict with receipt.");
        return obligation;
    }
    public async Task<IngestionAuditObligation?> ReadReviewAuditObligationAsync(ArtifactContentOperationId operationId, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken); using var command = connection.CreateCommand();
        command.CommandText = "SELECT ReviewJson FROM ArtifactIngestionReviews WHERE OperationId=$op";
        command.Parameters.AddWithValue("$op", operationId.Value);
        if (await command.ExecuteScalarAsync(cancellationToken) is not string json) return null;
        var obligation = Decode<IngestionAuditObligation>(json);
        if (obligation.OperationId != operationId || obligation.Action != IngestionAuditAction.RequiresReview || obligation.SchemaVersion != 1)
            throw new InvalidDataException("Immutable review identity conflict.");
        return obligation;
    }
    public async Task AcknowledgeAuditAsync(IngestionAuditObligation expected, CancellationToken cancellationToken = default)
        => await SetAuditDeliveryAsync(expected, true, cancellationToken);
    public async Task MarkAuditPendingAsync(IngestionAuditObligation expected, CancellationToken cancellationToken = default)
        => await SetAuditDeliveryAsync(expected, false, cancellationToken);
    private async Task SetAuditDeliveryAsync(IngestionAuditObligation expected, bool delivered, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken); using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        command.CommandText = "UPDATE ArtifactIngestionAudit SET Delivered=$delivered WHERE EventId=$id AND OperationId=$op AND ObligationJson=$json";
        command.Parameters.AddWithValue("$delivered", delivered ? 1 : 0);
        command.Parameters.AddWithValue("$id", expected.EventId.Value); command.Parameters.AddWithValue("$op", expected.OperationId.Value);
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(expected));
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidDataException("Ingestion audit obligation conflict.");
        transaction.Commit();
    }
    public async Task RecordOrphanReceiptAsync(ArtifactContentMutationReceipt receipt, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken); using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO ArtifactIngestionOrphanReceipts VALUES($op,$json) ON CONFLICT(OperationId) DO NOTHING";
        command.Parameters.AddWithValue("$op", receipt.OperationId.Value); command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(receipt));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    public async Task<ArtifactContentOperationId?> FindReceiptOperationAsync(ArtifactContentMutationReceipt receipt, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken); using var command = connection.CreateCommand();
        command.CommandText = "SELECT OperationId FROM ArtifactIngestionOperations WHERE OperationId=$op OR json_extract(BaselineJson,'$.CleanupOperationId.Value')=$op LIMIT 1";
        command.Parameters.AddWithValue("$op", receipt.OperationId.Value);
        return await command.ExecuteScalarAsync(cancellationToken) is string op ? new(op) : null;
    }
    public async Task<IngestionRecoveryHealth> ReadRecoveryHealthAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken); using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT (SELECT COUNT(*) FROM ArtifactIngestionIntents WHERE State NOT IN ($completed,$cleaned)),
                (SELECT COUNT(*) FROM ArtifactIngestionAudit WHERE Delivered=0),
                (SELECT COUNT(*) FROM ArtifactIngestionReviews),
                (SELECT COUNT(*) FROM ArtifactIngestionOrphanReceipts)
            """;
        command.Parameters.AddWithValue("$completed", (int)ArtifactIngestionState.Completed);
        command.Parameters.AddWithValue("$cleaned", (int)ArtifactIngestionState.Cleaned);
        using var reader = await command.ExecuteReaderAsync(cancellationToken); await reader.ReadAsync(cancellationToken);
        return new(reader.GetInt64(0), reader.GetInt64(1), reader.GetInt64(2), reader.GetInt64(3), DateTimeOffset.UtcNow);
    }
    private static T Decode<T>(string json)
    {
        try { return JsonSerializer.Deserialize<T>(json) ?? throw new InvalidDataException("Missing ingestion data."); }
        catch (Exception e) when (e is JsonException or ArgumentException) { throw new InvalidDataException("Damaged ingestion data.", e); }
    }

    private sealed class Session(SqliteConnection connection, SqliteTransaction transaction, ArtifactContentOperationId operationId) : IArtifactIngestionSession
    {
        public ArtifactIngestionIntent? Intent { get; private set; }
        public IngestionOperationBinding? OperationBinding { get; private set; }
        public ArtifactId? ProvisionalArtifactId { get; private set; }
        public ArtifactContentOperationId OperationId => operationId;
        public bool IsDamaged { get; private set; }
        public bool HasReview { get; private set; }
        public bool CandidatePreparationStarted { get; private set; }
        public IngestionCandidateBinding? CandidateBinding { get; private set; }
        public ArtifactContentMutationReceipt? CandidateCreationReceipt { get; private set; }
        private ArtifactIngestionIntent? _baseline;
        private bool _committed;
        private SqliteCommand Command(string sql, params (string, object?)[] values)
        {
            var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = sql;
            foreach (var (key, value) in values) command.Parameters.AddWithValue(key, value ?? DBNull.Value);
            return command;
        }
        private async Task<object?> Scalar(string sql, CancellationToken ct, params (string, object?)[] values)
        { using var command = Command(sql, values); return await command.ExecuteScalarAsync(ct); }
        private async Task<int> Execute(string sql, CancellationToken ct, params (string, object?)[] values)
        { using var command = Command(sql, values); return await command.ExecuteNonQueryAsync(ct); }
        public async Task LoadAsync(CancellationToken ct)
        {
            using (var command = Command("SELECT BaselineJson,BindingJson,ArtifactId FROM ArtifactIngestionOperations WHERE OperationId=$op", ("$op", operationId.Value)))
            using (var reader = await command.ExecuteReaderAsync(ct))
                if (await reader.ReadAsync(ct))
                {
                    try
                    {
                        _baseline = Decode<ArtifactIngestionIntent>(reader.GetString(0));
                        OperationBinding = Decode<IngestionOperationBinding>(reader.GetString(1));
                        ProvisionalArtifactId = new(reader.GetString(2));
                        Validate(_baseline, OperationBinding);
                        if (_baseline.ArtifactId != ProvisionalArtifactId || _baseline.OperationId != operationId) throw new InvalidDataException();
                    }
                    catch (InvalidDataException) { IsDamaged = true; }
                }
            using (var command = Command("SELECT IntentJson,Revision,State,ArtifactId,OwnershipToken FROM ArtifactIngestionIntents WHERE OperationId=$op", ("$op", operationId.Value)))
            using (var reader = await command.ExecuteReaderAsync(ct))
                if (await reader.ReadAsync(ct))
                {
                    try
                    {
                        var intent = Decode<ArtifactIngestionIntent>(reader.GetString(0));
                        if (_baseline is null || OperationBinding is null) throw new InvalidDataException();
                        Validate(intent, OperationBinding);
                        if (intent.OperationId != operationId || intent.Revision != reader.GetInt64(1) || (int)intent.State != reader.GetInt32(2)
                            || intent.ArtifactId.Value != reader.GetString(3) || intent.OwnershipToken.Value != reader.GetString(4)
                            || !SameIdentity(intent, _baseline)) throw new InvalidDataException();
                        Intent = intent;
                    }
                    catch (InvalidDataException) { IsDamaged = true; }
                }
            if (_baseline is not null && Intent is null) IsDamaged = true;
            if (ProvisionalArtifactId is null && await Scalar("SELECT ArtifactId FROM ArtifactIngestionAdoptions WHERE OperationId=$op", ct,
                ("$op", operationId.Value)) is string adoptedId)
            {
                ProvisionalArtifactId = new(adoptedId);
                IsDamaged = true;
            }
            if (!IsDamaged && Intent is not null && (Intent.CleanupActorId is not null
                || await Scalar("SELECT 1 FROM ArtifactIngestionCleanupClaims WHERE OperationId=$op", ct, ("$op", operationId.Value)) is not null
                || await Scalar("SELECT 1 FROM ArtifactIngestionAuditFacts WHERE OperationId=$op AND json_extract(ObligationJson,'$.Action')=$action", ct,
                    ("$op", operationId.Value), ("$action", (int)IngestionAuditAction.CleanupClaimed)) is not null))
            {
                // Immutable history cannot be hidden by rewinding mutable state
                // or clearing its actor pointer. Missing claim history is damage.
                try { await ReadCleanupClaimAsync(ct); }
                catch (InvalidDataException) { IsDamaged = true; }
            }
            var preparationRevision = await Scalar("SELECT ExpectedRevision FROM ArtifactIngestionCandidatePreparations WHERE OperationId=$op", ct, ("$op", operationId.Value));
            CandidatePreparationStarted = preparationRevision is not null;
            if (preparationRevision is not null && (Intent is null || Convert.ToInt64(preparationRevision) > Intent.Revision)) IsDamaged = true;
            if (CandidatePreparationStarted)
            {
                var draftJson = await Scalar("SELECT DraftJson FROM ArtifactIngestionDrafts WHERE OperationId=$op", ct, ("$op", operationId.Value)) as string;
                var boundDraft = await Scalar("SELECT DraftHash FROM ArtifactIngestionCandidatePreparations WHERE OperationId=$op", ct, ("$op", operationId.Value)) as string;
                if (draftJson is null || DraftBinding(draftJson) != boundDraft) IsDamaged = true;
            }
            var boundCandidate = await Scalar("SELECT CandidateHash FROM ArtifactIngestionCandidates WHERE OperationId=$op", ct, ("$op", operationId.Value)) as string;
            if (boundCandidate != Intent?.CandidateHash || boundCandidate is not null && !CandidatePreparationStarted) IsDamaged = true;
            try
            {
                var bindingJson = await Scalar("SELECT BindingJson FROM ArtifactIngestionCandidates WHERE OperationId=$op", ct, ("$op", operationId.Value)) as string;
                CandidateBinding = bindingJson is null ? null : Decode<IngestionCandidateBinding>(bindingJson);
                if (boundCandidate is not null)
                {
                    var requestHash = await Scalar("SELECT DraftHash FROM ArtifactIngestionCandidatePreparations WHERE OperationId=$op", ct, ("$op", operationId.Value)) as string;
                    if (Intent is null || OperationBinding is null || requestHash is null || CandidateBinding != CandidateBindingFor(Intent, OperationBinding, requestHash)) IsDamaged = true;
                }
                var creationJson = await Scalar("SELECT ReceiptJson FROM ArtifactIngestionCandidateCreations WHERE OperationId=$op", ct, ("$op", operationId.Value)) as string;
                CandidateCreationReceipt = creationJson is null ? null : Decode<ArtifactContentMutationReceipt>(creationJson);
                if (CandidateCreationReceipt != Intent?.CreateReceipt) IsDamaged = true;
            }
            catch (InvalidDataException) { IsDamaged = true; }
            HasReview = await Scalar("SELECT 1 FROM ArtifactIngestionReviews WHERE OperationId=$op", ct, ("$op", operationId.Value)) is not null;
        }
        private static void Validate(ArtifactIngestionIntent intent, IngestionOperationBinding binding)
        {
            try
            {
                foreach (var value in new[] { intent.OperationId.Value, intent.OwnershipToken.Value, intent.ClassificationId.Value,
                    intent.ClassificationRevision.Value, intent.AuthorizedOperationId.Value, intent.CleanupOperationId.Value,
                    intent.CreateEventId.Value, intent.AdoptionEventId.Value, intent.CleanupEventId.Value, binding.OperationId.Value })
                    ArtifactContentIdentity.Validate(value);
                if (binding.OperationId != intent.AuthorizedOperationId || string.IsNullOrWhiteSpace(binding.OriginalActorId)
                    || new System.Text.UTF8Encoding(false, true).GetByteCount(binding.OriginalActorId) > 256 || binding.OriginalActorId.Any(char.IsControl)
                    || !Enum.IsDefined(intent.State) || !Enum.IsDefined(intent.Disposition) || intent.Revision < 1)
                    throw new ArgumentException();
                _ = new ArtifactId(intent.ArtifactId.Value);
                if (binding.ParentOperationId is { } parent) ArtifactContentIdentity.Validate(parent.Value);
                if (intent.CleanupActorId is { } actor && (string.IsNullOrWhiteSpace(actor)
                    || new System.Text.UTF8Encoding(false, true).GetByteCount(actor) > 256 || actor.Any(char.IsControl))) throw new ArgumentException();
            }
            catch (ArgumentException e) { throw new InvalidDataException("Invalid ingestion binding.", e); }
        }
        private static bool SameIdentity(ArtifactIngestionIntent a, ArtifactIngestionIntent b) =>
            a.OperationId == b.OperationId && a.ArtifactId == b.ArtifactId && a.OwnershipToken == b.OwnershipToken &&
            a.ClassificationId == b.ClassificationId && a.ClassificationRevision == b.ClassificationRevision &&
            a.AuthorizedOperationId == b.AuthorizedOperationId && a.CleanupOperationId == b.CleanupOperationId &&
            a.CreateEventId == b.CreateEventId && a.AdoptionEventId == b.AdoptionEventId && a.CleanupEventId == b.CleanupEventId && a.PreparedUtc == b.PreparedUtc;
        private void Expect(ArtifactIngestionIntent expected)
        {
            if (IsDamaged || Intent != expected || expected.OperationId != operationId) throw new InvalidDataException("Ingestion fence conflict.");
        }
        private async Task Save(ArtifactIngestionIntent expected, ArtifactIngestionIntent updated, CancellationToken ct)
        {
            Expect(expected);
            updated = updated with { Revision = checked(expected.Revision + 1) };
            Validate(updated, OperationBinding ?? throw new InvalidDataException("Missing original operation binding."));
            if (!SameIdentity(expected, updated)) throw new InvalidDataException("Ingestion identity conflict.");
            var count = await Execute("""
                UPDATE ArtifactIngestionIntents SET Revision=$next,State=$state,IntentJson=$json
                WHERE OperationId=$op AND Revision=$expected AND State=$prior AND OwnershipToken=$owner
                """, ct, ("$next", updated.Revision), ("$state", (int)updated.State), ("$json", JsonSerializer.Serialize(updated)),
                ("$op", operationId.Value), ("$expected", expected.Revision), ("$prior", (int)expected.State), ("$owner", expected.OwnershipToken.Value));
            if (count != 1) throw new InvalidDataException("Ingestion fence conflict.");
            Intent = updated;
        }
        public async Task<bool> HasAdoptionEvidenceAsync(ArtifactId artifactId, CancellationToken cancellationToken = default) =>
            Convert.ToInt32(await Scalar("""
                SELECT EXISTS(SELECT 1 FROM ArtifactIngestionAdoptions WHERE ArtifactId=$id)
                OR EXISTS(SELECT 1 FROM Artifacts WHERE Id=$id)
                OR EXISTS(SELECT 1 FROM Provenance WHERE ArtifactId=$id)
                OR EXISTS(SELECT 1 FROM ArtifactMutationAuthority WHERE ArtifactId=$id AND IsAdopted=1)
                """, cancellationToken, ("$id", artifactId.Value))) != 0;
        public async Task<IngestionClassificationAuthority?> ResolveAuthorityAsync(ArtifactId artifactId, CancellationToken cancellationToken = default)
        {
            using var command = Command("SELECT ClassificationId,ClassificationRevision,IsAdopted FROM ArtifactMutationAuthority WHERE ArtifactId=$id", ("$id", artifactId.Value));
            using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) return null;
            var adopted = reader.GetInt32(2) == 1;
            return new(artifactId, new(reader.GetString(0)), new(reader.GetString(1)), adopted,
                !adopted && _baseline?.ArtifactId == artifactId ? _baseline.OwnershipToken : null,
                !adopted && _baseline?.ArtifactId == artifactId ? _baseline.AuthorizedOperationId : null);
        }
        public async Task<IngestionMetadataDraft?> ReadDraftAsync(CancellationToken cancellationToken = default)
        {
            var json = await Scalar("SELECT DraftJson FROM ArtifactIngestionDrafts WHERE OperationId=$op", cancellationToken, ("$op", operationId.Value));
            return json is string value ? Decode<IngestionMetadataDraft>(value) : null;
        }
        private async Task<IngestionMetadataDraft?> ReadArtifact(ArtifactId id, string? source, CancellationToken ct)
        {
            using var command = Command("""
                SELECT a.Id,a.Name,a.ArtifactType,a.CreatedUtc,a.FingerprintAlgorithm,a.FingerprintValue,a.MetadataJson,
                    p.Source,p.RecordedUtc,p.RecordedBy,p.PropertiesJson
                FROM Artifacts a JOIN Provenance p ON p.ArtifactId=a.Id
                WHERE a.Id=$id AND ($source IS NULL OR p.Source=$source) ORDER BY p.Id LIMIT 1
                """, ("$id", id.Value), ("$source", source));
            using var reader = await command.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct)) return null;
            return new(new Artifact { Id = new(reader.GetString(0)), Name = reader.GetString(1), ArtifactType = reader.GetString(2),
                CreatedUtc = DateTimeOffset.Parse(reader.GetString(3)), Fingerprint = reader.IsDBNull(4) ? null : new() { Algorithm = reader.GetString(4), Value = reader.GetString(5) },
                Metadata = Decode<Dictionary<string, object>>(reader.GetString(6)) },
                new Provenance { ArtifactId = id, Source = reader.GetString(7), RecordedUtc = DateTimeOffset.Parse(reader.GetString(8)),
                    RecordedBy = reader.GetString(9), Properties = Decode<Dictionary<string, object>>(reader.GetString(10)) });
        }
        public async Task<IngestionMetadataDraft?> FindCanonicalAsync(CancellationToken cancellationToken = default)
        {
            var draft = await ReadDraftAsync(cancellationToken) ?? throw new InvalidDataException("Missing ingestion metadata draft.");
            var fingerprint = draft.Artifact.Fingerprint ?? throw new InvalidDataException("Missing fingerprint.");
            var id = await Scalar("""
                SELECT a.Id FROM Artifacts a JOIN Provenance p ON p.ArtifactId=a.Id
                WHERE p.Source=$source AND a.FingerprintAlgorithm=$algorithm AND a.FingerprintValue=$value LIMIT 1
                """, cancellationToken, ("$source", draft.Provenance.Source), ("$algorithm", fingerprint.Algorithm), ("$value", fingerprint.Value));
            return id is string value ? await ReadArtifact(new(value), draft.Provenance.Source, cancellationToken) : null;
        }
        public async Task<IngestionMetadataDraft?> ReadResultAsync(CancellationToken cancellationToken = default)
        {
            var intent = Intent ?? _baseline;
            if (intent is null) return null;
            var draft = await ReadDraftAsync(cancellationToken);
            var id = intent.CanonicalArtifactId ?? intent.ArtifactId;
            return await ReadArtifact(id, draft?.Provenance.Source, cancellationToken);
        }
        public async Task PrepareAsync(ArtifactIngestionIntent intent, IngestionOperationBinding binding, IngestionMetadataDraft draft, CancellationToken cancellationToken = default)
        {
            Validate(intent, binding);
            if (IsDamaged || Intent is not null || _baseline is not null || intent.OperationId != operationId || intent.State != ArtifactIngestionState.Prepared
                || intent.Revision != 1 || draft.Artifact.Id != intent.ArtifactId || draft.Provenance.ArtifactId != intent.ArtifactId || draft.Artifact.Fingerprint is null
                || await HasAdoptionEvidenceAsync(intent.ArtifactId, cancellationToken)) throw new InvalidDataException("Invalid ingestion preparation.");
            await Execute("INSERT INTO ArtifactIngestionOperations(OperationId,ArtifactId,BaselineJson,BindingJson) VALUES($op,$id,$json,$binding)", cancellationToken,
                ("$op", operationId.Value), ("$id", intent.ArtifactId.Value), ("$json", JsonSerializer.Serialize(intent)), ("$binding", JsonSerializer.Serialize(binding)));
            await Execute("INSERT INTO ArtifactIngestionIntents VALUES($op,$id,$owner,$revision,$state,$json)", cancellationToken,
                ("$op", operationId.Value), ("$id", intent.ArtifactId.Value), ("$owner", intent.OwnershipToken.Value), ("$revision", 1), ("$state", (int)intent.State), ("$json", JsonSerializer.Serialize(intent)));
            await Execute("INSERT INTO ArtifactIngestionDrafts VALUES($op,$json)", cancellationToken, ("$op", operationId.Value), ("$json", JsonSerializer.Serialize(draft)));
            await Execute("INSERT INTO ArtifactMutationAuthority VALUES($id,$classification,$revision,0)", cancellationToken,
                ("$id", intent.ArtifactId.Value), ("$classification", intent.ClassificationId.Value), ("$revision", intent.ClassificationRevision.Value));
            Intent = _baseline = intent; OperationBinding = binding; ProvisionalArtifactId = intent.ArtifactId;
        }
        public async Task BeginCandidatePreparationAsync(ArtifactIngestionIntent expected, CancellationToken cancellationToken = default)
        {
            Expect(expected);
            if (expected.State != ArtifactIngestionState.Prepared || expected.CandidateHash is not null || CandidatePreparationStarted)
                throw new InvalidDataException("Candidate preparation conflict.");
            var draftJson = await Scalar("SELECT DraftJson FROM ArtifactIngestionDrafts WHERE OperationId=$op", cancellationToken, ("$op", operationId.Value)) as string
                ?? throw new InvalidDataException("Missing candidate request.");
            await Execute("INSERT INTO ArtifactIngestionCandidatePreparations VALUES($op,$revision,$draft)", cancellationToken,
                ("$op", operationId.Value), ("$revision", expected.Revision), ("$draft", DraftBinding(draftJson)));
            CandidatePreparationStarted = true;
        }
        public async Task SetCandidateAsync(ArtifactIngestionIntent expected, string candidateHash, CancellationToken cancellationToken = default)
        {
            Expect(expected);
            if (expected.State != ArtifactIngestionState.Prepared || candidateHash.Length != 64 || candidateHash.Any(c => !char.IsAsciiHexDigit(c))) throw new InvalidDataException("Candidate identity conflict.");
            if (expected.CandidateHash is not null && expected.CandidateHash != candidateHash) throw new ArtifactContentIdempotencyException();
            if (!CandidatePreparationStarted) throw new InvalidDataException("Candidate preparation was not admitted.");
            var prior = await Scalar("SELECT CandidateHash FROM ArtifactIngestionCandidates WHERE OperationId=$op", cancellationToken, ("$op", operationId.Value)) as string;
            if (prior is not null)
            {
                if (prior != candidateHash) throw new ArtifactContentIdempotencyException();
                return; // Recognition only; immutable admission is never updated.
            }
            var requestHash = await Scalar("SELECT DraftHash FROM ArtifactIngestionCandidatePreparations WHERE OperationId=$op", cancellationToken, ("$op", operationId.Value)) as string
                ?? throw new InvalidDataException("Missing candidate request binding.");
            CandidateBinding = CandidateBindingFor(expected with { CandidateHash = candidateHash }, OperationBinding!, requestHash);
            await Execute("INSERT INTO ArtifactIngestionCandidates VALUES($op,$candidate,$binding)", cancellationToken,
                ("$op", operationId.Value), ("$candidate", candidateHash), ("$binding", JsonSerializer.Serialize(CandidateBinding)));
            await Save(expected, expected with { CandidateHash = candidateHash }, cancellationToken);
        }
        private async Task Audit(ArtifactContentAuditEventId eventId, IngestionAuditAction action, string actor, bool recovery, DateTimeOffset time, CancellationToken ct, string? category = null)
        {
            var intent = Intent ?? _baseline;
            if (intent is null || OperationBinding is null) return;
            var classificationRevision = intent.ClassificationRevision;
            if (action == IngestionAuditAction.Adopted)
            {
                var adoptedRevision = await Scalar("SELECT AdoptedClassificationRevision FROM ArtifactIngestionAdoptions WHERE ArtifactId=$id", ct, ("$id", intent.ArtifactId.Value));
                if (adoptedRevision is not string value) throw new InvalidDataException("Missing adopted classification linkage.");
                classificationRevision = new(value);
            }
            var obligation = new IngestionAuditObligation(eventId, operationId, intent.ArtifactId, intent.ClassificationId, classificationRevision,
                OperationBinding.OriginalActorId, actor, recovery, action, time, category,
                action is IngestionAuditAction.Deleted or IngestionAuditAction.CleanupRejected ? intent.CleanupOperationId : null);
            await PersistAudit(obligation, ct);
        }
        private async Task PersistAudit(IngestionAuditObligation obligation, CancellationToken ct)
        {
            var json = JsonSerializer.Serialize(obligation);
            var fact = await Scalar("SELECT ObligationJson FROM ArtifactIngestionAuditFacts WHERE EventId=$id", ct, ("$id", obligation.EventId.Value));
            if (fact is string known && known != json) throw new InvalidDataException("Immutable audit event identity conflict.");
            if (fact is null)
                await Execute("INSERT INTO ArtifactIngestionAuditFacts VALUES($id,$op,$json)", ct,
                    ("$id", obligation.EventId.Value), ("$op", operationId.Value), ("$json", json));
            var prior = await Scalar("SELECT ObligationJson FROM ArtifactIngestionAudit WHERE EventId=$id", ct, ("$id", obligation.EventId.Value));
            if (prior is string old && old != json) throw new InvalidDataException("Audit identity conflict.");
            await Execute("INSERT INTO ArtifactIngestionAudit(EventId,OperationId,ObligationJson) VALUES($id,$op,$json) ON CONFLICT(EventId) DO NOTHING", ct,
                ("$id", obligation.EventId.Value), ("$op", operationId.Value), ("$json", json));
        }
        public async Task RecordCreatedAsync(ArtifactIngestionIntent expected, ArtifactContentMutationReceipt receipt, CancellationToken cancellationToken = default)
        {
            Expect(expected);
            if (expected.State != ArtifactIngestionState.Prepared || receipt.OperationId != operationId || receipt.ArtifactId != expected.ArtifactId
                || receipt.OwnershipToken != expected.OwnershipToken || receipt.Kind != ArtifactContentMutationKind.Create
                || receipt.Outcome != ArtifactContentMutationOutcome.Created || receipt.CurrentRevision is null || receipt.PriorRevision is not null
                || receipt.AuditEventId != expected.CreateEventId || receipt.AuditObligationVersion != 1 || expected.CandidateHash is null)
                throw new InvalidDataException("Creation receipt conflict.");
            await Execute("INSERT INTO ArtifactIngestionCandidateCreations VALUES($op,$receipt)", cancellationToken,
                ("$op", operationId.Value), ("$receipt", JsonSerializer.Serialize(receipt)));
            CandidateCreationReceipt = receipt;
            await Save(expected, expected with { State = ArtifactIngestionState.ContentCreated, CreateReceipt = receipt }, cancellationToken);
            await Audit(expected.CreateEventId, IngestionAuditAction.Created, OperationBinding!.OriginalActorId, false, receipt.OccurredUtc, cancellationToken);
        }
        private async Task CheckAuthority(ArtifactIngestionIntent expected, IngestionClassificationAuthority authority, CancellationToken ct)
        {
            Expect(expected);
            var current = await ResolveAuthorityAsync(expected.ArtifactId, ct);
            if (current != authority || authority.IsAdopted || authority.ArtifactId != expected.ArtifactId || authority.OwnershipToken != expected.OwnershipToken
                || authority.AuthorizedOperationId != expected.AuthorizedOperationId || authority.ClassificationId != expected.ClassificationId
                || authority.Revision != expected.ClassificationRevision) throw new InvalidDataException("Provisional classification binding conflict.");
        }
        private async Task CleanupEligible(ArtifactIngestionIntent expected, CancellationToken ct)
        {
            Expect(expected);
            if (expected.State is not (ArtifactIngestionState.ContentCreated or ArtifactIngestionState.CleanupClaimed)
                || expected.CreateReceipt?.CurrentRevision is null || await HasAdoptionEvidenceAsync(expected.ArtifactId, ct))
                throw new InvalidDataException("Ingestion compensation is forbidden.");
            if (HasReview && (expected.Disposition != ArtifactIngestionDisposition.Conflict
                || expected.SafeFailureCategory != "CanonicalReconciliationFailure"
                || await Scalar("SELECT Category FROM ArtifactIngestionReviews WHERE OperationId=$op", ct, ("$op", operationId.Value)) as string != "CanonicalReconciliationFailure"
                || await Scalar("SELECT json_extract(ReviewJson,'$.SafeFailureCategory') FROM ArtifactIngestionReviews WHERE OperationId=$op", ct,
                    ("$op", operationId.Value)) as string != "CanonicalReconciliationFailure"))
                throw new InvalidDataException("Unresolved lifecycle review prohibits compensation.");
        }
        private async Task<IngestionAuditObligation> ReadCleanupClaimAsync(CancellationToken ct)
        {
            var intent = Intent ?? throw new InvalidDataException("Missing cleanup lifecycle.");
            var json = await Scalar("SELECT ClaimJson FROM ArtifactIngestionCleanupClaims WHERE OperationId=$op", ct, ("$op", operationId.Value)) as string;
            var claim = json is null ? throw new InvalidDataException("Missing cleanup authorization context.") : Decode<IngestionAuditObligation>(json);
            if (claim.OperationId != operationId || claim.ArtifactId != intent.ArtifactId || claim.Action != IngestionAuditAction.CleanupClaimed
                || claim.ClassificationId != intent.ClassificationId || claim.ClassificationRevision != intent.ClassificationRevision
                || claim.OriginalActorId != OperationBinding?.OriginalActorId || claim.ExecutingActorId != intent.CleanupActorId
                || !claim.IsRecovery || claim.SchemaVersion != 1 || claim.SafeFailureCategory is not null || claim.MutationOperationId is not null)
                throw new InvalidDataException("Cleanup authorization context conflict.");
            var fact = await Scalar("SELECT ObligationJson FROM ArtifactIngestionAuditFacts WHERE OperationId=$op AND json_extract(ObligationJson,'$.Action')=$action", ct,
                ("$op", operationId.Value), ("$action", (int)IngestionAuditAction.CleanupClaimed)) as string;
            if (fact is null || Decode<IngestionAuditObligation>(fact) != claim)
                throw new InvalidDataException("Immutable cleanup claim and canonical event facts conflict.");
            return claim;
        }
        private async Task PersistCleanupClaimAsync(string actor, CancellationToken ct)
        {
            var intent = Intent ?? throw new InvalidDataException("Missing cleanup lifecycle.");
            var claim = new IngestionAuditObligation(new("ingestion.claim." + Guid.NewGuid().ToString("N")), operationId, intent.ArtifactId,
                intent.ClassificationId, intent.ClassificationRevision, OperationBinding!.OriginalActorId, actor, true,
                IngestionAuditAction.CleanupClaimed, DateTimeOffset.UtcNow);
            await Execute("INSERT INTO ArtifactIngestionCleanupClaims VALUES($op,$json)", ct,
                ("$op", operationId.Value), ("$json", JsonSerializer.Serialize(claim)));
            await PersistAudit(claim, ct);
        }
        public async Task ClaimCleanupAsync(ArtifactIngestionIntent expected, IngestionClassificationAuthority authority, string recoveryActorId, CancellationToken cancellationToken = default)
        {
            await CleanupEligible(expected, cancellationToken); await CheckAuthority(expected, authority, cancellationToken);
            if (expected.State == ArtifactIngestionState.CleanupClaimed)
            {
                var claim = await ReadCleanupClaimAsync(cancellationToken);
                if (claim.ExecutingActorId != recoveryActorId) throw new InvalidDataException("Cleanup service identity changed.");
                await PersistAudit(claim, cancellationToken);
                return; // caller still reauthorizes while holding this fence
            }
            await Save(expected, expected with { State = ArtifactIngestionState.CleanupClaimed, CleanupActorId = recoveryActorId }, cancellationToken);
            await PersistCleanupClaimAsync(recoveryActorId, cancellationToken);
        }
        public async Task RecordDeduplicationConflictAsync(ArtifactIngestionIntent expected, IngestionClassificationAuthority authority,
            ArtifactId canonicalArtifactId, string? cleanupActorId, CancellationToken cancellationToken = default)
        {
            await CleanupEligible(expected, cancellationToken); await CheckAuthority(expected, authority, cancellationToken);
            if (expected.State != ArtifactIngestionState.ContentCreated) throw new InvalidDataException("Deduplication fence conflict.");
            var canonical = await FindCanonicalAsync(cancellationToken);
            if (canonical?.Artifact.Id != canonicalArtifactId) throw new InvalidDataException("Canonical identity conflict.");
            var time = DateTimeOffset.UtcNow;
            await Save(expected, expected with { State = cleanupActorId is null ? ArtifactIngestionState.RequiresReview : ArtifactIngestionState.CleanupClaimed,
                Disposition = ArtifactIngestionDisposition.Conflict, CanonicalArtifactId = canonicalArtifactId, CleanupActorId = cleanupActorId,
                SafeFailureCategory = "CanonicalReconciliationFailure", AdoptionUtc = time }, cancellationToken);
            await Execute("INSERT INTO ArtifactIngestionReviews(OperationId,Category,ActorId) VALUES($op,$category,$actor)", cancellationToken,
                ("$op", operationId.Value), ("$category", "CanonicalReconciliationFailure"), ("$actor", OperationBinding!.OriginalActorId));
            HasReview = true;
            await Audit(expected.AdoptionEventId, IngestionAuditAction.RequiresReview, OperationBinding.OriginalActorId, false, time,
                cancellationToken, "CanonicalReconciliationFailure");
            await Execute("UPDATE ArtifactIngestionReviews SET ReviewJson=(SELECT ObligationJson FROM ArtifactIngestionAudit WHERE EventId=$event) WHERE OperationId=$op AND ReviewJson IS NULL",
                cancellationToken, ("$event", expected.AdoptionEventId.Value), ("$op", operationId.Value));
            if (cleanupActorId is not null)
                await PersistCleanupClaimAsync(cleanupActorId, cancellationToken);
        }
        public async Task AdoptAsync(ArtifactIngestionIntent expected, IngestionClassificationAuthority provisional,
            IngestionClassificationAuthority? canonical, string? cleanupActorId, CancellationToken cancellationToken = default)
        {
            Expect(expected); await CheckAuthority(expected, provisional, cancellationToken);
            if (expected.State != ArtifactIngestionState.ContentCreated || expected.CreateReceipt?.CurrentRevision is null
                || await HasAdoptionEvidenceAsync(expected.ArtifactId, cancellationToken)
                || await Scalar("SELECT 1 FROM ArtifactIngestionCleanupClaims WHERE OperationId=$op", cancellationToken, ("$op", operationId.Value)) is not null)
                throw new InvalidDataException("Adoption fence conflict.");
            var draft = await ReadDraftAsync(cancellationToken) ?? throw new InvalidDataException("Missing metadata draft.");
            var existing = await FindCanonicalAsync(cancellationToken);
            if (existing is not null)
            {
                if (canonical is null || canonical.ArtifactId != existing.Artifact.Id || !canonical.IsAdopted
                    || canonical.ClassificationId != provisional.ClassificationId || canonical != await ResolveAuthorityAsync(canonical.ArtifactId, cancellationToken)
                    || cleanupActorId is null) throw new InvalidDataException("Canonical adoption/classification conflict.");
                var dedupTime = DateTimeOffset.UtcNow;
                await Save(expected, expected with { State = ArtifactIngestionState.CleanupClaimed, Disposition = ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact,
                    CanonicalArtifactId = canonical.ArtifactId, CleanupActorId = cleanupActorId, AdoptionUtc = dedupTime }, cancellationToken);
                await Audit(expected.AdoptionEventId, IngestionAuditAction.Deduplicated, OperationBinding!.OriginalActorId, false, dedupTime, cancellationToken);
                await PersistCleanupClaimAsync(cleanupActorId, cancellationToken);
                return;
            }
            if (canonical is not null) throw new InvalidDataException("Canonical fence conflict.");
            var adoptedRevision = IngestionClassificationRevision.New(); var time = DateTimeOffset.UtcNow;
            await Execute("INSERT INTO ArtifactIngestionAdoptions VALUES($id,$op,$owner,$prior,$next,$time)", cancellationToken,
                ("$id", expected.ArtifactId.Value), ("$op", operationId.Value), ("$owner", expected.OwnershipToken.Value),
                ("$prior", provisional.Revision.Value), ("$next", adoptedRevision.Value), ("$time", time.ToString("O")));
            if (await Execute("UPDATE ArtifactMutationAuthority SET ClassificationRevision=$next,IsAdopted=1 WHERE ArtifactId=$id AND ClassificationRevision=$prior AND IsAdopted=0", cancellationToken,
                ("$next", adoptedRevision.Value), ("$id", expected.ArtifactId.Value), ("$prior", provisional.Revision.Value)) != 1) throw new InvalidDataException("Classification adoption conflict.");
            await Execute("""
                INSERT INTO Artifacts(Id,Name,ArtifactType,CreatedUtc,FingerprintAlgorithm,FingerprintValue,MetadataJson)
                VALUES($id,$name,$type,$time,$algorithm,$fingerprint,$metadata)
                """, cancellationToken, ("$id", draft.Artifact.Id.Value), ("$name", draft.Artifact.Name), ("$type", draft.Artifact.ArtifactType),
                ("$time", draft.Artifact.CreatedUtc.ToString("O")), ("$algorithm", draft.Artifact.Fingerprint!.Algorithm), ("$fingerprint", draft.Artifact.Fingerprint.Value),
                ("$metadata", JsonSerializer.Serialize(draft.Artifact.Metadata)));
            await Execute("INSERT INTO Provenance(ArtifactId,Source,RecordedUtc,RecordedBy,PropertiesJson) VALUES($id,$source,$time,$actor,$properties)", cancellationToken,
                ("$id", draft.Provenance.ArtifactId.Value), ("$source", draft.Provenance.Source), ("$time", draft.Provenance.RecordedUtc.ToString("O")),
                ("$actor", draft.Provenance.RecordedBy), ("$properties", JsonSerializer.Serialize(draft.Provenance.Properties)));
            await Save(expected, expected with { State = ArtifactIngestionState.MetadataCommitted, Disposition = ArtifactIngestionDisposition.ProvisionalArtifactAdopted,
                CanonicalArtifactId = expected.ArtifactId, AdoptionUtc = time }, cancellationToken);
            await Audit(expected.AdoptionEventId, IngestionAuditAction.Adopted, OperationBinding!.OriginalActorId, false, time, cancellationToken);
        }
        public async Task RecordCleanedAsync(ArtifactIngestionIntent expected, ArtifactContentMutationReceipt? receipt, string recoveryActorId, CancellationToken cancellationToken = default)
        {
            Expect(expected);
            if (await HasAdoptionEvidenceAsync(expected.ArtifactId, cancellationToken)) throw new InvalidDataException("Adopted content cannot be cleaned.");
            if (receipt is null)
            {
                if (expected.State != ArtifactIngestionState.Prepared || expected.CandidateHash is not null || expected.CreateReceipt is not null)
                    throw new InvalidDataException("Creation outcome is unresolved.");
            }
            else if (expected.State != ArtifactIngestionState.CleanupClaimed || receipt.OperationId != expected.CleanupOperationId
                || receipt.ArtifactId != expected.ArtifactId || receipt.OwnershipToken != expected.OwnershipToken || receipt.Kind != ArtifactContentMutationKind.Delete
                || receipt.Outcome != ArtifactContentMutationOutcome.Deleted || receipt.PriorRevision != expected.CreateReceipt?.CurrentRevision
                || receipt.AuditEventId != expected.CleanupEventId || receipt.AuditObligationVersion != 1 || receipt.CurrentRevision is null)
                throw new InvalidDataException("Cleanup receipt conflict.");
            if (receipt is not null)
            {
                var claim = await ReadCleanupClaimAsync(cancellationToken);
                if (claim.ExecutingActorId != recoveryActorId) throw new InvalidDataException("Cleanup service identity changed.");
            }
            await Save(expected, expected with { State = ArtifactIngestionState.Cleaned, CleanupReceipt = receipt }, cancellationToken);
            await Audit(expected.CleanupEventId, receipt is null ? IngestionAuditAction.NoContent : IngestionAuditAction.Deleted,
                expected.CleanupActorId ?? recoveryActorId, true, receipt?.OccurredUtc ?? DateTimeOffset.UtcNow, cancellationToken);
        }
        public async Task RecordReviewAsync(string safeFailureCategory, string executingActorId, CancellationToken cancellationToken = default, bool isRecovery = true)
        {
            if (!Enum.TryParse<IngestionFailureCategory>(safeFailureCategory, false, out var category) || !Enum.IsDefined(category) || category.ToString() != safeFailureCategory)
                throw new ArgumentException("Unapproved ingestion review condition.");
            var exists = await Scalar("SELECT 1 FROM ArtifactIngestionReviews WHERE OperationId=$op", cancellationToken, ("$op", operationId.Value)) is not null;
            await Execute(exists ? "UPDATE ArtifactIngestionReviews SET Category=$category,ActorId=$actor WHERE OperationId=$op"
                : "INSERT INTO ArtifactIngestionReviews(OperationId,Category,ActorId) VALUES($op,$category,$actor)", cancellationToken,
                ("$op", operationId.Value), ("$category", safeFailureCategory), ("$actor", executingActorId));
            HasReview = true;
            var prior = Intent;
            if (prior is not null && !IsDamaged && prior.State != ArtifactIngestionState.RequiresReview)
                await Save(prior, prior with { State = ArtifactIngestionState.RequiresReview, SafeFailureCategory = safeFailureCategory }, cancellationToken);
            var reviewJson = await Scalar("SELECT ReviewJson FROM ArtifactIngestionReviews WHERE OperationId=$op", cancellationToken, ("$op", operationId.Value));
            if (reviewJson is string json)
            {
                var obligation = Decode<IngestionAuditObligation>(json);
                if (obligation.OperationId != operationId || obligation.ArtifactId != ProvisionalArtifactId || obligation.Action != IngestionAuditAction.RequiresReview)
                    throw new InvalidDataException("Review event identity conflict.");
                await PersistAudit(obligation, cancellationToken);
            }
            else if ((Intent ?? _baseline) is not null && OperationBinding is not null)
            {
                var eventId = new ArtifactContentAuditEventId("ingestion.review." + Guid.NewGuid().ToString("N"));
                await Audit(eventId, IngestionAuditAction.RequiresReview, executingActorId,
                    isRecovery, DateTimeOffset.UtcNow, cancellationToken, safeFailureCategory);
                await Execute("UPDATE ArtifactIngestionReviews SET ReviewJson=(SELECT ObligationJson FROM ArtifactIngestionAudit WHERE EventId=$event) WHERE OperationId=$op",
                    cancellationToken, ("$event", eventId.Value), ("$op", operationId.Value));
            }
        }
        public async Task EnsureReceiptAuditAsync(ArtifactContentMutationReceipt receipt, CancellationToken cancellationToken = default)
        {
            if (IsDamaged || Intent is null || OperationBinding is null || receipt.ArtifactId != Intent.ArtifactId || receipt.AuditObligationVersion != 1)
                throw new InvalidDataException("Receipt audit binding is unavailable.");
            if (receipt.OperationId == operationId && receipt.Kind == ArtifactContentMutationKind.Create && receipt.AuditEventId == Intent.CreateEventId)
                await Audit(Intent.CreateEventId, receipt.Outcome == ArtifactContentMutationOutcome.Created ? IngestionAuditAction.Created : IngestionAuditAction.CreationRejected,
                    OperationBinding.OriginalActorId, false, receipt.OccurredUtc, cancellationToken);
            else if (receipt.OperationId == Intent.CleanupOperationId && receipt.Kind == ArtifactContentMutationKind.Delete && receipt.AuditEventId == Intent.CleanupEventId && Intent.CleanupActorId is not null)
            {
                var claim = await ReadCleanupClaimAsync(cancellationToken);
                await PersistAudit(claim, cancellationToken);
                await Audit(Intent.CleanupEventId, receipt.Outcome == ArtifactContentMutationOutcome.Deleted ? IngestionAuditAction.Deleted : IngestionAuditAction.CleanupRejected,
                    claim.ExecutingActorId, true, receipt.OccurredUtc, cancellationToken);
            }
            else throw new InvalidDataException("Receipt audit identity conflict.");
        }
        public async Task EnsureAdoptionAuditAsync(CancellationToken cancellationToken = default)
        {
            if (IsDamaged || Intent is null || OperationBinding is null) throw new InvalidDataException("Adoption audit binding is unavailable.");
            if (Intent.CleanupActorId is not null) await PersistAudit(await ReadCleanupClaimAsync(cancellationToken), cancellationToken);
            if (Intent.Disposition == ArtifactIngestionDisposition.None)
            { await RestoreAuditFactsAsync(cancellationToken); return; }
            var time = Intent.AdoptionUtc ?? throw new InvalidDataException("Adoption audit time is missing.");
            var action = Intent.Disposition switch
            {
                ArtifactIngestionDisposition.ProvisionalArtifactAdopted => IngestionAuditAction.Adopted,
                ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact => IngestionAuditAction.Deduplicated,
                ArtifactIngestionDisposition.Conflict => IngestionAuditAction.RequiresReview,
                _ => throw new InvalidDataException("Unknown adoption disposition.")
            };
            if (Intent.Disposition == ArtifactIngestionDisposition.ProvisionalArtifactAdopted)
            {
                var persistedTime = await Scalar("SELECT AdoptedUtc FROM ArtifactIngestionAdoptions WHERE ArtifactId=$id AND OperationId=$op AND OwnershipToken=$owner AND ProvisionalClassificationRevision=$revision",
                    cancellationToken, ("$id", Intent.ArtifactId.Value), ("$op", operationId.Value), ("$owner", Intent.OwnershipToken.Value), ("$revision", Intent.ClassificationRevision.Value));
                if (persistedTime is not string value || DateTimeOffset.Parse(value) != time) throw new InvalidDataException("Immutable adoption evidence conflict.");
            }
            await Audit(Intent.AdoptionEventId, action, OperationBinding.OriginalActorId, false, time, cancellationToken,
                action == IngestionAuditAction.RequiresReview ? "CanonicalReconciliationFailure" : null);
            if (Intent.RecoveryAuditObligation is { } recovery) await PersistAudit(recovery, cancellationToken);
            await RestoreAuditFactsAsync(cancellationToken);
        }
        private async Task RestoreAuditFactsAsync(CancellationToken cancellationToken)
        {
            // Outbox rows are replaceable. Immutable ingestion event facts retain
            // exact identities/times for non-physical lifecycle decisions too.
            var facts = new List<IngestionAuditObligation>();
            using (var command = Command("SELECT ObligationJson FROM ArtifactIngestionAuditFacts WHERE OperationId=$op", ("$op", operationId.Value)))
            using (var reader = await command.ExecuteReaderAsync(cancellationToken))
                while (await reader.ReadAsync(cancellationToken)) facts.Add(Decode<IngestionAuditObligation>(reader.GetString(0)));
            foreach (var fact in facts) await PersistAudit(fact, cancellationToken);
        }
        public async Task RecordRecoveryCompletionAsync(string recoveryActorId, CancellationToken cancellationToken = default)
        {
            var expected = Intent ?? throw new InvalidDataException("Missing recovery lifecycle."); Expect(expected);
            var authority = await ResolveAuthorityAsync(expected.ArtifactId, cancellationToken);
            if (expected.State is not (ArtifactIngestionState.MetadataCommitted or ArtifactIngestionState.Completed) || authority is not { IsAdopted: true }
                || !await HasAdoptionEvidenceAsync(expected.ArtifactId, cancellationToken) || OperationBinding is null)
                throw new InvalidDataException("Recovery completion lacks adoption authority.");
            var persisted = await Scalar("SELECT ObligationJson FROM ArtifactIngestionAuditFacts WHERE OperationId=$op AND json_extract(ObligationJson,'$.Action')=$action", cancellationToken,
                ("$op", operationId.Value), ("$action", (int)IngestionAuditAction.Reconciled)) as string;
            var durable = persisted is null ? null : Decode<IngestionAuditObligation>(persisted);
            var obligation = expected.RecoveryAuditObligation;
            if (durable is not null && obligation != durable) throw new InvalidDataException("Recovery audit identity conflict.");
            if (obligation is not null && durable is null) throw new InvalidDataException("Missing immutable recovery audit facts.");
            if (obligation is null)
            {
                obligation = new(new("ingestion.recover." + Guid.NewGuid().ToString("N")), operationId, expected.ArtifactId,
                    authority.ClassificationId, authority.Revision, OperationBinding.OriginalActorId, recoveryActorId,
                    true, IngestionAuditAction.Reconciled, DateTimeOffset.UtcNow);
                await Save(expected, expected with { RecoveryAuditObligation = obligation }, cancellationToken);
            }
            await PersistAudit(obligation, cancellationToken);
        }
        public async Task CompleteAsync(ArtifactIngestionIntent expected, CancellationToken cancellationToken = default)
        {
            Expect(expected);
            if (expected.State != ArtifactIngestionState.MetadataCommitted || !await HasAdoptionEvidenceAsync(expected.ArtifactId, cancellationToken)
                || await Scalar("SELECT 1 FROM ArtifactIngestionAdoptions WHERE ArtifactId=$id AND OperationId=$op AND OwnershipToken=$owner", cancellationToken,
                    ("$id", expected.ArtifactId.Value), ("$op", operationId.Value), ("$owner", expected.OwnershipToken.Value)) is null
                || HasReview
                || Convert.ToInt32(await Scalar("SELECT COUNT(*) FROM ArtifactIngestionAudit WHERE OperationId=$op AND Delivered=0", cancellationToken, ("$op", operationId.Value))) != 0)
                throw new InvalidDataException("Ingestion completion is not established.");
            await Save(expected, expected with { State = ArtifactIngestionState.Completed }, cancellationToken);
        }
        public async Task ReopenAuditDeliveryAsync(ArtifactIngestionIntent expected, CancellationToken cancellationToken = default)
        {
            Expect(expected);
            if (expected.State != ArtifactIngestionState.Completed || !await HasAdoptionEvidenceAsync(expected.ArtifactId, cancellationToken))
                throw new InvalidDataException("Only adopted audit bookkeeping can be reopened.");
            await Save(expected, expected with { State = ArtifactIngestionState.MetadataCommitted }, cancellationToken);
        }
        public Task CommitAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested(); transaction.Commit(); _committed = true; return Task.CompletedTask;
        }
        public ValueTask DisposeAsync()
        {
            if (!_committed) transaction.Rollback(); transaction.Dispose(); connection.Dispose(); return ValueTask.CompletedTask;
        }
    }
}

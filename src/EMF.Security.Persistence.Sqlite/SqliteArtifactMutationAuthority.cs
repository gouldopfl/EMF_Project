using EMF.Core.Models.Identities;
using EMF.Security.Models.Identities;
using EMF.Security.Storage;
using Microsoft.Data.Sqlite;
namespace EMF.Security.Persistence.Sqlite;

// Trusted classification/adoption persistence, separate from caller requests.
// Participating classification/adoption writers use SetAsync; the lease holds
// the same serialized database gate through authorization and physical promotion.
public sealed class SqliteArtifactMutationAuthority : IArtifactMutationAuthority
{
    private readonly string _databasePath;
    public SqliteArtifactMutationAuthority(string databasePath)
    { ArgumentException.ThrowIfNullOrWhiteSpace(databasePath); _databasePath = Path.GetFullPath(databasePath); }
    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _databasePath, Mode = SqliteOpenMode.ReadWrite, Pooling = false }.ToString());
        connection.Open(); using var command = connection.CreateCommand(); command.CommandText = "PRAGMA synchronous=FULL;"; command.ExecuteNonQuery(); return connection;
    }
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        PrivateSecuritySqliteDatabase.Prepare(_databasePath);
        using var connection = Open(); using var command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE IF NOT EXISTS ArtifactMutationAuthority(ArtifactId TEXT PRIMARY KEY, ClassificationId TEXT NOT NULL, ClassificationRevision TEXT NOT NULL, IsAdopted INTEGER NOT NULL CHECK(IsAdopted IN(0,1)));";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
    // Administrative repository capability. Composition exposes it only to the
    // governed classification/adoption coordinator, never to the rewrap caller.
    public async Task<ArtifactClassificationRevision> SetAsync(ArtifactId artifactId, ProtectionClassificationId classification,
        bool isAdopted, ArtifactClassificationRevision? expectedRevision = null, CancellationToken cancellationToken = default)
    {
        using var connection = Open(); using var transaction = connection.BeginTransaction(deferred: false);
        using var command = connection.CreateCommand(); command.Transaction = transaction;
        var revision = new ArtifactClassificationRevision(Guid.NewGuid().ToString("N"));
        command.CommandText = expectedRevision is null
            ? "INSERT INTO ArtifactMutationAuthority VALUES($id,$classification,$revision,$adopted)"
            : "UPDATE ArtifactMutationAuthority SET ClassificationId=$classification,ClassificationRevision=$revision,IsAdopted=$adopted WHERE ArtifactId=$id AND ClassificationRevision=$expected";
        command.Parameters.AddWithValue("$id", artifactId.Value); command.Parameters.AddWithValue("$classification", classification.Value);
        command.Parameters.AddWithValue("$revision", revision.Value); command.Parameters.AddWithValue("$adopted", isAdopted ? 1 : 0);
        if (expectedRevision is not null) command.Parameters.AddWithValue("$expected", expectedRevision.Value.Value);
        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1) throw new InvalidOperationException("Classification/adoption revision conflict.");
        transaction.Commit(); return revision;
    }
    public async Task<IArtifactMutationAuthorityLease> AcquireAsync(ArtifactId artifactId, CancellationToken cancellationToken = default)
    {
        var connection = Open(); SqliteTransaction? transaction = null;
        try
        {
            cancellationToken.ThrowIfCancellationRequested(); transaction = connection.BeginTransaction(deferred: false);
            using var command = connection.CreateCommand(); command.Transaction = transaction; command.CommandText = "SELECT ClassificationId,ClassificationRevision,IsAdopted FROM ArtifactMutationAuthority WHERE ArtifactId=$id";
            command.Parameters.AddWithValue("$id", artifactId.Value); using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken)) throw new UnauthorizedAccessException("Artifact classification authority is unknown.");
            return new Lease(connection, transaction, artifactId, new(reader.GetString(0)), new(reader.GetString(1)), reader.GetInt32(2) == 1);
        }
        catch { transaction?.Dispose(); connection.Dispose(); throw; }
    }
    private sealed class Lease(SqliteConnection connection, SqliteTransaction transaction, ArtifactId id,
        ProtectionClassificationId classification, ArtifactClassificationRevision revision, bool adopted) : IArtifactMutationAuthorityLease
    {
        public ArtifactId ArtifactId => id;
        public ProtectionClassificationId ClassificationId => classification;
        public ArtifactClassificationRevision ClassificationRevision => revision;
        public bool IsAdopted => adopted;
        public ValueTask DisposeAsync() { transaction.Dispose(); connection.Dispose(); return ValueTask.CompletedTask; }
    }
}

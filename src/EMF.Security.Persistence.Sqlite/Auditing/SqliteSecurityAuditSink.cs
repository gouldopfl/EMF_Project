using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using Microsoft.Data.Sqlite;

namespace EMF.Security.Persistence.Sqlite.Auditing;

public sealed class SqliteSecurityAuditSink :
    ISecurityAuditSink
{
    private readonly string _databasePath;

    public SqliteSecurityAuditSink(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection()
    {
        var builder =
            new SqliteConnectionStringBuilder
            {
                DataSource = _databasePath
            };

        return new SqliteConnection(
            builder.ToString());
    }

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        return new SecurityAuditSqliteSchema(
            _databasePath)
            .InitializeAsync(cancellationToken);
    }

    public async Task WriteAsync(
        SecurityAuditRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            record.Operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            record.ResourceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            record.ResourceId);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            record.SubjectId);
        ArgumentNullException.ThrowIfNull(record.Facts);

        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        await SecurityAuditHashChainWriter.WriteAsync(
            connection,
            record,
            cancellationToken);
    }
}

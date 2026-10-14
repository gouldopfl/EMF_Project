using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using Microsoft.Data.Sqlite;

namespace EMF.Security.Persistence.Sqlite.Auditing;

public sealed class SqliteSecurityAuditSink :
    IZipGuardedAcknowledgedSecurityAuditSink
{
    private readonly string _databasePath;

    public SqliteSecurityAuditSink(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        EMF.Infrastructure.CandidateCustodyPathExclusion.RejectSqlite(databasePath);
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

        if (record.AuditEventId is not null) record = SecurityAuditCanonicalEvent.Freeze(record);
        await using var connection =
            CreateConnection();

        await connection.OpenAsync(
            cancellationToken);

        if (record.AuditEventId is not null)
        {
            await CanonicalSecurityAuditWriter.AppendAsync(connection, record, cancellationToken);
            return;
        }
        await SecurityAuditHashChainWriter.WriteAsync(
            connection,
            record,
            cancellationToken);
    }
    public async Task<SecurityAuditAcknowledgement> AppendAsync(SecurityAuditRecord record, CancellationToken cancellationToken = default)
    {
        record = SecurityAuditCanonicalEvent.Freeze(record);
        await using var connection = CreateConnection(); await connection.OpenAsync(cancellationToken);
        return await CanonicalSecurityAuditWriter.AppendAsync(connection, record, cancellationToken);
    }
    public async Task<SecurityAuditAcknowledgement> AppendGuardedAsync(SecurityAuditRecord record,
        EMF.Core.Contracts.Zip.IZipEffectBoundaryGuard guard, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, guard.Invalidated);
        await guard.RevalidateAsync(linked.Token);
        record = SecurityAuditCanonicalEvent.Freeze(record);
        await using var connection = CreateConnection();
        await connection.OpenAsync(linked.Token);
        return await CanonicalSecurityAuditWriter.AppendAsync(connection, record, linked.Token, guard);
    }
    public async Task<VerifiedSecurityAuditEvent?> FindVerifiedAsync(SecurityAuditEventId eventId, CancellationToken cancellationToken = default)
    {
        SecurityAuditIdentity.Validate(eventId.Value);
        await using var connection = CreateConnection(); await connection.OpenAsync(cancellationToken);
        using var transaction = connection.BeginTransaction(deferred: true);
        var integrity = await SqliteSecurityAuditIntegrityVerifier.VerifyAsync(connection, transaction, cancellationToken);
        if (!integrity.IsValid) throw new InvalidOperationException("Canonical audit chain is not verifiable.");
        var found = await CanonicalSecurityAuditWriter.FindAsync(connection, transaction, eventId, cancellationToken);
        transaction.Commit(); return found;
    }
}

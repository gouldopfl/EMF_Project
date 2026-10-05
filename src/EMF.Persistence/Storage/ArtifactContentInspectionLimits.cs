using System.Text;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace EMF.Persistence.Storage;

// Admission profile for complete integrity inspection, not permission to skip rows.
// Metadata/row/entry limits bound retained collections; payload bytes bound migrated
// evidence hashing. SQLite work and the deadline also cover quick_check/schema SQL.
public sealed record ArtifactContentInspectionLimits
{
    public int MaximumNamespaceEntries { get; init; } = 100_000;
    public int MaximumCatalogRows { get; init; } = 100_000;
    public long MaximumMetadataBytes { get; init; } = 64L * 1024 * 1024;
    public int MaximumRowBytes { get; init; } = 1024 * 1024;
    public long MaximumDatabaseBytes { get; init; } = 256L * 1024 * 1024;
    public long MaximumEvidenceBytes { get; init; } = 300L * 1024 * 1024;
    public long MaximumWorkUnits { get; init; } = 50_000_000;
    // Cumulative processing time in synchronous inspection scopes. Queue/I/O wait
    // and time executing unrelated operations do not consume this work budget.
    public TimeSpan MaximumDuration { get; init; } = TimeSpan.FromSeconds(30);

    internal void Validate()
    {
        if (MaximumNamespaceEntries <= 0 || MaximumCatalogRows <= 0 || MaximumMetadataBytes <= 0
            || MaximumRowBytes <= 0 || MaximumRowBytes > MaximumMetadataBytes || MaximumDatabaseBytes <= 0
            || MaximumEvidenceBytes <= 0 || MaximumWorkUnits <= 0
            || MaximumDuration <= TimeSpan.Zero || MaximumDuration > TimeSpan.FromMinutes(2))
            throw new ArgumentOutOfRangeException(nameof(ArtifactContentInspectionLimits));
    }
}

public sealed class ArtifactContentInspectionTimeoutException : IOException
{
    public ArtifactContentInspectionTimeoutException()
        : base("Content inspection exceeded its admitted processing duration bound.") { }
}

internal sealed class ContentInspectionBudget : IDisposable
{
    private readonly ArtifactContentInspectionLimits _limits;
    private readonly CancellationToken _caller;
    private TimeSpan _elapsed, _started;
    private int _depth;
    private readonly Action<string>? _checkpoint;
    private long _entries, _rows, _metadata, _evidence, _work;
    private string? _failure;
    private bool _disposed;
    internal CancellationToken Token => _caller;
    private static TimeSpan ProcessingClock()
    {
        if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("Bounded content inspection requires Linux.");
        return LinuxContentDurability.InspectionProcessingTime();
    }
    private TimeSpan ProcessingTime => _elapsed + (_depth == 0 ? TimeSpan.Zero : ProcessingClock() - _started);
    internal bool IsStopped => Token.IsCancellationRequested || _failure is not null || ProcessingTime >= _limits.MaximumDuration;
    internal ContentInspectionBudget(ArtifactContentInspectionLimits limits, CancellationToken token, Action<string>? checkpoint = null)
    {
        limits.Validate(); _limits = limits; _checkpoint = checkpoint;
        _caller = token;
    }
    internal void Check(string point = "Inspection")
    {
        _checkpoint?.Invoke(point);
        ThrowIfStopped();
    }
    internal void ThrowIfStopped()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Token.ThrowIfCancellationRequested();
        if (ProcessingTime >= _limits.MaximumDuration) throw new ArtifactContentInspectionTimeoutException();
        if (_failure is not null) throw new InvalidDataException("Content inspection exceeded its admitted " + _failure + " bound.");
    }
    private void Charge(ref long used, long count, long maximum, string category)
    {
        ThrowIfStopped();
        if (count < 0 || count > maximum - used)
        { _failure = category; ThrowIfStopped(); }
        used += count;
    }
    internal void Entry(string path)
    {
        Check("NamespaceEntry"); Charge(ref _entries, 1, _limits.MaximumNamespaceEntries, "namespace entry"); Text(path);
    }
    internal void Row()
    {
        Check("CatalogRow"); Charge(ref _rows, 1, _limits.MaximumCatalogRows, "catalog row");
        // Fixed allowance for bounded row/collection bookkeeping in addition to strings.
        Charge(ref _metadata, 512, _limits.MaximumMetadataBytes, "metadata byte");
    }
    internal void Text(string value)
    {
        Check(); var bytes = Encoding.UTF8.GetByteCount(value);
        if (bytes > _limits.MaximumRowBytes) { _failure = "row byte"; ThrowIfStopped(); }
        Charge(ref _metadata, bytes, _limits.MaximumMetadataBytes, "metadata byte");
    }
    internal void Evidence(long bytes)
    { Check("EvidenceReservation"); Charge(ref _evidence, bytes, _limits.MaximumEvidenceBytes, "evidence byte"); }
    internal void Step()
    { Check("LineageStep"); Charge(ref _work, 1, _limits.MaximumWorkUnits, "work"); }
    internal void Database(string path)
    {
        Check("DatabaseInspection");
        if (new FileInfo(path).Length > _limits.MaximumDatabaseBytes)
        { _failure = "database byte"; ThrowIfStopped(); }
    }
    // Scopes must be synchronous: no owning thread's clock spans an await. Nested
    // scans share counters and accumulated processing time rather than resetting them.
    internal IDisposable Inspect()
    {
        if (_depth++ == 0) _started = ProcessingClock();
        return new InspectionScope(this);
    }
    private sealed class InspectionScope(ContentInspectionBudget budget) : IDisposable
    {
        public void Dispose()
        {
            if (--budget._depth == 0)
                budget._elapsed += ProcessingClock() - budget._started;
        }
    }
    internal IDisposable InspectSql(SqliteConnection connection)
    {
        Check("SqlInspection"); return new SqlInspection(this, connection);
    }
    private sealed class SqlInspection : IDisposable
    {
        private readonly ContentInspectionBudget _budget;
        private readonly IDisposable _scope;
        private readonly sqlite3 _handle;
        private readonly int _previousLength;
        private readonly CancellationTokenRegistration _cancellation;
        internal SqlInspection(ContentInspectionBudget budget, SqliteConnection connection)
        {
            // Reserve the first callback interval before running SQL, so even a
            // statement shorter than the interval has a conservatively bounded cost.
            budget.Charge(ref budget._work, 100, budget._limits.MaximumWorkUnits, "SQLite work");
            _scope = budget.Inspect();
            _budget = budget; _handle = connection.Handle!;
            _previousLength = raw.sqlite3_limit(_handle, raw.SQLITE_LIMIT_LENGTH, budget._limits.MaximumRowBytes);
            raw.sqlite3_progress_handler(_handle, 100, Progress, null);
            // This connection is private/nonpooled. No shared/global SQLite state is changed.
            _cancellation = budget.Token.Register(() => raw.sqlite3_interrupt(_handle));
        }
        private int Progress(object unused)
        {
            _budget._checkpoint?.Invoke("SqlProgress");
            if (_budget.IsStopped) return 1;
            if (100 > _budget._limits.MaximumWorkUnits - _budget._work)
            { _budget._failure = "SQLite work"; return 1; }
            _budget._work += 100; return 0;
        }
        public void Dispose()
        {
            _cancellation.Dispose();
            raw.sqlite3_progress_handler(_handle, 0, null, null);
            raw.sqlite3_limit(_handle, raw.SQLITE_LIMIT_LENGTH, _previousLength);
            _scope.Dispose();
        }
    }
    public void Dispose() { _disposed = true; _checkpoint?.Invoke("InspectionDisposed"); }
}

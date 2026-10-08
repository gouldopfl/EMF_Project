using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// Logical policy units, not heap measurements. Every cap is supplied by the caller.
internal sealed record ReviewerAssemblyRetainedReaderLimits(
    long MaximumLiveUnits, long MaximumWorkUnits, int MaximumSessions,
    long MaximumSessionLiveUnits, long MaximumSessionWorkUnits, long MaximumSessionAdmissionWork,
    int MaximumRegisteredBuffersPerSession, long MaximumOutputLiveUnitsPerQuery,
    long MaximumPrecountWorkPerQuery, long MaximumProjectionWorkPerQuery, long MaximumWorkPerQuery,
    int MaximumOutputRowsPerQuery, int MaximumOutputNodesPerQuery, int MaximumCollectionEntries,
    int MaximumMetadataNodes, int MaximumMetadataDepth, int MaximumMetadataKeyChars,
    int MaximumSourceBytesPerQuery, int MaximumTextUtf16Chars, int MaximumBuffersPerQuery,
    long MaximumQueuedWorkUnitsPerSession, long MaximumQueuedWorkUnitsContext,
    int MaximumActiveProjectionsPerSession, int MaximumWaitingProjectionsPerSession)
{
    internal void Validate(ReviewerAssemblyFoundationLimits f, int encodedLength)
    {
        f.Validate();
        // Caller-only validation occurs before owned construction. M is a cap, not a decoded count.
        if (MaximumLiveUnits <= 0 || MaximumWorkUnits <= 0 || MaximumSessions <= 0 ||
            MaximumSessionLiveUnits <= 0 || MaximumSessionWorkUnits <= 0 || MaximumSessionAdmissionWork <= 0 ||
            MaximumRegisteredBuffersPerSession <= 0 || MaximumOutputLiveUnitsPerQuery <= 0 ||
            MaximumPrecountWorkPerQuery <= 0 || MaximumProjectionWorkPerQuery <= 0 || MaximumWorkPerQuery <= 0 ||
            MaximumOutputRowsPerQuery <= 0 || MaximumOutputNodesPerQuery <= 0 || MaximumCollectionEntries <= 0 ||
            MaximumMetadataNodes <= 0 || MaximumMetadataDepth <= 0 || MaximumMetadataKeyChars <= 0 ||
            MaximumSourceBytesPerQuery <= 0 || MaximumTextUtf16Chars <= 0 || MaximumBuffersPerQuery <= 0 ||
            MaximumQueuedWorkUnitsPerSession <= 0 || MaximumQueuedWorkUnitsContext <= 0 ||
            MaximumActiveProjectionsPerSession != 1 || MaximumWaitingProjectionsPerSession != 1)
            throw new ArgumentOutOfRangeException(nameof(ReviewerAssemblyRetainedReaderLimits));
        try
        {
            checked
            {
                long session = ReviewerAssemblyRetainedReaderBudget.SessionLive(f, this);
                long peak = ReviewerAssemblyRetainedReaderBudget.ConstructionLive(encodedLength, f, this);
                long work = ReviewerAssemblyRetainedReaderBudget.ConstructionWork(encodedLength, f, this);
                long foundationWork = encodedLength + ReviewerAssemblyRetainedReaderBudget.D(encodedLength) +
                    3L * f.MaximumEncodedBytes + 2L * ReviewerAssemblyRetainedReaderBudget.D(f.MaximumEncodedBytes);
                if (MaximumSessions > f.MaximumLeases || MaximumSessions > Array.MaxLength || f.MaximumMembers > Array.MaxLength || MaximumMetadataDepth > Math.Min(64, f.MaximumDepth) ||
                    MaximumMetadataKeyChars > f.MaximumValueBytes || MaximumSourceBytesPerQuery > f.MaximumSourceBytes ||
                    MaximumSourceBytesPerQuery > Array.MaxLength || MaximumRegisteredBuffersPerSession >= Array.MaxLength ||
                    MaximumCollectionEntries > Array.MaxLength || MaximumBuffersPerQuery > MaximumRegisteredBuffersPerSession ||
                    MaximumWorkPerQuery != MaximumPrecountWorkPerQuery + MaximumProjectionWorkPerQuery ||
                    MaximumSessionLiveUnits < session || MaximumOutputLiveUnitsPerQuery > MaximumSessionLiveUnits ||
                    MaximumLiveUnits < Math.Max(peak, ReviewerAssemblyRetainedReaderBudget.FixedContext(this) + f.MaximumEncodedBytes + session) ||
                    MaximumWorkUnits < work || MaximumSessionWorkUnits < ReviewerAssemblyRetainedReaderBudget.SessionWork(f, this) ||
                    MaximumSessionAdmissionWork > MaximumSessionWorkUnits || MaximumPrecountWorkPerQuery > MaximumSessionWorkUnits ||
                    MaximumProjectionWorkPerQuery > MaximumSessionWorkUnits || MaximumSessionWorkUnits > MaximumWorkUnits ||
                    MaximumQueuedWorkUnitsPerSession < MaximumWorkPerQuery || MaximumQueuedWorkUnitsPerSession > MaximumSessionWorkUnits ||
                    MaximumQueuedWorkUnitsContext < MaximumQueuedWorkUnitsPerSession || MaximumQueuedWorkUnitsContext > MaximumWorkUnits ||
                    f.MaximumPrivateBytes < encodedLength + ReviewerAssemblyRetainedReaderBudget.D(encodedLength) + ReviewerAssemblyRetainedReaderBudget.D(f.MaximumEncodedBytes) ||
                    f.MaximumCopyWork < foundationWork + MaximumSessions * (256L + ReviewerAssemblyRetainedReaderBudget.D(f.MaximumEncodedBytes)) ||
                    f.MaximumDetachedBytes < MaximumSessions * (256L + ReviewerAssemblyRetainedReaderBudget.D(f.MaximumEncodedBytes)))
                    throw new ArgumentOutOfRangeException(nameof(ReviewerAssemblyRetainedReaderLimits), "Inconsistent explicit limits.");
            }
        }
        catch (OverflowException) { throw new ArgumentOutOfRangeException(nameof(ReviewerAssemblyRetainedReaderLimits), "ArithmeticOverflow"); }
    }
}

internal readonly record struct ReviewerAssemblyRetainedAccounting(long Live, long Spent, long Reserved, long Queued, int Sessions, int Buffers);

internal sealed class ReviewerAssemblyRetainedReaderBudget
{
    internal readonly object Gate = new();
    internal readonly ReviewerAssemblyRetainedReaderLimits Limits;
    internal long Live, Spent, Reserved, Queued;
    internal int Sessions;
    internal sealed class Account
    {
        internal long Live, Spent, Reserved, Queued;
        internal int Buffers;
    }
    internal ReviewerAssemblyRetainedReaderBudget(ReviewerAssemblyRetainedReaderLimits limits, long live, long spent)
    { Limits = limits; Live = live; Spent = spent; }
    internal static long D(long x) => checked(16 * x + 4096);
    internal static long A(long n) => checked(64 + 16 * n);
    internal static long H(long n) => checked(128 + 96 * n);
    internal static long FixedContext(ReviewerAssemblyRetainedReaderLimits l) => checked(256 + H(l.MaximumSessions));
    internal static long Index(long m) => checked(A(m) + 128 * m + H(m));
    internal static long SessionLive(ReviewerAssemblyFoundationLimits f, ReviewerAssemblyRetainedReaderLimits l) =>
        checked(256 + D(f.MaximumEncodedBytes) + Index(f.MaximumMembers) + H((long)l.MaximumRegisteredBuffersPerSession + 1) + A((long)l.MaximumMetadataDepth + 1));
    internal static long SessionWork(ReviewerAssemblyFoundationLimits f, ReviewerAssemblyRetainedReaderLimits l) =>
        checked(256 + D(f.MaximumEncodedBytes) + f.MaximumEncodedBytes + l.MaximumRegisteredBuffersPerSession + 1L);
    internal static long ConstructionLive(int e, ReviewerAssemblyFoundationLimits f, ReviewerAssemblyRetainedReaderLimits l) =>
        checked(FixedContext(l) + e + 3L * f.MaximumEncodedBytes + 2 * D(f.MaximumEncodedBytes));
    internal static long ConstructionWork(int e, ReviewerAssemblyFoundationLimits f, ReviewerAssemblyRetainedReaderLimits l) =>
        checked(e + D(e) + 3L * f.MaximumEncodedBytes + 2 * D(f.MaximumEncodedBytes) + e + 5L * f.MaximumEncodedBytes + 1L + l.MaximumSessions);
    internal static InvalidDataException Error(string code) => new(code);
    internal static long Add(long one, long two)
    { try { return checked(one + two); } catch (OverflowException) { throw Error("ArithmeticOverflow"); } }
    // Caller holds Gate. All transitions compute and validate first, then commit together.
    internal void ReserveLive(Account? a, long units)
    {
        long next = Add(Live, units);
        long local = a is null ? 0 : Add(a.Live, units);
        if (next > Limits.MaximumLiveUnits) throw Error("ContextLiveCapacity");
        if (a is not null && local > Limits.MaximumSessionLiveUnits) throw Error("SessionLiveCapacity");
        Live = next; if (a is not null) a.Live = local;
    }
    internal void ReleaseLive(Account? a, long units)
    {
        Live = checked(Live - units); if (a is not null) a.Live = checked(a.Live - units);
        if (Live < 0 || a?.Live < 0) throw new InvalidOperationException("Live accounting underflow.");
    }
    internal void Spend(Account? a, long units)
    {
        long next = Add(Spent, units);
        long local = a is null ? 0 : Add(a.Spent, units);
        if (Add(next, Reserved) > Limits.MaximumWorkUnits ||
            a is not null && Add(local, a.Reserved) > Limits.MaximumSessionWorkUnits)
            throw Error("WorkCapacity");
        Spent = next; if (a is not null) a.Spent = local;
    }
    internal void ReserveWork(Account a, long units)
    {
        long next = Add(Reserved, units); long local = Add(a.Reserved, units);
        if (Add(Spent, next) > Limits.MaximumWorkUnits ||
            Add(a.Spent, local) > Limits.MaximumSessionWorkUnits) throw Error("WorkCapacity");
        Reserved = next; a.Reserved = local;
    }
    internal void Consume(Account a, long units)
    {
        if (units < 0 || units > a.Reserved) throw Error("WorkCapacity");
        // Prepaid cleanup work is spent with the projection; no later refund.
        Reserved = checked(Reserved - units); a.Reserved = checked(a.Reserved - units);
        Spent = checked(Spent + units); a.Spent = checked(a.Spent + units);
    }
    internal void ReleaseWork(Account a, long units)
    { Reserved = checked(Reserved - units); a.Reserved = checked(a.Reserved - units); }
    internal void ReserveQueue(Account a)
    {
        long u = Limits.MaximumWorkPerQuery;
        long next = Add(Queued, u); long local = Add(a.Queued, u);
        if (next > Limits.MaximumQueuedWorkUnitsContext || local > Limits.MaximumQueuedWorkUnitsPerSession) throw Error("QueueCapacity");
        Queued = next; a.Queued = local;
    }
    internal void ReleaseQueue(Account a)
    { Queued = checked(Queued - Limits.MaximumWorkPerQuery); a.Queued = checked(a.Queued - Limits.MaximumWorkPerQuery); }
    internal void ReserveOutput(Account a, Ledger ledger)
    {
        if (ledger.Live > Limits.MaximumOutputLiveUnitsPerQuery || ledger.V > Limits.MaximumOutputNodesPerQuery ||
            ledger.Rows > Limits.MaximumOutputRowsPerQuery) throw Error("OutputCapacity");
        if (ledger.Buffers > Limits.MaximumBuffersPerQuery || checked((long)a.Buffers + ledger.Buffers) > Limits.MaximumRegisteredBuffersPerSession)
            throw Error("BufferCapacity");
        ReserveLive(a, ledger.Live); a.Buffers = checked(a.Buffers + ledger.Buffers);
    }
    internal ReviewerAssemblyRetainedAccounting Snapshot(Account? a = null) => a is null
        ? new(Live, Spent, Reserved, Queued, Sessions, 0)
        : new(a.Live, a.Spent, a.Reserved, a.Queued, Sessions, a.Buffers);
    internal sealed class Meter
    {
        private readonly ReviewerAssemblyRetainedReaderBudget budget;
        private readonly Account account;
        private readonly long maximum;
        private readonly CancellationToken token;
        internal long Used;
        internal Meter(ReviewerAssemblyRetainedReaderBudget budget, Account account, long maximum, CancellationToken token)
        { this.budget = budget; this.account = account; this.maximum = maximum; this.token = token; }
        internal void Tick(long units = 1)
        {
            token.ThrowIfCancellationRequested();
            long next = Add(Used, units);
            if (next > maximum) throw Error("WorkCapacity");
            lock (budget.Gate) budget.Consume(account, units);
            Used = next;
        }
    }
    internal struct Ledger
    {
        internal long Live, V, P, O, Extra;
        internal int Rows, Buffers, MetadataNodes;
        internal long ProjectionWork => Add(Add(V,O),Extra);
    }
}

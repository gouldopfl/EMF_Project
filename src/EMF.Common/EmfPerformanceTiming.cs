using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Security.Cryptography;
using System.Buffers.Binary;

namespace EMF.Common;

public enum EmfPerformancePhase
{
    EvidenceLoading,
    EvidenceMaterialization,
    PresentationPreparation,
    DocxConstruction,
    DocxSave,
    DocxPublish,
    PdfConversion,
    PrivacyMasking,
    PaddleDecode,
    PaddleRegionDetection,
    Ocr0,
    Ocr90,
    Ocr180,
    Ocr270,
    SourcePageRender,
    LiteratureProcessing,
    EnlargementDetection,
    EnlargementRender,
    ConverterTotal,
    LibreOfficeConversion,
    ConverterIdentity,
    ConverterProfile,
    PaddleTextInference,
    PdfRasterization,
    RasterIdentity,
    DocxMaterialization,
    PdfPublish
}

public enum EmfPerformanceCounter
{
    PrivacyOcrPages,
    PaddleRuns,
    LiteraturePages,
    SupplementsSelected,
    RasterOcrRequests,
    UniqueRasterIdentities,
    RepeatRasterRequests,
    PotentialPaddleRunsAvoided
}

/// <summary>Opt-in, operation-scoped diagnostics. Only fixed labels and numeric aggregates can be recorded.</summary>
public static class EmfPerformanceTiming
{
    private static readonly AsyncLocal<Session?> Current = new();
    private static readonly AsyncLocal<Measurement?> TopLevelOwner = new();
    private static readonly string[] PhaseLabels =
    [
        "Evidence loading", "Evidence extraction/materialization", "Presentation/version preparation",
        "DOCX construction", "DOCX save", "DOCX file publish", "PDF conversion total",
        "Privacy masking", "Paddle raster decode", "Paddle region detection",
        "OCR 0 degrees", "OCR 90 degrees", "OCR 180 degrees", "OCR 270 degrees",
        "Source-page raster/render", "Literature processing", "Enlargement candidate detection",
        "Enlargement crop/render pipeline", "Converter total", "LibreOffice conversion",
        "Converter identity", "Converter profile fingerprint", "Paddle text inference", "PDF page rasterization", "Raster identity bookkeeping",
        "DOCX output materialization", "PDF file publish"
    ];
    private static readonly string[] CounterLabels =
        ["Privacy OCR pages", "Paddle runs", "Literature pages examined", "Supplements selected",
            "Raster OCR requests", "Unique raster identities available", "Repeat raster requests",
            "Potential Paddle runs avoided by exact-raster caching"];

    public static IDisposable BeginReviewer(Action<string> writeSummary)
    {
        if (Current.Value is not null ||
            Environment.GetEnvironmentVariable("EMF_PERF_TIMING") != "1")
            return Measurement.None;
        var session = new Session(writeSummary);
        Current.Value = session;
        return session;
    }

    public static Measurement Measure(EmfPerformancePhase phase) => Create(phase, start: true);
    public static Measurement MeasureTopLevel(EmfPerformancePhase phase) => Create(phase, start: true, topLevel: true);
    // Used to time automatic DOCX disposal/serialization without changing its lifetime.
    public static Measurement DeferredTopLevel(EmfPerformancePhase phase) => Create(phase, start: false, topLevel: true);

    private static Measurement Create(EmfPerformancePhase phase, bool start, bool topLevel = false)
    {
        var session = Current.Value;
        if (session is null) return Measurement.None;
        if ((uint)phase >= PhaseLabels.Length) throw new ArgumentOutOfRangeException(nameof(phase));
        return new Measurement(session, (int)phase, start, topLevel);
    }

    public static void Count(EmfPerformanceCounter counter, long count = 1)
    {
        var session = Current.Value;
        if (session is null) return;
        if ((uint)counter >= CounterLabels.Length) throw new ArgumentOutOfRangeException(nameof(counter));
        Interlocked.Add(ref session.Counters[(int)counter], count);
    }

    // Bookkeeping only: no image buffers or OCR results are retained, and no calls are skipped.
    public static RasterRequest RecordPrivacyRaster(ReadOnlyMemory<byte> image)
    {
        var session = Current.Value;
        if (session is null) return default;
        using var timing = Measure(EmfPerformancePhase.RasterIdentity);
        Count(EmfPerformanceCounter.RasterOcrRequests);
        Span<byte> digest = stackalloc byte[32];
        SHA256.HashData(image.Span, digest);
        var a = BinaryPrimitives.ReadUInt64LittleEndian(digest);
        var b = BinaryPrimitives.ReadUInt64LittleEndian(digest[8..]);
        var c = BinaryPrimitives.ReadUInt64LittleEndian(digest[16..]);
        var d = BinaryPrimitives.ReadUInt64LittleEndian(digest[24..]);
        while (true)
        {
            var head = Volatile.Read(ref session.Rasters);
            for (var entry = head; entry is not null; entry = entry.Next)
                if (entry.Length == image.Length && entry.A == a && entry.B == b && entry.C == c && entry.D == d)
                {
                    Count(EmfPerformanceCounter.RepeatRasterRequests);
                    return new RasterRequest(entry, Volatile.Read(ref entry.Completed) != 0);
                }
            var added = new Session.RasterEntry(a, b, c, d, image.Length, head);
            if (Interlocked.CompareExchange(ref session.Rasters, added, head) == head)
            {
                Count(EmfPerformanceCounter.UniqueRasterIdentities);
                return new RasterRequest(added, false);
            }
        }
    }

    public readonly struct RasterRequest
    {
        private readonly Session.RasterEntry? _entry;
        public bool PreviouslyCompleted { get; }
        internal RasterRequest(Session.RasterEntry entry, bool previouslyCompleted)
        { _entry = entry; PreviouslyCompleted = previouslyCompleted; }
        public void Complete()
        {
            if (_entry is not null) Interlocked.Exchange(ref _entry.Completed, 1);
        }
    }

    public sealed class Measurement : IDisposable
    {
        internal static readonly Measurement None = new(null, 0, false);
        private readonly Session? _session;
        private readonly int _phase;
        private long _started;
        private int _disposed;
        private readonly bool _topLevel;
        private bool _ownsTopLevel;
        private Measurement? _previousOwner;

        internal Measurement(Session? session, int phase, bool start, bool topLevel = false)
        {
            _session = session;
            _phase = phase;
            _topLevel = topLevel;
            if (start) Start();
        }

        public void Start()
        {
            if (_session is null || _started != 0 || Volatile.Read(ref _disposed) != 0) return;
            if (_topLevel)
            {
                _previousOwner = TopLevelOwner.Value;
                var active = Interlocked.CompareExchange(ref _session.ActiveTopLevel, this, null);
                _ownsTopLevel = active is null;
                if (_ownsTopLevel) TopLevelOwner.Value = this;
                else if (active != _previousOwner) Interlocked.Exchange(ref _session.AccountingInvalid, 1);
            }
            _started = Stopwatch.GetTimestamp();
        }

        public void Dispose()
        {
            if (_session is null || Interlocked.Exchange(ref _disposed, 1) != 0 || _started == 0) return;
            var elapsed = Stopwatch.GetTimestamp() - _started;
            if (_topLevel && !_ownsTopLevel && _previousOwner is not null &&
                Volatile.Read(ref _previousOwner._disposed) != 0)
                Interlocked.Exchange(ref _session.AccountingInvalid, 1);
            Interlocked.Add(ref _session.Ticks[_phase], elapsed);
            if (_ownsTopLevel)
            {
                Interlocked.Add(ref _session.TopLevelTicks[_phase], elapsed);
                Interlocked.Increment(ref _session.TopLevelCalls[_phase]);
                TopLevelOwner.Value = _previousOwner;
                Interlocked.CompareExchange(ref _session.ActiveTopLevel, null, this);
            }
            Interlocked.Increment(ref _session.Calls[_phase]);
        }
    }

    internal sealed class Session(Action<string> writeSummary) : IDisposable
    {
        internal readonly long[] Ticks = new long[PhaseLabels.Length];
        internal readonly long[] Calls = new long[PhaseLabels.Length];
        internal readonly long[] TopLevelTicks = new long[PhaseLabels.Length];
        internal readonly long[] TopLevelCalls = new long[PhaseLabels.Length];
        internal Measurement? ActiveTopLevel;
        internal int AccountingInvalid;
        internal RasterEntry? Rasters;
        internal sealed class RasterEntry(ulong a, ulong b, ulong c, ulong d, int length, RasterEntry? next)
        {
            internal readonly ulong A = a, B = b, C = c, D = d;
            internal readonly int Length = length;
            internal readonly RasterEntry? Next = next;
            internal int Completed;
        }
        internal readonly long[] Counters = new long[CounterLabels.Length];
        private readonly long _started = Stopwatch.GetTimestamp();
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            var elapsed = Stopwatch.GetTimestamp() - _started;
            Current.Value = null;
            var summary = new StringBuilder("===== EMF PERFORMANCE TIMING =====\n");
            summary.AppendLine("Nested/overlapping measurements; do not sum rows. Zero calls means unmeasured/not invoked.");
            AppendDuration("Reviewer total", elapsed, 1);
            long attributed = 0;
            for (var i = 0; i < PhaseLabels.Length; i++)
            {
                var ticks = Interlocked.Read(ref TopLevelTicks[i]);
                attributed += ticks;
                if (Interlocked.Read(ref TopLevelCalls[i]) != 0)
                    AppendDuration("Top-level " + PhaseLabels[i], ticks, Interlocked.Read(ref TopLevelCalls[i]));
            }
            if (Volatile.Read(ref AccountingInvalid) != 0 || ActiveTopLevel is not null || attributed > elapsed)
                summary.AppendLine("Unattributed top-level remainder unavailable: concurrent/unfinished top-level scopes.");
            else
                AppendDuration("Unattributed top-level remainder", elapsed - attributed, 1);
            summary.AppendLine("Inclusive diagnostics below overlap the exclusive top-level rows above.");
            for (var i = 0; i < PhaseLabels.Length; i++)
                AppendDuration(PhaseLabels[i], Interlocked.Read(ref Ticks[i]), Interlocked.Read(ref Calls[i]));
            for (var i = 0; i < CounterLabels.Length; i++)
                summary.Append(CounterLabels[i].PadRight(36)).Append(' ').AppendLine(
                    Interlocked.Read(ref Counters[i]).ToString(CultureInfo.InvariantCulture));
            summary.AppendLine("Raster metrics cover exact encoded privacy inputs; potential savings require a prior completed request.");
            summary.Append("==================================");
            Rasters = null;
            // Diagnostics must not change success/failure of package generation.
            try { writeSummary(summary.ToString()); }
            catch { /* Best-effort diagnostic output only. */ }

            void AppendDuration(string label, long ticks, long calls) => summary
                .Append(label.PadRight(36)).Append(' ')
                .Append((ticks / (double)Stopwatch.Frequency).ToString("0.000", CultureInfo.InvariantCulture))
                .Append(" s; calls ").AppendLine(calls.ToString(CultureInfo.InvariantCulture));
        }
    }
}

using System.Security.Cryptography;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// Private authority is an owned canonical byte representation, never a caller
// object graph. Each read decodes a separate graph with separately owned buffers.
// Parent close is nonblocking and defers cleanup until existing children finish.
public sealed class ReviewerAssemblyInputOwner : IReviewerAssemblyCapturedInputs
{
    private readonly object gate = new();
    private byte[]? canonical;
    private readonly ReviewerAssemblyFoundationLimits limits;
    private bool closed;
    private int children;
    private long liveDetached, copyWork;
    private const long LeaseReservation = 256;
    internal Action<string, byte[]>? Checkpoint { get; set; }

    private ReviewerAssemblyInputOwner(byte[] canonical, ReviewerAssemblyFoundationLimits limits, long work)
    {
        this.canonical = canonical;
        this.limits = limits;
        copyWork = work;
        CanonicalSha256 = ReviewerAssemblyCaptureRepresentation.Hash(canonical);
    }

    public string CanonicalSha256 { get; }
    internal (long LiveDetached, long CopyWork, int Children, bool PrivateReleased) Accounting
    { get { lock (gate) return (liveDetached, copyWork, children, canonical is null); } }

    public static ReviewerAssemblyInputOwner Create(ReviewerAssemblyCaptureManifest input,
        ReviewerAssemblyDraftIdentity expected, ReviewerAssemblyFoundationLimits limits, CancellationToken ct = default)
        => CreateCore(input, expected, limits, ct, 0);

    internal static ReviewerAssemblyInputOwner CreateObserved(ReviewerAssemblyCaptureManifest input,
        ReviewerAssemblyFoundationLimits limits, CancellationToken ct, Action<string, byte[]> checkpoint)
        => CreateCore(input, ReviewerAssemblyDraftIdentity.Foundation, limits, ct, 0, checkpoint);

    private static ReviewerAssemblyInputOwner CreateCore(ReviewerAssemblyCaptureManifest input,
        ReviewerAssemblyDraftIdentity expected, ReviewerAssemblyFoundationLimits limits, CancellationToken ct, long priorWork,
        Action<string, byte[]>? checkpoint = null)
    {
        limits.Validate(); ct.ThrowIfCancellationRequested();
        var maximumWork = checked(priorWork + ReviewerAssemblyCaptureRepresentation.ConstructionWork(limits.MaximumEncodedBytes) + ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(limits.MaximumEncodedBytes));
        if (maximumWork > limits.MaximumCopyWork)
            throw new InvalidDataException("Combined foundation work budget exceeded before encoding/decoding.");
        byte[]? bytes = null;
        try
        {
            bytes = ReviewerAssemblyCaptureRepresentation.EncodeCore(input, expected, limits, ct, checkpoint);
            checkpoint?.Invoke("CanonicalOwned", bytes);
            using var validated = ReviewerAssemblyCaptureRepresentation.DecodeOwned(bytes, expected, limits, ct, checkpoint);
            var work = checked(priorWork + ReviewerAssemblyCaptureRepresentation.ConstructionWork(limits.MaximumEncodedBytes) + ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(bytes.Length));
            if (work > limits.MaximumCopyWork) throw new InvalidDataException("Private validation work budget exceeded.");
            ct.ThrowIfCancellationRequested();
            var owner = new ReviewerAssemblyInputOwner(bytes, limits, work);
            bytes = null; return owner;
        }
        finally { if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); }
    }

    public static ReviewerAssemblyInputOwner FromEncoded(ReadOnlyMemory<byte> input,
        ReviewerAssemblyDraftIdentity expected, ReviewerAssemblyFoundationLimits limits, CancellationToken ct = default)
        => FromEncodedCore(input, expected, limits, ct, null);

    internal static ReviewerAssemblyInputOwner FromEncodedObserved(ReadOnlyMemory<byte> input,
        ReviewerAssemblyFoundationLimits limits, CancellationToken ct, Action<string, byte[]> checkpoint)
        => FromEncodedCore(input, ReviewerAssemblyDraftIdentity.Foundation, limits, ct, checkpoint);

    private static ReviewerAssemblyInputOwner FromEncodedCore(ReadOnlyMemory<byte> input,
        ReviewerAssemblyDraftIdentity expected, ReviewerAssemblyFoundationLimits limits, CancellationToken ct,
        Action<string, byte[]>? checkpoint)
    {
        limits.Validate(); ct.ThrowIfCancellationRequested();
        if (input.Length == 0 || input.Length > limits.MaximumEncodedBytes ||
            checked((long)input.Length + ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(input.Length) +
                ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(limits.MaximumEncodedBytes)) > limits.MaximumPrivateBytes)
            throw new InvalidDataException("Input budget exceeded before copying.");
        var priorWork = checked((long)input.Length + ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(input.Length));
        if (checked(priorWork + ReviewerAssemblyCaptureRepresentation.ConstructionWork(limits.MaximumEncodedBytes) + ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(limits.MaximumEncodedBytes)) > limits.MaximumCopyWork)
            throw new InvalidDataException("Input validation/copy work budget exceeded.");
        var copy = input.ToArray(); // Never decode or retain caller-backed memory.
        try
        {
            checkpoint?.Invoke("InputOwned", copy);
            using var decoded = ReviewerAssemblyCaptureRepresentation.DecodeOwned(copy, expected, limits, ct, checkpoint);
            return CreateCore(decoded.Value, expected, limits, ct, priorWork, checkpoint);
        }
        finally { CryptographicOperations.ZeroMemory(copy); }
    }

    public IReviewerAssemblyInputCopyLease Acquire(CancellationToken ct = default)
    {
        lock (gate)
        {
            ct.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(closed, this);
            if (children >= limits.MaximumLeases) throw new InvalidDataException("Concurrent lease limit exceeded.");
            Reserve(LeaseReservation);
            try
            {
                ct.ThrowIfCancellationRequested();
                var child = new ReviewerAssemblyInputLease(this);
                children++; return child;
            }
            catch { liveDetached -= LeaseReservation; throw; }
        }
    }

    private void Reserve(long charge)
    {
        var live = checked(liveDetached + charge);
        var work = checked(copyWork + charge);
        if (live > limits.MaximumDetachedBytes || work > limits.MaximumCopyWork)
            throw new InvalidDataException("Detached live/cumulative work budget exceeded.");
        liveDetached = live; copyWork = work;
    }

    internal (ReviewerAssemblyCaptureRepresentation.Decoded Copy, long Reservation) Read(CancellationToken ct)
    {
        lock (gate)
        {
            ct.ThrowIfCancellationRequested();
            // Parent may be closed, but an outstanding child keeps authority alive.
            var bytes = canonical ?? throw new ObjectDisposedException(nameof(ReviewerAssemblyInputOwner));
            var charge = ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(bytes.Length);
            Reserve(charge); // Reserve before any controlled decode/allocation.
            try
            {
                ct.ThrowIfCancellationRequested();
                var result = ReviewerAssemblyCaptureRepresentation.DecodeOwned(bytes,
                    ReviewerAssemblyDraftIdentity.Foundation, limits, ct, Checkpoint);
                return (result, charge);
            }
            catch { liveDetached -= charge; throw; } // Work is intentionally not refunded.
        }
    }

    internal void Release(long reservation)
    {
        lock (gate)
        {
            liveDetached = checked(liveDetached - reservation - LeaseReservation);
            children--;
            if (children < 0 || liveDetached < 0) throw new InvalidOperationException("Ownership accounting underflow.");
            Cleanup();
        }
    }

    internal void ReleaseRead(long reservation)
    {
        lock (gate) liveDetached = checked(liveDetached - reservation);
    }

    public void Dispose()
    {
        lock (gate) { closed = true; Cleanup(); }
    }

    private void Cleanup()
    {
        if (!closed || children != 0 || canonical is null) return;
        CryptographicOperations.ZeroMemory(canonical); canonical = null;
    }
}

public sealed class ReviewerAssemblyInputLease : IReviewerAssemblyInputCopyLease
{
    private readonly object gate = new();
    private ReviewerAssemblyInputOwner? owner;
    private readonly List<ReviewerAssemblyCaptureRepresentation.Decoded> copies = [];
    private long reservations;
    internal ReviewerAssemblyInputLease(ReviewerAssemblyInputOwner owner) => this.owner = owner;

    public ReviewerAssemblyCaptureManifest ReadCopy(CancellationToken ct = default)
    {
        lock (gate)
        {
            var parent = owner ?? throw new ObjectDisposedException(nameof(ReviewerAssemblyInputLease));
            var (copy, charge) = parent.Read(ct);
            try
            {
                var next = checked(reservations + charge);
                copies.Add(copy);
                reservations = next;
                return copy.Value;
            }
            catch
            {
                // The bounded list cannot normally fail after reservation; retain
                // no unregistered source buffer if an allocation still fails.
                copy.Dispose();
                parent.ReleaseRead(charge);
                throw;
            }
        }
    }

    public void Dispose()
    {
        lock (gate)
        {
            if (owner is null) return;
            var parent = owner; owner = null;
            foreach (var copy in copies) copy.Dispose(); // Registry only; foreign insertions untouched.
            copies.Clear();
            parent.Release(reservations); reservations = 0;
        }
    }
}

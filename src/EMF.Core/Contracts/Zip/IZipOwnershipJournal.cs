namespace EMF.Core.Contracts.Zip;

public interface IZipOwnershipJournal
{
    // Extends only expiry. Work revision, epoch, plan and frontier remain unchanged.
    Task<DateTimeOffset> RenewOwnershipAsync(string operationId, string owner, long epoch, TimeSpan duration, CancellationToken ct = default);
}

// A completed release by this exact live owner ends renewal normally. It grants
// no further work and is distinct from expiry, revocation, review or takeover.
public sealed class ZipOwnershipReleasedException() : InvalidOperationException("ZIP ownership ended through completed release.");

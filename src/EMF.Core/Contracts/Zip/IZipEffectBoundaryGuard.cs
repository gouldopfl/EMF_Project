namespace EMF.Core.Contracts.Zip;

/// <summary>A transient acceptance capability for one narrowly bound ZIP effect.
/// Implementations revalidate live authority after waits. Acceptance must synchronously
/// reject invalidation and remain held through physical effects/transaction commit.
/// A composite metered read may instead consume acceptance for exactly one bounded
/// call initiation, release it, and invoke that call immediately without an await.
/// Nested physical reads remain independently guarded; no acceptance lock spans
/// nested journal/provider waits. Already accepted calls may finish, with linked
/// cancellation; no ticket may authorize a later call or progression.
/// This capability supplies permission only: it cannot manufacture eligibility,
/// repair identity/projections, or replace independent journal/storage validation.
/// This capability is never durable evidence and cannot renew authority.</summary>
public interface IZipEffectBoundaryGuard
{
    CancellationToken Invalidated { get; }
    ValueTask RevalidateAsync(CancellationToken ct = default);
    IDisposable Accept(CancellationToken ct = default);
}

/// <summary>Identifies prepared private ZIP mutations whose physical execution
/// carries the exact live guard used by the journal promotion.</summary>
public interface IZipGuardedPreparedContentMutation : EMF.Core.Contracts.Storage.IPreparedArtifactContentMutation
{
    IZipEffectBoundaryGuard BoundaryGuard { get; }
}

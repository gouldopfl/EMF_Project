namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

/// <summary>Legacy is explicit; a nonlegacy null snapshot is pending, never sealed.</summary>
public sealed record ReviewerPackageSnapshotRead(bool IsLegacy, ReviewerPackageSnapshot? Snapshot);

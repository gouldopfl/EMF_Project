namespace EMF.Persistence.Storage;

/// <summary>Operator attestation; locks cannot stop pre-protocol binaries that ignore them.</summary>
public sealed record ArtifactContentOfflineMigrationOptions(
    bool OldReadersAndWritersStopped,
    long MaxStoredBytes = FileSystemArtifactContentStore.DefaultMaxStoredBytes,
    int MaxArtifacts = 10000,
    long MaxTotalBytes = 2L * 1024 * 1024 * 1024);

public sealed record ArtifactContentMigrationResult(int ArtifactCount, string RetainedDirectory, bool AlreadyCompleted);

public sealed class ArtifactContentMigrationInProgressException : IOException
{
    public ArtifactContentMigrationInProgressException()
        : base("Offline content migration is incomplete; resume the explicit migration operation before admission.") { }
}

internal sealed record ContentSourceIdentity(long Length, string Stamp);

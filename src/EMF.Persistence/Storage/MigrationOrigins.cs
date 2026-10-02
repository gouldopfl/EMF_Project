using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Persistence.Storage;

// Bootstrap authority for imported state, never a mutation or a Core contract.
internal sealed record MigrationOrigin(string Generation, long Length, string Stamp, string Digest, string Retention);

internal static class MigrationOrigins
{
    internal static Dictionary<(string Artifact, string Revision), MigrationOrigin> Validate(
        SqliteConnection connection, SqliteTransaction? transaction, string root,
        IContentStoragePlatform platform, long maxBytes)
    {
        var origins = new Dictionary<(string Artifact, string Revision), MigrationOrigin>();
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT Id,MigrationId,Root,Retention,Phase,ArtifactCount,OfflineAcknowledged FROM ContentMigrationRun";
        string? migrationId = null;
        string? retention = null;
        long artifactCount = 0;
        using (var reader = command.ExecuteReader())
        {
            if (reader.Read())
            {
                migrationId = reader.GetString(1);
                retention = reader.GetString(3);
                artifactCount = reader.GetInt64(5);
                if (reader.GetInt64(0) != 1 || !Guid.TryParseExact(migrationId, "N", out _) ||
                    reader.GetString(2) != root || retention != ".content-legacy-" + migrationId ||
                    reader.GetString(4) != "Completed" || artifactCount < 0 || reader.GetInt64(6) != 1 || reader.Read())
                    throw new InvalidDataException("Content migration checkpoint is incomplete or damaged.");
            }
        }
        if (migrationId is null && Directory.Exists(FileSystemArtifactContentMigration.WorkspaceFor(root)))
            throw new InvalidDataException("Content migration completion has no lineage checkpoint.");
        if (migrationId is not null)
        {
            FileSystemArtifactContentMigration.ValidateCompletedRun(root, migrationId, retention!, artifactCount, platform);
            if (!Directory.Exists(FileSystemArtifactContentMigration.WorkspaceFor(root)))
                throw new InvalidDataException("Content migration completion authority is missing.");

        }
        command.CommandText = "SELECT ArtifactId,Length,SourceStamp,Digest,Generation,Revision,Status FROM ContentMigrationArtifacts";
        var sourceNames = new HashSet<string>(StringComparer.Ordinal);
        using (var reader = command.ExecuteReader())
        {
            while (reader.Read())
            {
                if (migrationId is null) throw new InvalidDataException("Content migration origin has no checkpoint.");
                var artifact = new ArtifactId(reader.GetString(0)).Value;
                // This restriction applies only to retained pre-protocol filenames.
                if (artifact is "." or ".." || artifact.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
                    artifact.Contains('/') || artifact.Contains('\\') || Path.GetFileName(artifact) != artifact)
                    throw new InvalidDataException("Retained legacy source identity is invalid.");
                var length = reader.GetInt64(1);
                var stamp = reader.GetString(2);
                var digest = reader.GetString(3);
                var generation = reader.GetString(4);
                var revision = ArtifactContentIdentity.Validate(reader.GetString(5));
                if (length < 0 || length > maxBytes || string.IsNullOrWhiteSpace(stamp) ||
                    digest.Length != 64 || digest.Any(c => !char.IsAsciiHexDigit(c) || char.IsLower(c)) ||
                    generation.Length != 32 || generation.Any(c => !char.IsAsciiHexDigit(c)) ||
                    reader.GetString(6) != "Retained" || !sourceNames.Add(artifact) ||
                    !origins.TryAdd((artifact, revision), new(generation, length, stamp, digest, retention!)))
                    throw new InvalidDataException("Content migration origin is damaged.");

            }
        }
        if (origins.Count != artifactCount)
            throw new InvalidDataException("Content migration inventory is incomplete.");
        if (artifactCount == 0 && retention is not null && Directory.EnumerateFileSystemEntries(Path.Combine(root, retention)).Any())
            throw new InvalidDataException("Empty retained legacy inventory is contradictory.");
        return origins;
    }

    // Called deliberately at admission after the catalog read transaction ends.
    internal static void VerifyEvidence(Dictionary<(string Artifact, string Revision), MigrationOrigin> origins,
        string root, IContentStoragePlatform platform, long maxBytes)
    {
        foreach (var ((artifact, _), origin) in origins)
        {
            ValidateDirectory(Path.Combine(root, origin.Retention), platform);
            FileSystemArtifactContentMigration.CheckSource(Path.Combine(root, origin.Retention, artifact),
                origin.Length, origin.Stamp, origin.Digest, platform, maxBytes);
            var generationPath = Path.Combine(root, ".content-generations", origin.Generation);
            platform.ValidatePrivatePermissions(generationPath);
            if (platform.InspectSourceFile(generationPath).Length != origin.Length ||
                FileSystemArtifactContentMigration.HashFile(generationPath, platform, maxBytes) != origin.Digest)
                throw new InvalidDataException("Imported content generation is damaged.");
        }
        foreach (var group in origins.GroupBy(pair => pair.Value.Retention))
        {
            var sourceNames = group.Select(pair => pair.Key.Artifact).ToHashSet(StringComparer.Ordinal);
            if (Directory.EnumerateFileSystemEntries(Path.Combine(root, group.Key))
                .Any(path => !sourceNames.Contains(Path.GetFileName(path))))
                throw new InvalidDataException("Retained legacy inventory is contradictory.");
        }
    }

    private static void ValidateDirectory(string path, IContentStoragePlatform platform)
    {
        for (var current = path; current is not null; current = Path.GetDirectoryName(current))
            if (new DirectoryInfo(current).LinkTarget is not null)
                throw new IOException("Content migration paths cannot contain symbolic links.");
        if (!Directory.Exists(path)) throw new InvalidDataException("Retained legacy inventory is missing.");
        platform.ValidatePrivatePermissions(path);
    }
}

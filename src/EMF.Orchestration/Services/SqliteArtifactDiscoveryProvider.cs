using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Contracts.Storage;
using EMF.Inventory.Providers;

namespace EMF.Orchestration.Services;

public sealed class SqliteArtifactDiscoveryProvider :
    IArtifactDiscoveryProvider
{
    private readonly IArtifactContentStore _contentStore;
    private readonly SqliteDatabaseStructureProvider _structureProvider;

    public SqliteArtifactDiscoveryProvider(
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(contentStore);

        _contentStore = contentStore;
        _structureProvider = new SqliteDatabaseStructureProvider();
    }

    public bool CanDiscover(string contentType) =>
        string.Equals(
            contentType,
            "application/x-sqlite3",
            StringComparison.OrdinalIgnoreCase);

    public async Task<ArtifactDiscoveryResult?> DiscoverAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var content = await _contentStore.ReadAsync(
            artifactId,
            cancellationToken);

        if (content is null)
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        var directory = Path.Combine(
            Path.GetTempPath(),
            $"emf-discovery-{Guid.NewGuid():N}");
        if (OperatingSystem.IsWindows())
            Directory.CreateDirectory(directory);
        else
            Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var path = Path.Combine(directory, "source.sqlite");

        try
        {
            await File.WriteAllBytesAsync(
                path,
                content,
                cancellationToken);

            var structure =
                await _structureProvider.DiscoverAsync(
                    path,
                    cancellationToken);

            return new ArtifactDiscoveryResult
            {
                ContentType = "application/x-sqlite3",
                Format = "SQLite",
                Confidence = 1.0,
                Metadata = new Dictionary<string, object>
                {
                    ["databaseStructure"] = structure
                }
            };
        }
        finally
        {
            try
            {
                Directory.Delete(directory, recursive: true);
            }
            catch
            {
                // Best-effort cleanup.
            }
        }
    }
}

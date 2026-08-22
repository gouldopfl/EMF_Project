using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class SqliteArtifactDiscoveryProviderTests
{
    [Fact]
    public async Task DiscoverAsync_ReturnsNullWhenContentMissing()
    {
        var provider = new SqliteArtifactDiscoveryProvider(
            new StubContentStore(null));

        Assert.Null(await provider.DiscoverAsync(
            new ArtifactId("sqlite-missing")));
    }

    [Fact]
    public async Task DiscoverAsync_HonorsCancellation()
    {
        using var cancellation =
            new CancellationTokenSource();

        cancellation.Cancel();

        var provider =
            new SqliteArtifactDiscoveryProvider(
                new StubContentStore(Array.Empty<byte>()));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.DiscoverAsync(
                new ArtifactId("sqlite-cancelled"),
                cancellation.Token));
    }

    [Fact]
    public async Task DiscoverAsync_DiscoversSqliteSchema()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-discovery-{Guid.NewGuid():N}.db");

        try
        {
            await using (var connection =
                new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();

                await using var command =
                    connection.CreateCommand();

                command.CommandText =
                    """
                    CREATE TABLE evidence (
                        id INTEGER PRIMARY KEY,
                        name TEXT NOT NULL
                    );

                    INSERT INTO evidence (name)
                    VALUES ('OSCAR evidence');
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var content =
                await File.ReadAllBytesAsync(path);

            var provider =
                new SqliteArtifactDiscoveryProvider(
                    new StubContentStore(content));

            var result =
                await provider.DiscoverAsync(
                    new ArtifactId("sqlite-001"));

            Assert.NotNull(result);
            Assert.Equal(
                "SQLite",
                result.Format);

            var inventory =
                Assert.IsType<
                    EMF.Inventory.Models.DatabaseStructure>(
                    result.Metadata["databaseStructure"]);

            var schema =
                Assert.Single(inventory.Schemas);

            var table =
                Assert.Single(schema.Tables);

            Assert.Equal(
                "evidence",
                table.Name);

            Assert.Equal(
                1,
                table.RowCount);

            Assert.Contains(
                table.Columns,
                column => column.Name == "id" &&
                          column.IsPrimaryKey);
        }
        finally
        {
            File.Delete(path);
        }
    }

    private sealed class StubContentStore :
        EMF.Core.Contracts.Storage.IArtifactContentStore
    {
        private readonly byte[]? _content;

        public StubContentStore(byte[]? content) =>
            _content = content;

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<byte[]?>(_content);
        }

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}

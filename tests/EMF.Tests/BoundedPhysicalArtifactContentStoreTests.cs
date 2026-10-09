using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;
using Microsoft.Data.Sqlite;
namespace EMF.Tests;

public sealed class BoundedPhysicalArtifactContentStoreTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
    private readonly ArtifactId _id = new("bounded-physical");
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    [Fact]
    public async Task BoundaryReadEvidenceAndLegacyCompatibility()
    {
        var store = new FileSystemArtifactContentStore(_root);
        await store.WriteAsync(_id, new byte[] { 1, 2, 3 });
        var snapshot = await store.ReadVersionedAsync(_id);
        int allocations = 0;
        store.BoundedReadAllocated = _ => allocations++;
        await using var lease = await store.ReadBoundedVersionedAsync(_id, new(3, 3, snapshot!.Revision));
        Assert.Equal(_id, lease!.ArtifactId); Assert.Equal(snapshot.Revision, lease.Revision);
        Assert.Equal(3, lease.StoredLength); Assert.Equal(new byte[] { 1, 2, 3 }, lease.Content.ToArray());
        Assert.Equal(1, allocations);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadBoundedVersionedAsync(_id, new(2, 3)));
        Assert.Equal(1, allocations);
        Assert.Equal(snapshot.Content, await store.ReadAsync(_id));
    }
    [Theory]
    [InlineData(2, 3)] [InlineData(3, 2)]
    public async Task EachCallerCeilingRejectedBeforeAllocation(long stored, long returned)
    {
        var store = new FileSystemArtifactContentStore(_root);
        await store.WriteAsync(_id, new byte[3]);
        bool reached = false;
        store.Checkpoint = p => { if (p == "BeforeReadAllocation") reached = true; return Task.CompletedTask; };
        store.BoundedReadAllocated = _ => throw new Exception("Payload allocation forbidden");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadBoundedVersionedAsync(_id, new(stored, returned)));
        Assert.False(reached);
    }
    [Fact]
    public async Task ProviderCeilingRejectedBeforeAllocation()
    {
        await new FileSystemArtifactContentStore(_root).WriteAsync(_id, new byte[3]);
        var store = new FileSystemArtifactContentStore(_root, 2);
        store.BoundedReadAllocated = _ => throw new Exception("Payload allocation forbidden");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadBoundedVersionedAsync(_id, new(3, 3)));
    }
    [Fact]
    public async Task ExpectedCurrentRevisionMismatchNeverAllocates()
    {
        var store = new FileSystemArtifactContentStore(_root);
        await store.WriteAsync(_id, new byte[3]);
        store.BoundedReadAllocated = _ => throw new Exception("Payload allocation forbidden");
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ReadBoundedVersionedAsync(_id, new(3, 3, new("other"))));
        Assert.Null(await store.ReadBoundedVersionedAsync(new("missing"), new(3, 3)));
    }
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task CorruptLengthRejectedBeforeAllocation(bool catalog)
    {
        var store = new FileSystemArtifactContentStore(_root);
        await store.WriteAsync(_id, new byte[3]);
        if (catalog)
        {
            using var connection = new SqliteConnection($"Data Source={Path.Combine(_root, ".content-catalog.sqlite")}");
            connection.Open(); using var command = connection.CreateCommand();
            command.CommandText = "UPDATE ContentState SET Length=2"; command.ExecuteNonQuery();
        }
        else await File.WriteAllBytesAsync(Directory.GetFiles(Path.Combine(_root, ".content-generations")).Single(), new byte[2]);
        store.BoundedReadAllocated = _ => throw new Exception("Payload allocation forbidden");
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadBoundedVersionedAsync(_id, new(3, 3)));
    }
    [Fact]
    public async Task CancellationAfterOwnedAllocationClearsCapturedArray()
    {
        var store = new FileSystemArtifactContentStore(_root);
        await store.WriteAsync(_id, new byte[] { 1, 2, 3 });
        using var cancellation = new CancellationTokenSource();
        byte[]? captured = null;
        store.BoundedReadAllocated = bytes => { captured = bytes; Array.Fill(bytes, (byte)9); cancellation.Cancel(); };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.ReadBoundedVersionedAsync(_id, new(3, 3), cancellation.Token));
        Assert.NotNull(captured); Assert.All(captured!, b => Assert.Equal(0, b));
    }
}

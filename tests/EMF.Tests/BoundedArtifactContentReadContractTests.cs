using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using System.Runtime.InteropServices;
namespace EMF.Tests;

public sealed class BoundedArtifactContentReadContractTests
{
    [Fact]
    public void RequestKeepsIndependentLimitsAndExpectedCurrentRevision()
    {
        var request = new BoundedArtifactContentReadRequest(200, 100, new("revision"));
        Assert.Equal(200, request.MaximumStoredRepresentationBytes);
        Assert.Equal(100, request.MaximumReturnedContentBytes);
        Assert.Equal(new ArtifactContentRevision("revision"), request.ExpectedRevision);
    }
    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(long.MaxValue)] [InlineData(2147483647)]
    public void RequestRejectsUnrepresentableSizes(long size)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedArtifactContentReadRequest(size, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new BoundedArtifactContentReadRequest(1, size));
    }
    [Fact]
    public async Task LeaseOwnsSameBufferStreamIsSeekableAndDisposalZerosEscapedView()
    {
        byte[] bytes = [1, 2, 3];
        var lease = new ArtifactContentReadLease(new("artifact"), new("revision"), 100, bytes);
        var borrowed = lease.Content;
        Assert.True(MemoryMarshal.TryGetArray(borrowed, out var segment));
        Assert.Same(bytes, segment.Array);
        Assert.Equal(100, lease.StoredLength);
        Assert.Equal(3, lease.ReturnedLength);
        using (var stream = lease.OpenReadStream())
        {
            Assert.True(stream.CanSeek); Assert.False(stream.CanWrite);
            bytes[1] = 9; stream.Position = 1; Assert.Equal(9, stream.ReadByte());
        }
        Assert.Equal(9, lease.Content.Span[1]); // Stream did not dispose owner.
        await lease.DisposeAsync(); lease.Dispose();
        Assert.All(bytes, b => Assert.Equal(0, b));
        Assert.All(borrowed.ToArray(), b => Assert.Equal(0, b));
        Assert.Throws<ObjectDisposedException>(() => lease.Content);
        Assert.Throws<ObjectDisposedException>(() => lease.OpenReadStream());
        Assert.Throws<ObjectDisposedException>(() => lease.Revision);
    }
}

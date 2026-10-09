using System.IO.Compression;
using EMF.Core.Contracts.Storage;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class ZipArchiveDecoderTests
{
    [Fact]
    public async Task SeekableLeaseStreamDecodesExistingParentAndLeavesCallerStreamOpen()
    {
        var bytes = CreateZip(("entry.txt", "bounded-parent"));
        using var lease = new ArtifactContentReadLease(new("zip"), new("revision"), bytes.Length, bytes);
        using var stream = lease.OpenReadStream();
        Stream? archiveInput = null;
        var decoder = new ZipArchiveDecoder { ArchiveInputObserved = input => archiveInput = input };
        var entries = await decoder.DecodeAsync(stream);
        Assert.Same(stream, archiveInput);
        Assert.Equal("bounded-parent", System.Text.Encoding.UTF8.GetString(entries.Single().Content));
        Assert.True(stream.CanRead); Assert.True(stream.CanSeek); Assert.Equal(bytes.Length, stream.Length);
        Assert.True(System.Runtime.InteropServices.MemoryMarshal.TryGetArray(lease.Content, out var segment));
        Assert.Same(bytes, segment.Array);
    }
    [Fact]
    public async Task StreamCeilingRejectsBeforeAnyArchiveRead()
    {
        using var stream = new TrackingStream(new byte[4]);
        await Assert.ThrowsAsync<InvalidDataException>(() => new ZipArchiveDecoder(maxInputBytes: 3).DecodeAsync(stream));
        Assert.Equal(0, stream.Reads); Assert.True(stream.CanRead);
    }
    [Fact]
    public async Task FailedArchiveLeavesOwnedInputForCallerCleanup()
    {
        byte[] bytes = [1, 2, 3];
        using var lease = new ArtifactContentReadLease(new("zip"), new("revision"), 3, bytes);
        using (var stream = lease.OpenReadStream())
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => new ZipArchiveDecoder().DecodeAsync(stream));
            Assert.True(stream.CanRead); Assert.Equal(new byte[] { 1, 2, 3 }, lease.Content.ToArray());
        }
        lease.Dispose(); Assert.All(bytes, b => Assert.Equal(0, b));
    }
    private sealed class TrackingStream(byte[] bytes) : MemoryStream(bytes, writable: false)
    {
        public int Reads;
        public override int Read(byte[] buffer, int offset, int count) { Reads++; return base.Read(buffer, offset, count); }
        public override int Read(Span<byte> buffer) { Reads++; return base.Read(buffer); }
    }

    [Fact]
    public async Task DecodeAsync_ReturnsFileEntries()
    {
        var content = CreateZip(
            ("one.txt", "alpha"),
            ("folder/two.txt", "beta"));

        var decoder = new ZipArchiveDecoder();

        var entries = await decoder.DecodeAsync(content);

        Assert.Equal(2, entries.Count);
        Assert.Equal("one.txt", entries[0].EntryName);
        Assert.Equal("alpha", System.Text.Encoding.UTF8.GetString(entries[0].Content));
        Assert.Equal("folder/two.txt", entries[1].EntryName);
        Assert.Equal("beta", System.Text.Encoding.UTF8.GetString(entries[1].Content));
    }

    [Fact]
    public async Task DecodeAsync_SkipsDirectories()
    {
        using var stream = new MemoryStream();

        using (var archive =
            new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            archive.CreateEntry("folder/");

            var entry = archive.CreateEntry("folder/file.txt");

            await using var writer =
                new StreamWriter(entry.Open());

            await writer.WriteAsync("content");
        }

        var decoder = new ZipArchiveDecoder();

        var entries =
            await decoder.DecodeAsync(stream.ToArray());

        Assert.Single(entries);
        Assert.Equal("folder/file.txt", entries[0].EntryName);
    }

    [Fact]
    public async Task DecodeAsync_RejectsTooManyEntries()
    {
        var content =
            CreateZip(
                ("one.txt", "alpha"),
                ("two.txt", "beta"));

        var decoder =
            new ZipArchiveDecoder(
                maxEntryCount: 1,
                maxEntryBytes: 1024,
                maxTotalBytes: 2048);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => decoder.DecodeAsync(content));

        Assert.Equal(
            "ZIP archive exceeds the maximum allowed entry count.",
            ex.Message);
    }

    [Fact]
    public async Task DecodeAsync_RejectsOversizedEntry()
    {
        var content =
            CreateZip(
                ("one.txt", "alpha"));

        var decoder =
            new ZipArchiveDecoder(
                maxEntryCount: 10,
                maxEntryBytes: 4,
                maxTotalBytes: 16);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => decoder.DecodeAsync(content));

        Assert.Contains(
            "exceeds the maximum allowed size",
            ex.Message);
    }

    [Fact]
    public async Task DecodeAsync_RejectsOversizedExtractedTotal()
    {
        var content =
            CreateZip(
                ("one.txt", "abc"),
                ("two.txt", "def"));

        var decoder =
            new ZipArchiveDecoder(
                maxEntryCount: 10,
                maxEntryBytes: 16,
                maxTotalBytes: 5);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => decoder.DecodeAsync(content));

        Assert.Equal(
            "ZIP archive exceeds the maximum allowed extracted size.",
            ex.Message);
    }

    [Fact]
    public async Task DecodeAsync_RejectsOversizedInput()
    {
        var decoder =
            new ZipArchiveDecoder(
                maxInputBytes: 1);

        var ex =
            await Assert.ThrowsAsync<InvalidDataException>(
                () => decoder.DecodeAsync(new byte[2]));

        Assert.Equal(
            "ZIP input exceeds the maximum allowed size.",
            ex.Message);
    }

    private static byte[] CreateZip(
        params (string Name, string Content)[] entries)
    {
        using var stream = new MemoryStream();

        using (var archive =
            new ZipArchive(stream, ZipArchiveMode.Create, true))
        {
            foreach (var item in entries)
            {
                var entry = archive.CreateEntry(item.Name);

                using var writer =
                    new StreamWriter(entry.Open());

                writer.Write(item.Content);
            }
        }

        return stream.ToArray();
    }
}

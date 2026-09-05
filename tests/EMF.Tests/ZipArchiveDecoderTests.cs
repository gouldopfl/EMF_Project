using System.IO.Compression;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class ZipArchiveDecoderTests
{
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

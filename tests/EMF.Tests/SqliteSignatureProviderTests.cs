using EMF.Discovery.Services;

namespace EMF.Tests;

public sealed class SqliteSignatureProviderTests
{
    [Fact]
    public void TryDetect_RecognizesSqlite()
    {
        var provider = new SqliteSignatureProvider();
        var content = "SQLite format 3\0test"u8.ToArray();

        var detected =
            provider.TryDetect(
                content,
                out var contentType,
                out var format);

        Assert.True(detected);
        Assert.Equal("application/x-sqlite3", contentType);
        Assert.Equal("SQLite", format);
    }

    [Fact]
    public void TryDetect_RejectsShortContent()
    {
        var provider = new SqliteSignatureProvider();

        Assert.False(
            provider.TryDetect(
                Array.Empty<byte>(),
                out _,
                out _));
    }

    [Fact]
    public void TryDetect_RejectsWrongSignature()
    {
        var provider = new SqliteSignatureProvider();

        Assert.False(
            provider.TryDetect(
                "not a database"u8.ToArray(),
                out _,
                out _));
    }
}

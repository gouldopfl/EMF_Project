using System.Text;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;

namespace EMF.Tests;

public sealed class FileSystemArtifactContentStoreTests
{

    [Fact]
    public async Task WriteAsync_CreatesPrivateDirectoryAndFile()
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        try
        {
            var store = new FileSystemArtifactContentStore(root);
            await store.WriteAsync(new ArtifactId("artifact"), new byte[] { 1 });
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
                File.GetUnixFileMode(root));
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                File.GetUnixFileMode(Path.Combine(root, "artifact")));
        }
        finally { if (Directory.Exists(root)) Directory.Delete(root, true); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Operations_RejectSymbolicLinksIncludingDanglingLinks(bool dangling)
    {
        if (OperatingSystem.IsWindows()) return;
        var root = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(root);
        try
        {
            var outside = Path.Combine(root, "outside");
            if (!dangling) await File.WriteAllTextAsync(outside, "synthetic secret");
            File.CreateSymbolicLink(Path.Combine(root, "artifact"), outside);
            var store = new FileSystemArtifactContentStore(root);
            var id = new ArtifactId("artifact");
            await Assert.ThrowsAsync<IOException>(() => store.ReadAsync(id));
            await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(id, new byte[] { 1 }));
            await Assert.ThrowsAsync<IOException>(() => store.DeleteAsync(id));
            if (!dangling) Assert.Equal("synthetic secret", await File.ReadAllTextAsync(outside));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public async Task Operations_RejectContentRootRedirectedBySymbolicLink()
    {
        if (OperatingSystem.IsWindows()) return;
        var parent = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
        Directory.CreateDirectory(parent);
        try
        {
            var actual = Directory.CreateDirectory(Path.Combine(parent, "actual")).FullName;
            var link = Path.Combine(parent, "link");
            Directory.CreateSymbolicLink(link, actual);
            var store = new FileSystemArtifactContentStore(Path.Combine(link, "content"));
            await Assert.ThrowsAsync<IOException>(() => store.WriteAsync(new ArtifactId("artifact"), new byte[] { 1 }));
            Assert.False(Directory.Exists(Path.Combine(actual, "content")));
        }
        finally { Directory.Delete(parent, true); }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Constructor_RejectsNonPositiveStoredSizeLimit(
        long value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new FileSystemArtifactContentStore(
                Path.GetTempPath(),
                value));
    }

    [Fact]
    public async Task WriteAsync_RejectsContentOverStoredSizeLimit()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        try
        {
            var store =
                new FileSystemArtifactContentStore(
                    root,
                    maxStoredBytes: 4);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => store.WriteAsync(
                    new ArtifactId("artifact-too-large"),
                    new byte[5]));

            Assert.False(
                Directory.Exists(root) &&
                Directory.EnumerateFiles(root).Any());
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ReadAsync_RejectsOversizedBackingFile()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        Directory.CreateDirectory(root);

        try
        {
            var id =
                new ArtifactId("artifact-too-large");

            await File.WriteAllBytesAsync(
                Path.Combine(root, id.Value),
                new byte[5]);

            var store =
                new FileSystemArtifactContentStore(
                    root,
                    maxStoredBytes: 4);

            await Assert.ThrowsAsync<InvalidDataException>(
                () => store.ReadAsync(id));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }


    [Fact]
    public async Task DeleteAsync_RemovesStoredContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var store = new FileSystemArtifactContentStore(root);
            var id = new ArtifactId("artifact-delete");
            var content = Encoding.UTF8.GetBytes("delete me");

            await store.WriteAsync(id, content);
            await store.DeleteAsync(id);

            var result = await store.ReadAsync(id);

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }


    [Fact]
    public async Task WriteAsync_ReplacesExistingContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var store = new FileSystemArtifactContentStore(root);
            var id = new ArtifactId("artifact-replace");

            var first =
                Encoding.UTF8.GetBytes("first content");

            var second =
                Encoding.UTF8.GetBytes("second content");

            await store.WriteAsync(id, first);
            await store.WriteAsync(id, second);

            var result = await store.ReadAsync(id);

            Assert.Equal(second, result);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }


    [Fact]
    public async Task WriteAsync_DoesNotEscapeRootPath()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        var outside =
            Path.Combine(
                Path.GetDirectoryName(root)!,
                "artifact-escape");

        try
        {
            var store =
                new FileSystemArtifactContentStore(root);

            var id =
                new ArtifactId("../artifact-escape");

            var content =
                Encoding.UTF8.GetBytes("must stay inside root");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.WriteAsync(id, content));

            Assert.False(File.Exists(outside));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);

            if (File.Exists(outside))
                File.Delete(outside);
        }
    }


    [Theory]
    [InlineData("nested/artifact")]
    [InlineData(@"nested\artifact")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("/rooted-artifact")]
    public async Task Operations_RejectPathLikeArtifactIds(
        string value)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var store =
                new FileSystemArtifactContentStore(root);
            var id = new ArtifactId(value);
            var content =
                Encoding.UTF8.GetBytes("protected content");

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.WriteAsync(id, content));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.ReadAsync(id));

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => store.DeleteAsync(id));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task Operations_RejectDefaultArtifactId()
    {
        var store =
            new FileSystemArtifactContentStore(
                Path.GetTempPath());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.ReadAsync(default));
    }

    [Fact]
    public async Task WriteThenRead_RoundTripsContent()
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            Guid.NewGuid().ToString());

        try
        {
            var store = new FileSystemArtifactContentStore(root);
            var id = new ArtifactId("artifact-1");
            var content = Encoding.UTF8.GetBytes("hello emf");

            await store.WriteAsync(id, content);

            var result = await store.ReadAsync(id);

            Assert.Equal(content, result);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task WriteAsync_CancellationPreservesExistingContent()
    {
        var root =
            Path.Combine(
                Path.GetTempPath(),
                Guid.NewGuid().ToString());

        try
        {
            var store =
                new FileSystemArtifactContentStore(root);

            var id =
                new ArtifactId("artifact-cancel");

            var original =
                Encoding.UTF8.GetBytes("original");

            await store.WriteAsync(id, original);

            using var cancellation =
                new CancellationTokenSource();

            cancellation.Cancel();

            await Assert.ThrowsAnyAsync<
                OperationCanceledException>(
                    () => store.WriteAsync(
                        id,
                        Encoding.UTF8.GetBytes("replacement"),
                        cancellation.Token));

            Assert.Equal(
                original,
                await store.ReadAsync(id));

            Assert.Single(
                Directory.GetFiles(root));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, true);
            }
        }
    }
}

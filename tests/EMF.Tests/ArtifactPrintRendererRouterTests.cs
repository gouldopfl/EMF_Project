using System.Reflection;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ArtifactPrintRendererRouterTests
{
    [Fact]
    public async Task RenderAsync_ReturnsEmptyWhenArtifactMissing()
    {
        var router = new ArtifactPrintRendererRouter(
            new InMemoryEvidenceRepository(),
            new DefaultArtifactContentTypeResolver(),
            []);

        var result =
            await router.RenderAsync(new ArtifactId("missing"));

        Assert.Empty(result);
    }

    [Fact]
    public async Task RenderAsync_RejectsDifferentReturnedArtifact()
    {
        var requested = new ArtifactId("artifact-001");
        var returned = CreateArtifact("artifact-other", ".pdf");

        var repository =
            Proxy<IEvidenceRepository>(
                (method, args) =>
                    method.Name == "GetArtifactAsync"
                        ? Task.FromResult<Artifact?>(returned)
                        : throw new NotSupportedException());

        var router = new ArtifactPrintRendererRouter(
            repository,
            new DefaultArtifactContentTypeResolver(),
            [new StubProvider("application/pdf")]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.RenderAsync(requested));
    }

    [Fact]
    public async Task RenderAsync_ReturnsEmptyWhenContentTypeUnknown()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-002", ".bin");

        await repository.AddArtifactAsync(artifact);

        var router = new ArtifactPrintRendererRouter(
            repository,
            new DefaultArtifactContentTypeResolver(),
            []);

        Assert.Empty(await router.RenderAsync(artifact.Id));
    }

    [Fact]
    public async Task RenderAsync_RejectsUnsupportedContentType()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-003", ".pdf");

        await repository.AddArtifactAsync(artifact);

        var router = new ArtifactPrintRendererRouter(
            repository,
            new DefaultArtifactContentTypeResolver(),
            []);

        await Assert.ThrowsAsync<NotSupportedException>(
            () => router.RenderAsync(artifact.Id));
    }

    [Fact]
    public async Task RenderAsync_UsesMatchingProvider()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-004", ".pdf");

        await repository.AddArtifactAsync(artifact);

        var router = new ArtifactPrintRendererRouter(
            repository,
            new DefaultArtifactContentTypeResolver(),
            [new StubProvider("application/pdf")]);

        var page =
            Assert.Single(await router.RenderAsync(artifact.Id));

        Assert.Equal(1, page.PageNumber);
        Assert.Equal("image/png", page.ContentType);
        Assert.Equal(
            new byte[] { 1, 2, 3 },
            page.Content.ToArray());
    }

    private static Artifact CreateArtifact(
        string id,
        string extension) =>
        new()
        {
            Id = new ArtifactId(id),
            Name = $"evidence{extension}",
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] = extension
            }
        };

    private sealed class StubProvider :
        IArtifactPrintRenderingProvider
    {
        private readonly string _contentType;

        public StubProvider(string contentType) =>
            _contentType = contentType;

        public bool CanRender(string contentType) =>
            string.Equals(
                contentType,
                _contentType,
                StringComparison.OrdinalIgnoreCase);

        public Task<IReadOnlyList<PrintableArtifactPage>> RenderAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PrintableArtifactPage>>(
                [
                    new PrintableArtifactPage
                    {
                        PageNumber = 1,
                        ContentType = "image/png",
                        Content = new byte[] { 1, 2, 3 }
                    }
                ]);
    }

    private static T Proxy<T>(
        Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestDispatchProxy>();
        ((TestDispatchProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class TestDispatchProxy : DispatchProxy
    {
        public required Func<MethodInfo, object?[]?, object?> Handler
        { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            Handler(
                targetMethod ??
                    throw new InvalidOperationException(),
                args);
    }
}


using System.Reflection;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ArtifactDiscoveryRouterTests
{
    [Fact]
    public async Task DiscoverAsync_ReturnsNullWhenArtifactMissing()
    {
        var router = new ArtifactDiscoveryRouter(
            new InMemoryEvidenceRepository(),
            new DefaultArtifactContentTypeResolver(),
            []);

        Assert.Null(await router.DiscoverAsync(
            new ArtifactId("missing")));
    }

    [Fact]
    public async Task DiscoverAsync_RejectsDifferentReturnedArtifact()
    {
        var requested = new ArtifactId("artifact-001");
        var returned = CreateArtifact("artifact-other", ".txt");

        var repository =
            Proxy<IEvidenceRepository>(
                (method, args) =>
                    method.Name == "GetArtifactAsync"
                        ? Task.FromResult<Artifact?>(returned)
                        : throw new NotSupportedException());

        var router =
            new ArtifactDiscoveryRouter(
                repository,
                new DefaultArtifactContentTypeResolver(),
                [new StubProvider("text/plain")]);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => router.DiscoverAsync(requested));
    }

    [Fact]
    public async Task DiscoverAsync_ReturnsNullWhenContentTypeUnknown()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-001", ".bin");

        await repository.AddArtifactAsync(artifact);

        var router = new ArtifactDiscoveryRouter(
            repository,
            new DefaultArtifactContentTypeResolver(),
            []);

        Assert.Null(await router.DiscoverAsync(artifact.Id));
    }

    [Fact]
    public async Task DiscoverAsync_UsesMatchingProvider()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("artifact-002", ".txt");

        await repository.AddArtifactAsync(artifact);

        var router = new ArtifactDiscoveryRouter(
            repository,
            new DefaultArtifactContentTypeResolver(),
            [new StubProvider("text/plain")]);

        var result = await router.DiscoverAsync(artifact.Id);

        Assert.NotNull(result);
        Assert.Equal("text/plain", result.ContentType);
        Assert.Equal("Test", result.Format);
    }

    [Fact]
    public async Task DiscoverAsync_RoutesSqliteToDiscoveryProvider()
    {
        var repository = new InMemoryEvidenceRepository();
        var artifact = CreateArtifact("sqlite-001", ".db");

        await repository.AddArtifactAsync(artifact);

        var router = new ArtifactDiscoveryRouter(
            repository,
            new DefaultArtifactContentTypeResolver(),
            [new StubProvider("application/x-sqlite3")]);

        var result = await router.DiscoverAsync(artifact.Id);

        Assert.NotNull(result);
        Assert.Equal("application/x-sqlite3", result.ContentType);
    }

    private static T Proxy<T>(
        Func<MethodInfo, object?[]?, object?> handler)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Handler = handler;
        return proxy;
    }

    private class TestProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?>? Handler
            { get; set; }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            Handler!(targetMethod!, args);
    }

    private static Artifact CreateArtifact(
        string id,
        string extension) =>
        new()
        {
            Id = new ArtifactId(id),
            Name = "evidence" + extension,
            ArtifactType = "file",
            Metadata = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.FileExtension] = extension
            }
        };

    private sealed class StubProvider : IArtifactDiscoveryProvider
    {
        private readonly string _contentType;

        public StubProvider(string contentType) =>
            _contentType = contentType;

        public bool CanDiscover(string contentType) =>
            string.Equals(
                _contentType,
                contentType,
                StringComparison.OrdinalIgnoreCase);

        public Task<ArtifactDiscoveryResult?> DiscoverAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<ArtifactDiscoveryResult?>(
                new ArtifactDiscoveryResult
                {
                    ContentType = _contentType,
                    Format = "Test"
                });
    }
}

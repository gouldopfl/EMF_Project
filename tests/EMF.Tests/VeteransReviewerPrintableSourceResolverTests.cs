using System.Text;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class VeteransReviewerPrintableSourceResolverTests
{
    [Theory]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    public async Task Resolve_UsesBoundedNativeParentAndKeepsSourceCoordinates(string extension)
    {
        var (repository, child, parent) = await Fixture(extension);
        var renderer = new RecordingRenderer();
        var result = await new VeteransReviewerPrintableSourceResolver(repository, renderer).ResolveAsync(child, default);
        Assert.Equal(parent.Id, result.SourceArtifactId);
        Assert.False(result.IsExtractedTextFallback);
        Assert.Equal(new[] { 2, 3, 4 }, result.Pages.Select(p => p.PageNumber));
        Assert.Equal((parent.Id, 2, 4), Assert.Single(renderer.Ranges));
        Assert.Empty(renderer.FullCalls);
        Assert.Equal("2", child.Metadata[VeteransArtifactMetadataKeys.SourceStartPage]);
        Assert.Single(await repository.GetRelationshipsAsync(child.Id));
    }

    [Theory]
    [InlineData(null, "4")]
    [InlineData("2", null)]
    [InlineData("0", "4")]
    [InlineData("4", "2")]
    [InlineData("bad", "4")]
    [InlineData(null, null)]
    public async Task Resolve_RejectsInvalidMappingRatherThanFallingBack(string? start, string? end)
    {
        var (repository, child, _) = await Fixture(".pdf", start, end);
        var renderer = new RecordingRenderer();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new VeteransReviewerPrintableSourceResolver(repository, renderer).ResolveAsync(child, default));
        Assert.Empty(renderer.FullCalls);
        Assert.Empty(renderer.Ranges);
    }

    [Theory]
    [InlineData(".txt", null, null)]
    public async Task Resolve_MarksExtractedFallbackWhenNoBoundedNativeRepresentationExists(
        string extension, string? start, string? end)
    {
        var (repository, child, _) = await Fixture(extension, start, end);
        var renderer = new RecordingRenderer();
        var result = await new VeteransReviewerPrintableSourceResolver(repository, renderer).ResolveAsync(child, default);
        Assert.Null(result.SourceArtifactId);
        Assert.True(result.IsExtractedTextFallback);
        Assert.Equal(child.Id, Assert.Single(renderer.FullCalls));
        Assert.Equal("text/plain", Assert.Single(result.Pages).ContentType);
    }

    [Fact]
    public async Task Resolve_DoesNotHideNativeRenderFailure()
    {
        var (repository, child, _) = await Fixture(".pdf");
        var renderer = new RecordingRenderer { Fail = true };
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new VeteransReviewerPrintableSourceResolver(repository, renderer).ResolveAsync(child, default));
        Assert.Empty(renderer.FullCalls);
    }

    [Fact]
    public async Task Resolve_RejectsAmbiguousParentsBeforeRendering()
    {
        var (repository, child, _) = await Fixture(".pdf");
        await repository.AddRelationshipAsync(new Relationship
        {
            SourceArtifactId = child.Id, TargetArtifactId = new("other-parent"), RelationshipType = RelationshipTypes.DerivedFrom
        });
        var renderer = new RecordingRenderer();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new VeteransReviewerPrintableSourceResolver(repository, renderer).ResolveAsync(child, default));
        Assert.Empty(renderer.FullCalls);
        Assert.Empty(renderer.Ranges);
    }

    [Theory]
    [InlineData("", "image/png")]
    [InlineData("1,2", "image/png")]
    [InlineData("2,5", "image/png")]
    [InlineData("3,2", "image/png")]
    [InlineData("2,2", "image/png")]
    [InlineData("2,4", "image/png")]
    [InlineData("3,4", "image/png")]
    [InlineData("2,3", "image/png")]
    [InlineData("2,3,4", "text/plain")]
    public async Task Resolve_RejectsInvalidNativePagesWithoutRenderingExtraction(string numbers, string contentType)
    {
        var (repository, child, _) = await Fixture(".pdf");
        var renderer = new RecordingRenderer
        {
            RangePages = numbers.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(number => new PrintableArtifactPage
                {
                    PageNumber = int.Parse(number), ContentType = contentType,
                    Content = Encoding.UTF8.GetBytes("invalid source representation")
                }).ToArray()
        };
        var resolver = new VeteransReviewerPrintableSourceResolver(repository, renderer);
        if (numbers.Length == 0)
            await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync(child, default));
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => resolver.ResolveAsync(child, default));
        Assert.Empty(renderer.FullCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Resolve_BlueButtonRequiresVerifiedRecordBoundaries(bool withBoundaries)
    {
        var (repository, original, _) = await Fixture(".pdf");
        var metadata = new Dictionary<string, object>(original.Metadata)
        {
            [ArtifactMetadataKeys.SourceType] = "veterans-clinical-note"
        };
        if (withBoundaries)
        {
            metadata[VeteransArtifactMetadataKeys.SourceStartText] = "ER STAFF ASSESSMENT";
            metadata[VeteransArtifactMetadataKeys.SourceEndText] = "Signed";
        }
        var child = new Artifact { Id = original.Id, Name = original.Name, ArtifactType = original.ArtifactType, Metadata = metadata };
        // Use an independent fixture to keep the evidence repository append-only.
        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(child);
        // Renaming a Blue Button source must not disable its boundary enforcement.
        var parent = new Artifact { Id = new("source"), Name = "renamed-source.pdf", ArtifactType = "file" };
        await evidence.AddArtifactAsync(parent);
        await evidence.AddRelationshipAsync(new Relationship { SourceArtifactId = child.Id,
            TargetArtifactId = parent.Id, RelationshipType = RelationshipTypes.DerivedFrom });
        var renderer = new RecordingRenderer { RangePages = [
            VeteransReviewerNativeExcerptTests.Page(2, "Previous record", "ER STAFF ASSESSMENT"),
            VeteransReviewerNativeExcerptTests.Page(3, "Bilateral pes planus"),
            VeteransReviewerNativeExcerptTests.Page(4, "Signed", "AUDIO HEARING AID CHECK")
        ] };
        var resolver = new VeteransReviewerPrintableSourceResolver(evidence, renderer);
        if (!withBoundaries)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => resolver.ResolveAsync(child, default));
            return;
        }
        var result = await resolver.ResolveAsync(child, default);
        var text = string.Join(" ", result.Pages.SelectMany(p => p.TextGeometry!.Glyphs).Select(g => g.Text));
        Assert.Contains("Bilateral pes planus", text);
        Assert.DoesNotContain("Previous record", text);
        Assert.DoesNotContain("AUDIO HEARING AID CHECK", text);
        Assert.Equal(parent.Id, result.SourceArtifactId);
    }

    [Fact]
    public async Task Resolve_RejectsDeclaredBoundariesWithoutParent()
    {
        var (_, child, _) = await Fixture(".pdf");
        var repository = new InMemoryEvidenceRepository();
        await repository.AddArtifactAsync(child);
        var renderer = new RecordingRenderer();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new VeteransReviewerPrintableSourceResolver(repository, renderer).ResolveAsync(child, default));
        Assert.Empty(renderer.FullCalls);
    }

    [Fact]
    public async Task Resolve_RejectsDeclaredNativeRangeOnUnsupportedParent()
    {
        var (repository, child, _) = await Fixture(".txt");
        var renderer = new RecordingRenderer();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new VeteransReviewerPrintableSourceResolver(repository, renderer).ResolveAsync(child, default));
        Assert.Empty(renderer.FullCalls);
    }

    [Theory]
    [InlineData(".pdf")]
    [InlineData(".docx")]
    [InlineData(".bin")]
    public async Task Resolve_RejectsContradictoryBoundedFormatBeforeRendering(string extension)
    {
        var (_, original, _) = await Fixture(".pdf");
        var child = new Artifact
        {
            Id = original.Id, Name = "renamed" + extension, ArtifactType = "derived-text",
            Metadata = new Dictionary<string, object>(original.Metadata)
            { [ArtifactMetadataKeys.FileExtension] = extension }
        };
        var evidence = new InMemoryEvidenceRepository();
        await evidence.AddArtifactAsync(child);
        var renderer = new RecordingRenderer();
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new VeteransReviewerPrintableSourceResolver(evidence, renderer).ResolveAsync(child, default));
        Assert.Empty(renderer.FullCalls);
        Assert.Empty(renderer.Ranges);
    }

    private static async Task<(InMemoryEvidenceRepository Repository, Artifact Child, Artifact Parent)> Fixture(
        string extension, string? start = "2", string? end = "4")
    {
        var metadata = new Dictionary<string, object> { [ArtifactMetadataKeys.FileExtension] = ".txt" };
        if (start is not null) metadata[VeteransArtifactMetadataKeys.SourceStartPage] = start;
        if (end is not null) metadata[VeteransArtifactMetadataKeys.SourceEndPage] = end;
        var child = new Artifact { Id = new("excerpt"), Name = "excerpt.txt", ArtifactType = "derived-text", Metadata = metadata };
        var parent = new Artifact { Id = new("source"), Name = "source" + extension, ArtifactType = "file",
            Metadata = new Dictionary<string, object> { [ArtifactMetadataKeys.FileExtension] = extension } };
        var repository = new InMemoryEvidenceRepository();
        await repository.AddArtifactAsync(child);
        await repository.AddArtifactAsync(parent);
        await repository.AddRelationshipAsync(new Relationship
        {
            SourceArtifactId = child.Id, TargetArtifactId = parent.Id, RelationshipType = RelationshipTypes.DerivedFrom
        });
        return (repository, child, parent);
    }

    private sealed class RecordingRenderer : IArtifactPrintRenderer, IArtifactPageRangePrintRenderer
    {
        public List<ArtifactId> FullCalls { get; } = [];
        public List<(ArtifactId, int, int)> Ranges { get; } = [];
        public bool Fail { get; init; }
        public IReadOnlyList<PrintableArtifactPage>? RangePages { get; init; }
        public Task<IReadOnlyList<PrintableArtifactPage>> RenderAsync(ArtifactId id, CancellationToken cancellationToken = default)
        {
            FullCalls.Add(id);
            return Task.FromResult<IReadOnlyList<PrintableArtifactPage>>([
                new() { PageNumber = 1, ContentType = "text/plain", Content = Encoding.UTF8.GetBytes("fragmented\ntext") }
            ]);
        }
        public Task<IReadOnlyList<PrintableArtifactPage>> RenderRangeAsync(ArtifactId id, int start, int end, CancellationToken cancellationToken = default)
        {
            Ranges.Add((id, start, end));
            if (Fail) throw new InvalidDataException("Invalid native PDF.");
            if (RangePages is not null) return Task.FromResult(RangePages);
            return Task.FromResult<IReadOnlyList<PrintableArtifactPage>>(Enumerable.Range(start, end - start + 1)
                .Select(n => new PrintableArtifactPage { PageNumber = n, ContentType = "image/png", Content = new byte[] { (byte)n } }).ToArray());
        }
    }
}

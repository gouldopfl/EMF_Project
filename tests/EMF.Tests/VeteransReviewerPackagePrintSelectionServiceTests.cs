using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackagePrintSelectionServiceTests
{
    private readonly VeteransReviewerPackagePrintSelectionService _service = new();

    [Fact]
    public void Normalize_AllPreservesCanonicalWholePackageSelection()
    {
        var result =
            _service.Normalize(
                new VeteransReviewerPackagePrintSelection
                {
                    Mode = VeteransReviewerPackagePrintSelectionMode.All
                });

        Assert.Equal(VeteransReviewerPackagePrintSelectionMode.All, result.Mode);
        Assert.False(result.RequiresFixedLayoutPagination);
        Assert.Null(result.CurrentPage);
        Assert.Empty(result.PageRanges);
        Assert.Empty(result.SectionIds);
    }

    [Fact]
    public void Normalize_CurrentPageRequiresFixedLayoutPagination()
    {
        var result =
            _service.Normalize(
                new VeteransReviewerPackagePrintSelection
                {
                    Mode = VeteransReviewerPackagePrintSelectionMode.CurrentPage,
                    CurrentPage = 7
                });

        Assert.Equal(7, result.CurrentPage);
        Assert.True(result.RequiresFixedLayoutPagination);
    }

    [Fact]
    public void Normalize_PageRangesSortsAndMergesOverlappingAndAdjacentRanges()
    {
        var result =
            _service.Normalize(
                new VeteransReviewerPackagePrintSelection
                {
                    Mode = VeteransReviewerPackagePrintSelectionMode.PageRange,
                    PageRanges =
                    [
                        new VeteransReviewerPackagePageRange
                        {
                            StartPage = 8,
                            EndPage = 10
                        },
                        new VeteransReviewerPackagePageRange
                        {
                            StartPage = 2,
                            EndPage = 4
                        },
                        new VeteransReviewerPackagePageRange
                        {
                            StartPage = 5,
                            EndPage = 7
                        },
                        new VeteransReviewerPackagePageRange
                        {
                            StartPage = 14,
                            EndPage = 15
                        }
                    ]
                });

        Assert.True(result.RequiresFixedLayoutPagination);
        Assert.Equal(2, result.PageRanges.Count);
        Assert.Equal(2, result.PageRanges[0].StartPage);
        Assert.Equal(10, result.PageRanges[0].EndPage);
        Assert.Equal(14, result.PageRanges[1].StartPage);
        Assert.Equal(15, result.PageRanges[1].EndPage);
    }

    [Fact]
    public void Normalize_SectionsUsesStableHeaderCatalogOrder()
    {
        var result =
            _service.Normalize(
                new VeteransReviewerPackagePrintSelection
                {
                    Mode = VeteransReviewerPackagePrintSelectionMode.Sections,
                    SectionIds =
                    [
                        VeteransReviewerPackageSectionCatalog.EvidenceAppendices,
                        VeteransReviewerPackageSectionCatalog.ExecutiveSummary,
                        VeteransReviewerPackageSectionCatalog.ClinicalProgression,
                        VeteransReviewerPackageSectionCatalog.ExecutiveSummary
                    ]
                });

        Assert.False(result.RequiresFixedLayoutPagination);
        Assert.Equal(
            [
                VeteransReviewerPackageSectionCatalog.ExecutiveSummary,
                VeteransReviewerPackageSectionCatalog.ClinicalProgression,
                VeteransReviewerPackageSectionCatalog.EvidenceAppendices
            ],
            result.SectionIds);
    }

    [Fact]
    public void Normalize_SectionsRejectsUnknownHeader()
    {
        var ex =
            Assert.Throws<InvalidOperationException>(
                () => _service.Normalize(
                    new VeteransReviewerPackagePrintSelection
                    {
                        Mode = VeteransReviewerPackagePrintSelectionMode.Sections,
                        SectionIds = ["not-a-real-section"]
                    }));

        Assert.Contains("unknown section", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Normalize_RejectsConflictingModes()
    {
        var ex =
            Assert.Throws<InvalidOperationException>(
                () => _service.Normalize(
                    new VeteransReviewerPackagePrintSelection
                    {
                        Mode = VeteransReviewerPackagePrintSelectionMode.CurrentPage,
                        CurrentPage = 4,
                        SectionIds =
                            [VeteransReviewerPackageSectionCatalog.ExecutiveSummary]
                    }));

        Assert.Contains("conflicting", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}

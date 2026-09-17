namespace EMF.Extensions.VeteransClaims.Orchestration;

public enum VeteransReviewerPackagePrintSelectionMode
{
    All,
    CurrentPage,
    PageRange,
    Sections
}

public sealed record VeteransReviewerPackagePageRange
{
    public required int StartPage { get; init; }

    public required int EndPage { get; init; }
}

public sealed class VeteransReviewerPackagePrintSelection
{
    public required VeteransReviewerPackagePrintSelectionMode Mode
    { get; init; }

    public int? CurrentPage { get; init; }

    public IReadOnlyList<VeteransReviewerPackagePageRange> PageRanges
    { get; init; } = Array.Empty<VeteransReviewerPackagePageRange>();

    public IReadOnlyList<string> SectionIds
    { get; init; } = Array.Empty<string>();

    public bool RequiresFixedLayoutPagination =>
        Mode is
            VeteransReviewerPackagePrintSelectionMode.CurrentPage or
            VeteransReviewerPackagePrintSelectionMode.PageRange;
}

using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPageSelectorTests
{
    [Fact]
    public void Select_ExpandsReviewerPageSelection()
    {
        var pages = Enumerable.Range(1, 17).Select(Page).ToArray();

        var result =
            VeteransReviewerPageSelector.Select(
                pages,
                "11,13-17");

        Assert.Equal(
            [11, 13, 14, 15, 16, 17],
            result.Select(x => x.PageNumber));
    }

    [Fact]
    public void Select_PreservesRequestedOrder()
    {
        var result =
            VeteransReviewerPageSelector.Select(
                [Page(1), Page(2), Page(3)],
                "3,1");

        Assert.Equal(
            [3, 1],
            result.Select(x => x.PageNumber));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("3-1")]
    [InlineData("1,1")]
    [InlineData("1,,2")]
    public void Select_RejectsInvalidSelection(string selection)
    {
        Assert.Throws<InvalidDataException>(
            () => VeteransReviewerPageSelector.Select(
                [Page(1), Page(2), Page(3)],
                selection));
    }

    [Fact]
    public void Select_RejectsUnavailablePage()
    {
        Assert.Throws<InvalidDataException>(
            () => VeteransReviewerPageSelector.Select(
                [Page(1), Page(2), Page(3)],
                "1,4"));
    }

    [Fact]
    public void Select_RejectsDuplicateSourcePageNumbers()
    {
        Assert.Throws<InvalidDataException>(
            () => VeteransReviewerPageSelector.Select(
                [Page(1), Page(1)],
                "1"));
    }

    [Fact]
    public void Select_RejectsExcessiveRangeWithoutExpansion()
    {
        Assert.Throws<InvalidDataException>(
            () => VeteransReviewerPageSelector.Select(
                [Page(1), Page(2), Page(3)],
                "1-2147483647"));
    }

    private static PrintableArtifactPage Page(int number) =>
        new()
        {
            PageNumber = number,
            ContentType = "image/png",
            Content = ReadOnlyMemory<byte>.Empty
        };
}

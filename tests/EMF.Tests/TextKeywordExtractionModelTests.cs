using EMF.Intelligence.Capabilities;

namespace EMF.Tests;

public sealed class TextKeywordExtractionModelTests
{
    [Fact]
    public void Request_PreservesSettingsAndExclusions()
    {
        var request =
            new TextKeywordExtractionRequest(
                "Evidence evidence review.",
                5,
                4,
                [" Review ", "review", "custom"]);

        Assert.Equal(5, request.MaximumKeywords);
        Assert.Equal(
            4,
            request.MinimumKeywordLength);

        Assert.Equal(
            ["Review", "custom"],
            request.ExcludedTerms);
    }

    [Fact]
    public void Request_RejectsInvalidMaximum()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new TextKeywordExtractionRequest(
                "Source text.",
                0));
    }

    [Fact]
    public void Keyword_SortsAndExposesOffsets()
    {
        var keyword =
            new TextKeyword(
                "evidence",
                [20, 0, 10]);

        Assert.Equal("evidence", keyword.Term);
        Assert.Equal([0, 10, 20], keyword.Offsets);
        Assert.Equal(3, keyword.Occurrences);
        Assert.Equal(0, keyword.FirstOffset);
    }

    [Fact]
    public void Keyword_RejectsDuplicateOffsets()
    {
        Assert.Throws<ArgumentException>(
            () => new TextKeyword(
                "evidence",
                [0, 0]));
    }
}

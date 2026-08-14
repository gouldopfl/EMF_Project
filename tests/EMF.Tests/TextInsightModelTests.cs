using EMF.Intelligence.Capabilities;

namespace EMF.Tests;

public sealed class TextInsightModelTests
{
    [Fact]
    public void Request_PreservesAnalysisSettings()
    {
        var request =
            new TextInsightRequest(
                "Evidence evidence policy.",
                100,
                5,
                4,
                ["policy"]);

        Assert.Equal(
            100,
            request.MaximumSummaryCharacters);
        Assert.Equal(5, request.MaximumKeywords);
        Assert.Equal(
            4,
            request.MinimumKeywordLength);
        Assert.Equal(
            ["policy"],
            request.ExcludedTerms);
    }

    [Fact]
    public void Insight_ExposesSummaryAndKeywords()
    {
        var keyword =
            new TextKeyword(
                "evidence",
                [0, 9]);

        var insight =
            new TextInsight(
                "Evidence summary.",
                [keyword]);

        Assert.Equal(
            "Evidence summary.",
            insight.Summary);
        Assert.Same(
            keyword,
            Assert.Single(insight.Keywords));
    }

    [Fact]
    public void Insight_RejectsBlankSummary()
    {
        Assert.Throws<ArgumentException>(
            () => new TextInsight(
                " ",
                Array.Empty<TextKeyword>()));
    }
}

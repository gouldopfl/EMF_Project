using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerDisplayNameResolverTests
{
    [Theory]
    [InlineData("claim.pdf")]
    [InlineData("therapy.csv")]
    [InlineData("article.html")]
    [InlineData("statement.docx")]
    public void IsReviewerFacingLabel_RejectsFileNames(string value)
    {
        Assert.False(
            VeteransReviewerDisplayNameResolver
                .IsReviewerFacingLabel(value));
    }

    [Theory]
    [InlineData("VA Blue Button Report")]
    [InlineData("SLEEP MED PAP CLINIC NOTE")]
    [InlineData("Medical / Scientific Literature")]
    public void IsReviewerFacingLabel_AcceptsHumanLabels(string value)
    {
        Assert.True(
            VeteransReviewerDisplayNameResolver
                .IsReviewerFacingLabel(value));
    }

    [Fact]
    public void Resolve_PrefersHumanTitleOverFileName()
    {
        var value =
            VeteransReviewerDisplayNameResolver.Resolve(
                "Evidence of Record",
                "Sleep Medicine Follow-Up",
                "source.pdf");

        Assert.Equal("Sleep Medicine Follow-Up", value);
    }

    [Fact]
    public void Resolve_FallsBackRatherThanExposeFileName()
    {
        var value =
            VeteransReviewerDisplayNameResolver.Resolve(
                "Medical Evidence",
                null,
                "source.pdf");

        Assert.Equal("Medical Evidence", value);
    }
}

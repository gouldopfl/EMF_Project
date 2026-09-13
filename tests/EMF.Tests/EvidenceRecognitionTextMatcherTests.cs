using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class EvidenceRecognitionTextMatcherTests
{
    [Theory]
    [InlineData("OSA documented.")]
    [InlineData("Diagnosis: (OSA).")]
    [InlineData("OSA/CPAP treatment reviewed.")]
    [InlineData("History includes osa, hypertension.")]
    public void ContainsTerm_MatchesBoundedAcronym(string text)
    {
        Assert.True(
            EvidenceRecognitionTextMatcher.ContainsTerm(
                text,
                "OSA"));
    }

    [Theory]
    [InlineData("Medication dosage adjusted.")]
    [InlineData("MIMOSA extract reviewed.")]
    [InlineData("OSA2 research label.")]
    public void ContainsTerm_RejectsAcronymInsideAlphanumericToken(
        string text)
    {
        Assert.False(
            EvidenceRecognitionTextMatcher.ContainsTerm(
                text,
                "OSA"));
    }

    [Fact]
    public void ContainsTerm_ContinuesPastEmbeddedOccurrence()
    {
        Assert.True(
            EvidenceRecognitionTextMatcher.ContainsTerm(
                "Dosage changed; OSA remains active.",
                "OSA"));
    }

    [Fact]
    public void ContainsTerm_MatchesPhraseAcrossPunctuationBoundaries()
    {
        Assert.True(
            EvidenceRecognitionTextMatcher.ContainsTerm(
                "Assessment: obstructive sleep apnea (OSA).",
                "obstructive sleep apnea"));
    }

    [Fact]
    public void ContainsTerm_RejectsPhraseInsideLargerWordSequence()
    {
        Assert.False(
            EvidenceRecognitionTextMatcher.ContainsTerm(
                "presleep apnea-like notation",
                "sleep apnea"));
    }

    [Fact]
    public void ContainsTerm_RejectsBlankTerm()
    {
        Assert.Throws<ArgumentException>(
            () => EvidenceRecognitionTextMatcher.ContainsTerm(
                "text",
                " "));
    }
}

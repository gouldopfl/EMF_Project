using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class EvidenceRecognitionTermTests
{
    [Fact]
    public void RecognitionTerm_PreservesRequirementAndSource()
    {
        var term = new EvidenceRecognitionTerm
        {
            Id =
                new EvidenceRecognitionTermId(
                    "recognition-001"),
            RequirementId =
                new RequirementId("requirement-001"),
            Term = "sleep study",
            TermType =
                EvidenceRecognitionTermTypes.Phrase,
            RecognitionRole =
                EvidenceRecognitionRoles.EvidenceType,
            AuthoritySource = "38 CFR"
        };

        Assert.Equal(
            "recognition-001",
            term.Id.Value);

        Assert.Equal(
            "requirement-001",
            term.RequirementId.Value);

        Assert.Equal("sleep study", term.Term);
        Assert.Equal("Phrase", term.TermType);
        Assert.Equal(
            "EvidenceType",
            term.RecognitionRole);
        Assert.Equal(
            "38 CFR",
            term.AuthoritySource);
    }

    [Fact]
    public void RecognitionTermId_RejectsEmptyValue()
    {
        Assert.Throws<ArgumentException>(
            () => new EvidenceRecognitionTermId(" "));
    }
}

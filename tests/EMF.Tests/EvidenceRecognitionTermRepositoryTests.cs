using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class EvidenceRecognitionTermRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsRecognitionTerm()
    {
        var repository =
            new InMemoryEvidenceRecognitionTermRepository();

        var term =
            CreateTerm(
                "term-001",
                "requirement-001");

        await repository.AddEvidenceRecognitionTermAsync(term);

        var stored =
            await repository.GetEvidenceRecognitionTermAsync(
                term.Id);

        Assert.NotNull(stored);
        Assert.Equal(term.Term, stored!.Term);
        Assert.Equal(
            term.RequirementId,
            stored.RequirementId);
        Assert.Equal(
            term.AuthoritySource,
            stored.AuthoritySource);
    }


    [Fact]
    public async Task Repository_GetsTermsByRequirementInIdOrder()
    {
        var repository =
            new InMemoryEvidenceRecognitionTermRepository();

        var requirement =
            new RequirementId(
                "requirement-001");

        await repository.AddEvidenceRecognitionTermAsync(
            CreateTerm(
                "term-002",
                requirement.Value));

        await repository.AddEvidenceRecognitionTermAsync(
            CreateTerm(
                "term-001",
                requirement.Value));

        await repository.AddEvidenceRecognitionTermAsync(
            CreateTerm(
                "term-003",
                "requirement-002"));

        var stored =
            await repository.GetEvidenceRecognitionTermsAsync(
                requirement);

        Assert.Equal(2, stored.Count);
        Assert.Equal(
            "term-001",
            stored[0].Id.Value);
        Assert.Equal(
            "term-002",
            stored[1].Id.Value);
    }


    private static EvidenceRecognitionTerm CreateTerm(
        string id,
        string requirementId)
    {
        return new EvidenceRecognitionTerm
        {
            Id =
                new EvidenceRecognitionTermId(id),
            RequirementId =
                new RequirementId(requirementId),
            Term = "sleep study",
            TermType =
                EvidenceRecognitionTermTypes.Phrase,
            RecognitionRole =
                EvidenceRecognitionRoles.EvidenceType,
            AuthoritySource = "38 CFR"
        };
    }
}

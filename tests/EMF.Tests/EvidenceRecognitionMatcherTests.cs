using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Services;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class EvidenceRecognitionMatcherTests
{
    [Fact]
    public async Task FindMatchesAsync_ReturnsRecognizedTerms()
    {
        var repository =
            new InMemoryEvidenceRecognitionTermRepository();

        var requirement =
            new RequirementId(
                "requirement-001");

        await repository.AddEvidenceRecognitionTermAsync(
            CreateTerm(
                "term-001",
                requirement,
                "chronic"));

        await repository.AddEvidenceRecognitionTermAsync(
            CreateTerm(
                "term-002",
                requirement,
                "bilateral"));

        await repository.AddEvidenceRecognitionTermAsync(
            CreateTerm(
                "term-003",
                requirement,
                "instability"));

        var matcher =
            new EvidenceRecognitionMatcher(
                repository);

        var result =
            await matcher.FindMatchesAsync(
                requirement,
                "Veteran has chronic bilateral ankle instability.");

        Assert.Equal(3, result.Count);

        Assert.Contains(
            result,
            match => match.Term == "chronic");

        Assert.Contains(
            result,
            match => match.Term == "bilateral");

        Assert.Contains(
            result,
            match => match.Term == "instability");
    }


    [Fact]
    public async Task FindMatchesAsync_IgnoresNonMatchingTerms()
    {
        var repository =
            new InMemoryEvidenceRecognitionTermRepository();

        var requirement =
            new RequirementId(
                "requirement-001");

        await repository.AddEvidenceRecognitionTermAsync(
            CreateTerm(
                "term-001",
                requirement,
                "chronic"));

        await repository.AddEvidenceRecognitionTermAsync(
            CreateTerm(
                "term-002",
                requirement,
                "migraine"));

        var matcher =
            new EvidenceRecognitionMatcher(
                repository);

        var result =
            await matcher.FindMatchesAsync(
                requirement,
                "Veteran has chronic ankle pain.");

        Assert.Single(result);

        Assert.Equal(
            "chronic",
            result[0].Term);

        Assert.Equal(
            EvidenceClassifications.MedicalEvidence,
            result[0].EvidenceClassification);
    }


    [Fact]
    public async Task FindMatchesAsync_RejectsTermForDifferentRequirement()
    {
        var requested =
            new RequirementId("requirement-001");

        var repository =
            new MismatchedRepository(
                CreateTerm(
                    "term-001",
                    new RequirementId("requirement-other"),
                    "chronic"));

        var matcher =
            new EvidenceRecognitionMatcher(repository);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => matcher.FindMatchesAsync(
                requested,
                "Veteran has chronic ankle pain."));
    }

    private sealed class MismatchedRepository :
        IEvidenceRecognitionTermRepository
    {
        private readonly EvidenceRecognitionTerm _term;

        public MismatchedRepository(EvidenceRecognitionTerm term) =>
            _term = term;

        public Task<IReadOnlyList<EvidenceRecognitionTerm>>
            GetEvidenceRecognitionTermsAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceRecognitionTerm>>(
                [_term]);

        public Task AddEvidenceRecognitionTermAsync(
            EvidenceRecognitionTerm term,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EvidenceRecognitionTerm?>
            GetEvidenceRecognitionTermAsync(
                EvidenceRecognitionTermId termId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static EvidenceRecognitionTerm CreateTerm(
        string id,
        RequirementId requirementId,
        string term)
    {
        return new EvidenceRecognitionTerm
        {
            Id =
                new EvidenceRecognitionTermId(id),

            RequirementId =
                requirementId,

            Term = term,

            TermType =
                EvidenceRecognitionTermTypes.Keyword,


            RecognitionRole =
                EvidenceRecognitionRoles.EvidenceType,

            EvidenceClassification =
                EvidenceClassifications.MedicalEvidence,

            AuthoritySource =
                "38 CFR 4.71a"
        };
    }
}

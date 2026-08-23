using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteEvidenceRecognitionTermRepositoryTests
{
    [Fact]
    public async Task Repository_RoundTripsRecognitionTerm()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceRecognitionTermRepository(path);

            await repository.InitializeAsync();

            var requirement =
                new RequirementId("requirement-001");

            await SeedRequirementAsync(
                path,
                requirement);

            var term =
                CreateTerm(
                    "term-001",
                    requirement.Value);

            await repository.AddEvidenceRecognitionTermAsync(term);

            var stored =
                await repository.GetEvidenceRecognitionTermAsync(
                    term.Id);

            Assert.NotNull(stored);
            Assert.Equal(term.Id, stored!.Id);
            Assert.Equal(term.RequirementId, stored.RequirementId);
            Assert.Equal(term.Term, stored.Term);
            Assert.Equal(term.TermType, stored.TermType);
            Assert.Equal(term.RecognitionRole, stored.RecognitionRole);
            Assert.Equal(
                term.EvidenceClassification,
                stored.EvidenceClassification);
            Assert.Equal(term.AuthoritySource, stored.AuthoritySource);
        }
        finally
        {
            File.Delete(path);
        }
    }


    [Fact]
    public async Task Repository_GetsTermsByRequirementInIdOrder()
    {
        var path = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteEvidenceRecognitionTermRepository(path);

            await repository.InitializeAsync();

            var requirement =
                new RequirementId("requirement-001");

            await SeedRequirementAsync(
                path,
                requirement);

            await repository.AddEvidenceRecognitionTermAsync(
                CreateTerm(
                    "term-002",
                    requirement.Value));

            await repository.AddEvidenceRecognitionTermAsync(
                CreateTerm(
                    "term-001",
                    requirement.Value));

            var stored =
                await repository.GetEvidenceRecognitionTermsAsync(
                    requirement);

            Assert.Equal(2, stored.Count);
            Assert.Equal("term-001", stored[0].Id.Value);
            Assert.Equal("term-002", stored[1].Id.Value);
        }
        finally
        {
            File.Delete(path);
        }
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
            EvidenceClassification =
                EvidenceClassifications.MedicalEvidence,
            AuthoritySource = "38 CFR"
        };
    }

    private static async Task SeedRequirementAsync(
        string path,
        RequirementId requirementId)
    {
        var regulatory = new SqliteRegulatoryRepository(path);

        var authority = new RegulatoryAuthority
        {
            Id = new RegulatoryAuthorityId("authority-recognition"),
            AuthorityType = "Regulation",
            Citation = "38 CFR",
            Title = "Veterans Affairs"
        };

        await regulatory.AddRegulatoryAuthorityAsync(authority);

        var provision = new RegulatoryProvision
        {
            Id = new RegulatoryProvisionId("provision-recognition"),
            RegulatoryAuthorityId = authority.Id,
            ProvisionType = RegulatoryProvisionTypes.Requirement,
            Citation = "38 CFR 3.303"
        };

        await regulatory.AddRegulatoryProvisionAsync(provision);

        await regulatory.AddRequirementAsync(
            new Requirement
            {
                Id = requirementId,
                RegulatoryProvisionId = provision.Id,
                Description = "Required element."
            });
    }

}

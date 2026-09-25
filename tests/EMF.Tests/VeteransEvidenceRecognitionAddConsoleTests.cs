using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class VeteransEvidenceRecognitionAddConsoleTests
{
    [Fact]
    public async Task RunAsync_AddsReviewedRecognitionTermIdempotently()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var regulatory = new SqliteRegulatoryRepository(databasePath);
            await regulatory.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-recognition-add"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };
            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-recognition-add"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };
            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirement = new Requirement
            {
                Id = new RequirementId("requirement-recognition-add"),
                RegulatoryProvisionId = provision.Id,
                Description = "Secondary aggravation requirement."
            };
            await regulatory.AddRequirementAsync(requirement);

            var args = new[]
            {
                "evidence", "recognition", "add", databasePath,
                requirement.Id.Value,
                "low back pain",
                EvidenceRecognitionTermTypes.Phrase,
                EvidenceRecognitionRoles.Aggravation,
                EvidenceClassifications.MedicalEvidence,
                "reviewed-lumbar-projection"
            };

            Assert.Equal(0, await VeteransConsoleCommand.RunAsync(args));
            Assert.Equal(0, await VeteransConsoleCommand.RunAsync(args));

            var recognition =
                new SqliteEvidenceRecognitionTermRepository(databasePath);
            await recognition.InitializeAsync();

            var terms =
                await recognition.GetEvidenceRecognitionTermsAsync(
                    requirement.Id);

            var term = Assert.Single(terms);
            Assert.Equal("low back pain", term.Term);
            Assert.Equal(EvidenceRecognitionTermTypes.Phrase, term.TermType);
            Assert.Equal(EvidenceRecognitionRoles.Aggravation, term.RecognitionRole);
            Assert.Equal(EvidenceClassifications.MedicalEvidence, term.EvidenceClassification);
            Assert.Equal("reviewed-lumbar-projection", term.AuthoritySource);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task RunAsync_RecognitionAddRejectsMissingDatabase()
    {
        var exitCode =
            await VeteransConsoleCommand.RunAsync(
                [
                    "evidence", "recognition", "add",
                    "/tmp/emf-missing-recognition-add.db",
                    "requirement-1", "fall", "Keyword",
                    "Aggravation", "MedicalEvidence", "reviewed"
                ]);

        Assert.Equal(2, exitCode);
    }
}

using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class VeteransClaimsSqliteIssueDecisionRegulatoryProvisionRepositoryTests
{
    [Fact]
    public async Task Association_RoundTrips()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            await using var connection =
                new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();

            await using var seed = connection.CreateCommand();
            seed.CommandText =
                """
                INSERT INTO VeteransClaims_Veterans (Id)
                VALUES ('v-idrp');

                INSERT INTO VeteransClaims_Claims (Id, VeteranId)
                VALUES ('claim-idrp', 'v-idrp');

                INSERT INTO VeteransClaims_ClaimIssues
                    (Id, ClaimId, ClaimIssueType)
                VALUES ('issue-idrp', 'claim-idrp', 'ServiceConnection');

                INSERT INTO VeteransClaims_VaDecisions (Id, DecisionDate)
                VALUES ('va-idrp', '2026-09-06');

                INSERT INTO VeteransClaims_IssueDecisions
                    (Id, VaDecisionId, ClaimIssueId, Outcome)
                VALUES (
                    'decision-idrp',
                    'va-idrp',
                    'issue-idrp',
                    'Granted'
                );

                INSERT INTO VeteransClaims_RegulatoryAuthorities
                    (Id, AuthorityType, Citation, Title)
                VALUES (
                    'authority-idrp',
                    'Regulation',
                    '38 CFR',
                    'Test Authority'
                );

                INSERT INTO VeteransClaims_RegulatoryProvisions
                    (Id, RegulatoryAuthorityId, ProvisionType, Citation)
                VALUES (
                    'provision-idrp',
                    'authority-idrp',
                    'Requirement',
                    '38 CFR 3.303'
                );
                """;
            await seed.ExecuteNonQueryAsync();

            var repository = new SqliteVaDecisionRepository(path);

            await repository.AddIssueDecisionRegulatoryProvisionAsync(
                new IssueDecisionRegulatoryProvision
                {
                    IssueDecisionId =
                        new IssueDecisionId("decision-idrp"),
                    RegulatoryProvisionId =
                        new RegulatoryProvisionId("provision-idrp")
                });

            var provisions =
                await repository.GetRegulatoryProvisionIdsAsync(
                    new IssueDecisionId("decision-idrp"));

            var decisions =
                await repository.GetIssueDecisionIdsAsync(
                    new RegulatoryProvisionId("provision-idrp"));

            Assert.Equal(
                "provision-idrp",
                Assert.Single(provisions).Value);
            Assert.Equal(
                "decision-idrp",
                Assert.Single(decisions).Value);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Association_RejectsMissingRecords()
    {
        var path = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();

            var repository = new SqliteVaDecisionRepository(path);

            await Assert.ThrowsAsync<InvalidOperationException>(
                () =>
                    repository
                        .AddIssueDecisionRegulatoryProvisionAsync(
                            new IssueDecisionRegulatoryProvision
                            {
                                IssueDecisionId =
                                    new IssueDecisionId("missing-decision"),
                                RegulatoryProvisionId =
                                    new RegulatoryProvisionId(
                                        "missing-provision")
                            }));
        }
        finally
        {
            File.Delete(path);
        }
    }
}

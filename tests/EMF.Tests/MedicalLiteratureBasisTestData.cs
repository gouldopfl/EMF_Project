using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

internal static class MedicalLiteratureBasisTestData
{
    public static async Task LinkRequirementAsync(
        string path, RequirementId requirementId,
        string basisId = "basis-literature-tests")
    {
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder { DataSource = path, ForeignKeys = true }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT OR IGNORE INTO VeteransClaims_Veterans (Id) VALUES ('veteran-literature-tests');
            INSERT OR IGNORE INTO VeteransClaims_Claims (Id, VeteranId)
                VALUES ('claim-literature-tests', 'veteran-literature-tests');
            INSERT OR IGNORE INTO VeteransClaims_ClaimIssues (Id, ClaimId, ClaimIssueType)
                VALUES ('issue-literature-tests', 'claim-literature-tests', 'ServiceConnection');
            INSERT OR IGNORE INTO VeteransClaims_ServiceConnectionTheories (Id, ClaimIssueId, TheoryType)
                VALUES ('theory-literature-tests', 'issue-literature-tests', 'Secondary');
            INSERT OR IGNORE INTO VeteransClaims_ServiceConnectionBases (Id, ClaimIssueId, ServiceConnectionTheoryId)
                VALUES ($basis, 'issue-literature-tests', 'theory-literature-tests');
            INSERT INTO VeteransClaims_BasisRequirements (ServiceConnectionBasisId, RequirementId)
                VALUES ($basis, $requirement);
            """;
        command.Parameters.AddWithValue("$basis", basisId);
        command.Parameters.AddWithValue("$requirement", requirementId.Value);
        await command.ExecuteNonQueryAsync();
    }
}

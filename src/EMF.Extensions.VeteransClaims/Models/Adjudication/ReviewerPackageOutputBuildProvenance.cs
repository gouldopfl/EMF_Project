using System.Security.Cryptography;
using System.Text;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public sealed record ReviewerPackageOutputBuildProvenance(
    string LinkId,
    string ProvenanceId,
    int Version,
    string BuildId,
    string SourceRevisionId,
    DateTimeOffset LinkedUtc)
{
    public static ReviewerPackageOutputBuildProvenance Create(
        string provenanceId,
        string buildId,
        string sourceRevisionId,
        DateTimeOffset linkedUtc)
    {
        var row = new ReviewerPackageOutputBuildProvenance(
            string.Empty,
            provenanceId,
            1,
            buildId,
            sourceRevisionId.ToLowerInvariant(),
            linkedUtc);

        row = row with { LinkId = ComputeIdentity(row) };
        row.ValidateIntegrity();
        return row;
    }

    public void ValidateIntegrity()
    {
        if (Version != 1 ||
            !IsUpperSha256(ProvenanceId) ||
            !BuildId.StartsWith("sha256:", StringComparison.Ordinal) ||
            !IsUpperSha256(BuildId["sha256:".Length..]) ||
            !IsRevision(SourceRevisionId) ||
            LinkedUtc == default ||
            !IsUpperSha256(LinkId) ||
            LinkId != ComputeIdentity(this))
            throw new InvalidDataException(
                "Reviewer output build provenance is invalid.");
    }

    public static string ComputeIdentity(
        ReviewerPackageOutputBuildProvenance value)
    {
        var text =
            $"reviewer-output-build-provenance-v1\n" +
            $"{value.ProvenanceId}\n{value.Version}\n" +
            $"{value.BuildId}\n{value.SourceRevisionId}\n";

        return Convert.ToHexString(
            SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    }

    private static bool IsUpperSha256(string value) =>
        value.Length == 64 &&
        value.All(c => c is >= '0' and <= '9' or >= 'A' and <= 'F');

    private static bool IsRevision(string value) =>
        value.Length is 40 or 64 &&
        value.All(c => c is >= '0' and <= '9' or >= 'a' and <= 'f');
}

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

/// <summary>Opaque, versioned renderer input contract owned by reviewer orchestration.</summary>
public sealed record ReviewerPackageSnapshot(
    EvidencePackageId PackageId, int Version, string Payload, string Sha256)
{
    public void ValidateIntegrity()
    {
        if (Version != 1 || string.IsNullOrWhiteSpace(PackageId.Value) ||
            string.IsNullOrWhiteSpace(Payload) || Sha256 != ComputeHash(Payload))
            throw new InvalidDataException("Reviewer snapshot version, identity, or integrity is invalid.");
    }

    // Validate the package binding again at the repository boundary. The remaining
    // V1 renderer fields are interpreted exclusively by reviewer orchestration.
    public void ValidateMembership(EvidencePackageDetails expected)
    {
        ValidateIntegrity();
        try
        {
            using var document = JsonDocument.Parse(Payload);
            var root = document.RootElement;
            var details = root.GetProperty("Details").GetProperty("PackageDetails");
            var package = details.GetProperty("Package");
            var p = expected.Package;
            if (root.GetProperty("Version").GetInt32() != Version ||
                package.GetProperty("Id").GetString() != PackageId.Value ||
                PackageId != p.Id || package.GetProperty("ClaimIssueId").GetString() != p.ClaimIssueId.Value ||
                package.GetProperty("Purpose").GetString() != p.Purpose ||
                package.GetProperty("ReviewerRole").GetString() != p.ReviewerRole ||
                package.GetProperty("ServiceConnectionBasisId").GetString() != p.ServiceConnectionBasisId?.Value)
                throw new InvalidDataException("Reviewer snapshot package binding is invalid.");
            var members = details.GetProperty("Artifacts").EnumerateArray().Select(x => (
                x.GetProperty("EvidencePackageId").GetString(), x.GetProperty("ArtifactId").GetString(),
                x.GetProperty("ContentRole").GetString(), x.GetProperty("ReviewerPageSelection").GetString()))
                .OrderBy(x => x.Item2, StringComparer.Ordinal);
            var wanted = expected.Artifacts.OrderBy(x => x.ArtifactId.Value, StringComparer.Ordinal)
                .Select(x => ((string?)x.EvidencePackageId.Value, (string?)x.ArtifactId.Value, (string?)x.ContentRole, x.ReviewerPageSelection));
            if (!members.SequenceEqual(wanted))
                throw new InvalidDataException("Reviewer snapshot member binding is invalid.");
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            throw new InvalidDataException("Reviewer snapshot package binding is incomplete.", ex);
        }
    }

    public static string ComputeHash(string payload) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(payload)));
}

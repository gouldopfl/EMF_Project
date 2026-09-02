using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.ConsoleApplication;

internal static class VeteransEvidencePackageFormatter
{
    public static IReadOnlyList<string> Format(
        EvidencePackageDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var lines = new List<string>
        {
            $"Package: {details.Package.Id.Value}",
            $"Claim Issue: {details.Package.ClaimIssueId.Value}",
            $"Purpose: {details.Package.Purpose}",
            $"Reviewer Role: {details.Package.ReviewerRole}",
            $"Artifacts: {details.Artifacts.Count}"
        };

        lines.AddRange(
            details.Artifacts.Select(
                artifact =>
                    $"- {artifact.ContentRole}: " +
                    $"{artifact.ArtifactId.Value}"));

        return lines;
    }
}

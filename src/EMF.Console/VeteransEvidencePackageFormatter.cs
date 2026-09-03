using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;

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

    public static IReadOnlyList<string> Format(
        VeteransReviewerPackageDetails details)
    {
        ArgumentNullException.ThrowIfNull(details);

        var lines =
            new List<string>(
                Format(details.PackageDetails));

        if (details.Artifacts.Count == 0)
            return lines;

        lines.Add(string.Empty);
        lines.Add("Artifact Details:");

        foreach (var artifact in details.Artifacts)
        {
            lines.Add(
                $"- {artifact.Id.Value}: " +
                $"{artifact.Name} [{artifact.ArtifactType}]");
        }

        foreach (var content in details.ArtifactContents)
        {
            lines.Add(string.Empty);
            lines.Add("Artifact Content:");
            lines.Add(content.Text);
        }

        return lines;
    }
}

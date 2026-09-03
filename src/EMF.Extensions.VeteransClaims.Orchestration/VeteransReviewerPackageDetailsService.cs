using System.Text.Json;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageDetailsService
{
    private readonly IEvidencePackageService _packages;
    private readonly IEvidenceRepository _evidence;
    private readonly IArtifactTextExtractor? _textExtractor;

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(evidence);

        _packages = packages;
        _evidence = evidence;
    }

    public VeteransReviewerPackageDetailsService(
        IEvidencePackageService packages,
        IEvidenceRepository evidence,
        IArtifactTextExtractor textExtractor)
    {
        ArgumentNullException.ThrowIfNull(packages);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(textExtractor);

        _packages = packages;
        _evidence = evidence;
        _textExtractor = textExtractor;
    }

    public async Task<VeteransReviewerPackageDetails?> GetAsync(
        EvidencePackageId packageId,
        CancellationToken cancellationToken = default)
    {
        var details =
            await _packages.GetAsync(
                packageId,
                cancellationToken);

        if (details is null)
            return null;

        var artifacts = new List<Artifact>();
        var artifactContents =
            new List<VeteransReviewerArtifactContent>();

        foreach (var packageArtifact in details.Artifacts)
        {
            var artifact =
                await _evidence.GetArtifactAsync(
                    packageArtifact.ArtifactId,
                    cancellationToken);

            if (artifact is null)
                continue;

            artifacts.Add(artifact);

            var text =
                GetTextSummary(artifact);

            if (string.IsNullOrWhiteSpace(text) &&
                _textExtractor is not null)
            {
                text =
                    await _textExtractor.ExtractTextAsync(
                        artifact.Id,
                        cancellationToken);
            }

            if (string.IsNullOrWhiteSpace(text))
                continue;

            artifactContents.Add(
                new VeteransReviewerArtifactContent
                {
                    Artifact = artifact,
                    Text = text
                });
        }

        return new VeteransReviewerPackageDetails
        {
            PackageDetails = details,
            Artifacts = artifacts,
            ArtifactContents = artifactContents
        };
    }

    private static string? GetTextSummary(
        Artifact artifact)
    {
        if (!string.Equals(
                artifact.ArtifactType,
                "text-summary",
                StringComparison.Ordinal))
        {
            return null;
        }

        if (!artifact.Metadata.TryGetValue(
                "summary",
                out var value))
        {
            return null;
        }

        return value switch
        {
            string text => text,
            JsonElement json when
                json.ValueKind == JsonValueKind.String =>
                json.GetString(),
            _ => null
        };
    }
}

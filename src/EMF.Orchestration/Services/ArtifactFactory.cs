using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class ArtifactFactory : IArtifactFactory
{
    public ArtifactCreationResult Create(
        DiscoveredItem item,
        ArtifactId artifactId,
ContentFingerprint? fingerprint)
    {
        ArgumentNullException.ThrowIfNull(item);

        var metadata = new Dictionary<string, object>(
            item.Metadata)
        {
            [ArtifactMetadataKeys.SourcePath] = item.SourcePath,
            [ArtifactMetadataKeys.SourceType] = item.SourceType
        };

        if (item.SizeBytes is not null)
        {
            metadata[ArtifactMetadataKeys.SizeBytes] = item.SizeBytes.Value;
        }

        if (item.ModifiedUtc is not null)
        {
            metadata[ArtifactMetadataKeys.ModifiedUtc] = item.ModifiedUtc.Value;
        }

        if (!metadata.ContainsKey(
                ArtifactMetadataKeys.FileExtension))
        {
            var extension =
                Path.GetExtension(item.Name);

            if (!string.IsNullOrWhiteSpace(extension))
            {
                metadata[
                    ArtifactMetadataKeys.FileExtension] =
                    extension;
            }
        }

        var artifact = new Artifact
        {
            Id = artifactId,
            Name = item.Name,
            ArtifactType = item.SourceType,
Fingerprint = fingerprint,
            CreatedUtc = item.CreatedUtc ?? DateTimeOffset.UtcNow,
            Metadata = metadata
        };

        var provenance = new Provenance
        {
            ArtifactId = artifactId,
            Source = item.SourcePath,
            RecordedBy = "EMF.Discovery",
            Properties = new Dictionary<string, object>
            {
                [ArtifactMetadataKeys.SourceType] = item.SourceType
            }
        };

        return new ArtifactCreationResult
        {
            Artifact = artifact,
            Provenance = provenance
        };
    }
}

using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class ArtifactFactory : IArtifactFactory
{
    public ArtifactCreationResult Create(
        DiscoveredItem item,
        ArtifactId artifactId)
    {
        ArgumentNullException.ThrowIfNull(item);

        var metadata = new Dictionary<string, object>(
            item.Metadata)
        {
            ["sourcePath"] = item.SourcePath,
            ["sourceType"] = item.SourceType
        };

        if (item.SizeBytes is not null)
        {
            metadata["sizeBytes"] = item.SizeBytes.Value;
        }

        if (item.ModifiedUtc is not null)
        {
            metadata["modifiedUtc"] = item.ModifiedUtc.Value;
        }

        var artifact = new Artifact
        {
            Id = artifactId,
            Name = item.Name,
            ArtifactType = item.SourceType,
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
                ["sourceType"] = item.SourceType
            }
        };

        return new ArtifactCreationResult
        {
            Artifact = artifact,
            Provenance = provenance
        };
    }
}

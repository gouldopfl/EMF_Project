using System.Security.Cryptography;
using System.Text;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;

namespace EMF.Orchestration.Services;

public sealed class TextSummaryEvidenceArtifactFactory
{
    public Artifact Create(
        string summary,
        string name,
        DateTimeOffset createdUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(summary);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (createdUtc == default)
            throw new ArgumentOutOfRangeException(
                nameof(createdUtc));

        var hash =
            Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(summary)))
                .ToLowerInvariant();

        return new Artifact
        {
            Id = new ArtifactId($"sha256:{hash}"),
            Name = name,
            ArtifactType = "text-summary",
            Fingerprint = new ContentFingerprint
            {
                Algorithm = "SHA-256",
                Value = hash
            },
            CreatedUtc = createdUtc,
            Metadata =
                new Dictionary<string, object>
                {
                    ["summary"] = summary
                }
        };
    }
}

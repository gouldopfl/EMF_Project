using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Intelligence.Capabilities;

namespace EMF.Laboratory;

public sealed class TextInsightEvidenceArtifactFactory
{
    public Artifact Create(
        TextInsight insight,
        string name,
        DateTimeOffset createdUtc)
    {
        ArgumentNullException.ThrowIfNull(insight);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        if (createdUtc == default)
            throw new ArgumentOutOfRangeException(
                nameof(createdUtc));

        var keywords =
            insight.Keywords
                .Select(keyword =>
                    new Dictionary<string, object>
                    {
                        ["term"] = keyword.Term,
                        ["offsets"] =
                            keyword.Offsets.ToArray()
                    })
                .ToArray();

        var metadata =
            new Dictionary<string, object>
            {
                ["summary"] = insight.Summary,
                ["keywords"] = keywords
            };

        var payload =
            JsonSerializer.Serialize(metadata);

        var hash =
            Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(payload)))
                .ToLowerInvariant();

        return new Artifact
        {
            Id = new ArtifactId($"sha256:{hash}"),
            Name = name,
            ArtifactType = "text-insight",
            Fingerprint = new ContentFingerprint
            {
                Algorithm = "SHA-256",
                Value = hash
            },
            CreatedUtc = createdUtc,
            Metadata = metadata
        };
    }
}

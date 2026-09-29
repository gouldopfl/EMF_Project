using System.Text.Json;
using EMF.Common;

namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

/// <summary>
/// Immutable archive payload at the VeteransClaims persistence boundary.
/// Full manifest integrity remains owned by the shared build-identity validator;
/// persistence does not depend on Common's runtime or assembly-capture types.
/// </summary>
public sealed class ReviewerBuildManifestDocument
{
    private ReviewerBuildManifestDocument(
        string json,
        string buildId,
        string sourceRevisionId)
    {
        Json = json;
        BuildId = buildId;
        SourceRevisionId = sourceRevisionId;
    }

    public string Json { get; }
    public string BuildId { get; }
    public string SourceRevisionId { get; }

    public static ReviewerBuildManifestDocument Parse(string json)
    {
        var manifest = ReadValidatedManifest(json);
        return new ReviewerBuildManifestDocument(
            json,
            manifest.BuildId,
            manifest.SourceRevisionId);
    }

    public void ValidateIntegrity()
    {
        var manifest = ReadValidatedManifest(Json);
        if (!string.Equals(BuildId, manifest.BuildId, StringComparison.Ordinal) ||
            !string.Equals(
                SourceRevisionId,
                manifest.SourceRevisionId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Reviewer build manifest document does not match its identity.");
        }
    }

    private static EmfBuildManifest ReadValidatedManifest(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidDataException("Reviewer build manifest is invalid.");

        EmfBuildManifest? manifest;
        try
        {
            manifest = JsonSerializer.Deserialize<EmfBuildManifest>(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Reviewer build manifest is invalid.", ex);
        }

        // Validate structure before the shared validator sorts the artifacts.
        if (manifest is null || manifest.Artifacts is null ||
            manifest.Artifacts.Any(artifact => artifact is null))
        {
            throw new InvalidDataException(
                "Reviewer build manifest structure is invalid.");
        }

        EmfBuildManifestIdentity.Validate(manifest);
        return manifest;
    }
}

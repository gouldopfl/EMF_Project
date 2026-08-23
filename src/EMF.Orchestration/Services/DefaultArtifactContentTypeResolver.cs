using EMF.Core.Contracts;
using EMF.Core.Models;

namespace EMF.Orchestration.Services;

public sealed class DefaultArtifactContentTypeResolver :
    IArtifactContentTypeResolver
{
    public string? ResolveContentType(
        Artifact artifact)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        if (!artifact.Metadata.TryGetValue(
                ArtifactMetadataKeys.FileExtension,
                out var value))
        {
            return null;
        }

        var extension =
            value as string;

        return extension?.ToLowerInvariant() switch
        {
            ".txt" => "text/plain",
            ".pdf" => "application/pdf",
            ".docx" =>
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".xlsx" =>
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".db" => "application/x-sqlite3",
            ".sqlite" => "application/x-sqlite3",
            ".sqlite3" => "application/x-sqlite3",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".eml" => "message/rfc822",
            ".msg" => "application/vnd.ms-outlook",
            ".xml" => "application/xml",
            _ => null
        };
    }
}

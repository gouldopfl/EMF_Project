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
            ".rtf" => "application/rtf",
            ".odt" => "application/vnd.oasis.opendocument.text",
            ".ods" => "application/vnd.oasis.opendocument.spreadsheet",
            ".odp" => "application/vnd.oasis.opendocument.presentation",
            ".pdf" => "application/pdf",
            ".pptx" =>
                "application/vnd.openxmlformats-officedocument.presentationml.presentation",
            ".ppt" =>
                "application/vnd.ms-powerpoint",
            ".docx" =>
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            ".doc" =>
                "application/msword",
            ".xlsx" =>
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            ".xls" =>
                "application/vnd.ms-excel",
            ".db" => "application/x-sqlite3",
            ".sqlite" => "application/x-sqlite3",
            ".sqlite3" => "application/x-sqlite3",
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".eml" => "message/rfc822",
            ".msg" => "application/vnd.ms-outlook",
            ".xml" => "application/xml",
            ".html" => "text/html",
            ".htm" => "text/html",
            ".jpg" => "image/jpeg",
            ".jpeg" => "image/jpeg",
            ".png" => "image/png",
            ".tif" => "image/tiff",
            ".tiff" => "image/tiff",
            _ => null
        };
    }
}

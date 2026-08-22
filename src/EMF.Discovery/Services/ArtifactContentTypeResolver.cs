namespace EMF.Discovery.Services;

public sealed class ArtifactContentTypeResolver
{
    public string? Resolve(string extension)
    {
        return extension.ToLowerInvariant() switch
        {
            ".json" => "application/json",
            ".csv" => "text/csv",
            ".tsv" => "text/csv",
            ".xml" => "application/xml",
            ".html" => "text/html",
            ".htm" => "text/html",
            ".txt" => "text/plain",
            ".eml" => "message/rfc822",
            _ => null
        };
    }
}

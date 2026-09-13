namespace EMF.Extensions.VeteransClaims.Orchestration;

public static class VeteransReviewerDisplayNameResolver
{
    private static readonly HashSet<string> FileExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".csv",
            ".doc",
            ".docx",
            ".eml",
            ".htm",
            ".html",
            ".jpeg",
            ".jpg",
            ".json",
            ".msg",
            ".pdf",
            ".png",
            ".rtf",
            ".tif",
            ".tiff",
            ".txt",
            ".xml",
            ".yaml",
            ".yml",
            ".zip"
        };

    public static string Resolve(
        string fallback,
        params string?[] candidates)
    {
        if (string.IsNullOrWhiteSpace(fallback))
            throw new ArgumentException(
                "Reviewer display-name fallback is required.",
                nameof(fallback));

        foreach (var candidate in candidates)
        {
            if (IsReviewerFacingLabel(candidate))
                return candidate!.Trim();
        }

        return fallback;
    }

    public static bool IsReviewerFacingLabel(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var candidate = value.Trim();

        if (candidate.Contains("://", StringComparison.Ordinal) ||
            candidate.Contains('\\') ||
            Path.IsPathRooted(candidate))
        {
            return false;
        }

        var extension = Path.GetExtension(candidate);

        return string.IsNullOrWhiteSpace(extension) ||
               !FileExtensions.Contains(extension);
    }
}

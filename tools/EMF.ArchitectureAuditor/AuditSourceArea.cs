namespace EMF.ArchitectureAuditor;

public enum AuditSourceArea
{
    Production,
    Tests,
    Tools,
    Other
}

public static class AuditSourceAreaClassifier
{
    public static AuditSourceArea Classify(string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var path = relativePath.Replace('\\', '/');

        if (path.StartsWith("src/", StringComparison.Ordinal))
            return AuditSourceArea.Production;

        if (path.StartsWith("tests/", StringComparison.Ordinal))
            return AuditSourceArea.Tests;

        if (path.StartsWith("tools/", StringComparison.Ordinal))
            return AuditSourceArea.Tools;

        return AuditSourceArea.Other;
    }
}

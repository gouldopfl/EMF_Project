using System.Xml;
using System.Xml.Linq;

namespace EMF.ArchitectureAuditor;

public sealed class ConsoleCompositionDependencyRule :
    IRepositoryAuditRule
{
    private static readonly HashSet<string> Allowed =
    [
        "EMF.Common",
        "EMF.Inventory",
        "EMF.Discovery",
        "EMF.Orchestration",
        "EMF.Integrity",
        "EMF.Persistence",
        "EMF.Security.Azure",
        "EMF.Security",
        "EMF.Intelligence",
        "EMF.Intelligence.Development",
        "EMF.Security.Persistence.Sqlite",
        "EMF.Intelligence.AzureOpenAI",
        "EMF.Extensions.VeteransClaims",
        "EMF.Extensions.VeteransClaims.Persistence.Sqlite",
        "EMF.Extensions.VeteransClaims.Orchestration"
    ];

    public string Id => "EMF-ARCH-006";

    public string Version => "1";

    public string Category => "Architecture";

    public IEnumerable<AuditFinding> Analyze(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        const string relativePath =
            "src/EMF.Console/EMF.Console.csproj";

        var fullPath =
            Path.Combine(
                Path.GetFullPath(repositoryRoot),
                relativePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Console composition project not found.",
                fullPath);
        }

        var document =
            XDocument.Load(
                fullPath,
                LoadOptions.SetLineInfo);

        foreach (var reference in
                 document.Descendants()
                     .Where(
                         element =>
                             element.Name.LocalName ==
                             "ProjectReference"))
        {
            cancellationToken.ThrowIfCancellationRequested();

            var include =
                reference.Attribute("Include")?.Value;

            var dependency =
                include is null
                    ? "<missing Include>"
                    : Path.GetFileNameWithoutExtension(
                        include.Replace('\\', '/'));

            if (Allowed.Contains(dependency))
                continue;

            var lineInfo = (IXmlLineInfo)reference;

            yield return new AuditFinding(
                Id,
                Version,
                Category,
                AuditSeverity.High,
                AuditConfidence.High,
                AuditAnalysisMode.Syntax,
                AuditSourceArea.Production,
                relativePath,
                lineInfo.HasLineInfo()
                    ? lineInfo.LineNumber
                    : 1,
                lineInfo.HasLineInfo()
                    ? lineInfo.LinePosition
                    : 1,
                $"EMF.Console references unapproved project " +
                $"'{dependency}'.",
                "Remove the dependency or explicitly revise the " +
                "Console composition boundary.");
        }
    }
}

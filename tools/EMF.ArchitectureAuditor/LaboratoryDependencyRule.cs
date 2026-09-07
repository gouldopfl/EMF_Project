using System.Xml;
using System.Xml.Linq;

namespace EMF.ArchitectureAuditor;

public sealed class LaboratoryDependencyRule :
    IRepositoryAuditRule
{
    private const string ProjectName = "EMF.Laboratory";
    private const string ProjectPath =
        "src/EMF.Laboratory/EMF.Laboratory.csproj";

    private static readonly HashSet<string> Allowed =
        new(StringComparer.Ordinal)
        {
            "EMF.Core",
            "EMF.Intelligence",
            "EMF.Intelligence.Development",
            "EMF.Orchestration",
            "EMF.Persistence",
            "EMF.Security",
            "EMF.Security.Persistence.Sqlite"
        };

    public string Id => "EMF-ARCH-003";

    public string Version => "1";

    public string Category => "Architecture";

    public IEnumerable<AuditFinding> Analyze(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);
        cancellationToken.ThrowIfCancellationRequested();

        var fullPath =
            Path.Combine(
                Path.GetFullPath(repositoryRoot),
                ProjectPath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "EMF.Laboratory project file was not found.",
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
                ProjectPath,
                lineInfo.HasLineInfo()
                    ? lineInfo.LineNumber
                    : 1,
                lineInfo.HasLineInfo()
                    ? lineInfo.LinePosition
                    : 1,
                $"{ProjectName} references unapproved project " +
                $"'{dependency}'.",
                "Remove the dependency or explicitly revise the " +
                "Laboratory integration boundary.");
        }
    }
}

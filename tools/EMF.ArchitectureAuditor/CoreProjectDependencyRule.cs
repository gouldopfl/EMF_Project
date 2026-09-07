using System.Xml;
using System.Xml.Linq;

namespace EMF.ArchitectureAuditor;

public sealed class CoreProjectDependencyRule : IRepositoryAuditRule
{
    private const string ProjectPath =
        "src/EMF.Core/EMF.Core.csproj";

    public string Id => "EMF-ARCH-001";

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
            throw new FileNotFoundException(
                "EMF.Core project file was not found.",
                fullPath);

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
                reference.Attribute("Include")?.Value ??
                "<missing Include>";

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
                lineInfo.HasLineInfo() ? lineInfo.LineNumber : 1,
                lineInfo.HasLineInfo() ? lineInfo.LinePosition : 1,
                $"EMF.Core references project '{include}'.",
                "Remove the project reference. EMF.Core must remain " +
                "independent of all other EMF projects.");
        }
    }
}

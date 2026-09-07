using System.Xml;
using System.Xml.Linq;

namespace EMF.ArchitectureAuditor;

public sealed class ProviderAdapterDependencyRule :
    IRepositoryAuditRule
{
    private static readonly IReadOnlyDictionary<
        string,
        HashSet<string>> Allowed =
        new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal)
        {
            ["EMF.Security.Azure"] =
                ["EMF.Security"],

            ["EMF.Security.Persistence.Sqlite"] =
                ["EMF.Security"],

            ["EMF.Intelligence.AzureOpenAI"] =
                ["EMF.Intelligence", "EMF.Security"],

            ["EMF.Intelligence.Development"] =
                ["EMF.Intelligence"],

            ["EMF.Intelligence.Persistence.Sqlite"] =
                ["EMF.Intelligence"]
        };

    public string Id => "EMF-ARCH-004";

    public string Version => "1";

    public string Category => "Architecture";

    public IEnumerable<AuditFinding> Analyze(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        foreach (var (project, allowed) in Allowed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var relativePath =
                $"src/{project}/{project}.csproj";

            var fullPath =
                Path.Combine(
                    Path.GetFullPath(repositoryRoot),
                    relativePath);

            if (!File.Exists(fullPath))
            {
                throw new FileNotFoundException(
                    $"Adapter project not found: {project}",
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

                if (allowed.Contains(dependency))
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
                    $"{project} references unapproved project " +
                    $"'{dependency}'.",
                    "Remove the dependency or explicitly revise the " +
                    "provider/adapter architecture boundary.");
            }
        }
    }
}

using System.Xml;
using System.Xml.Linq;

namespace EMF.ArchitectureAuditor;

public sealed class VeteransClaimsDependencyRule :
    IRepositoryAuditRule
{
    private static readonly IReadOnlyDictionary<
        string,
        HashSet<string>> Allowed =
        new Dictionary<string, HashSet<string>>(
            StringComparer.Ordinal)
        {
            ["EMF.Extensions.VeteransClaims"] =
                ["EMF.Common", "EMF.Core"],

            ["EMF.Extensions.VeteransClaims.Orchestration"] =
            [
                "EMF.Common",
                "EMF.Core",
                "EMF.Extensions.VeteransClaims",
                "EMF.Intelligence",
                "EMF.Orchestration",
                "EMF.Security"
            ],

            ["EMF.Extensions.VeteransClaims.Persistence"] =
            [
                "EMF.Extensions.VeteransClaims",
                "EMF.Extensions.VeteransClaims.Persistence.Sqlite"
            ],

            ["EMF.Extensions.VeteransClaims.Persistence.Sqlite"] =
                ["EMF.Extensions.VeteransClaims"]
        };

    public string Id => "EMF-ARCH-005";

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
                    $"VeteransClaims project not found: {project}",
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
                    "VeteransClaims extension architecture boundary.");
            }
        }
    }
}

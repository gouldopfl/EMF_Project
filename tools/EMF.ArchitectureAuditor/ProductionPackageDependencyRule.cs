using System.Xml;
using System.Xml.Linq;

namespace EMF.ArchitectureAuditor;

public sealed class ProductionPackageDependencyRule : IRepositoryAuditRule
{
    private static readonly HashSet<string> SqliteProjects =
    [
        "EMF.Inventory",
        "EMF.Persistence",
        "EMF.Security.Persistence.Sqlite",
        "EMF.Intelligence.Persistence.Sqlite",
        "EMF.Extensions.VeteransClaims.Persistence.Sqlite"
    ];

    private static readonly HashSet<string> OrchestrationPackages =
    [
        "DocumentFormat.OpenXml",
        "ExcelDataReader",
        "GHSoftware.WordDocTextExtractor",
        "MetadataExtractor",
        "MimeKit",
        "MsgReader",
        "OfficeIMO.Reader.OpenDocument",
        "OfficeIMO.Reader.PowerPoint",
        "OfficeIMO.Rtf",
        "OpenCvSharp4",
        "PdfPig",
        "PDFtoImage",
        "Sdcb.PaddleOCR",
        "Sdcb.PaddleOCR.Models.Local",
        "Sdcb.PaddleOCR.Models.LocalV5",
        "OpenCvSharp4.official.runtime.linux-x64.slim",
        "Sdcb.PaddleInference.runtime.linux-x64.mkl",
        "OpenCvSharp4.runtime.win",
        "Sdcb.PaddleInference.runtime.win64.mkl",
        "OpenCvSharp4.runtime.osx.x64",
        "Sdcb.PaddleInference.runtime.osx-x64",
        "OpenCvSharp4.runtime.osx.arm64",
        "Sdcb.PaddleInference.runtime.osx-arm64"
    ];

    public string Id => "EMF-ARCH-007";
    public string Version => "2";
    public string Category => "Architecture";

    public IEnumerable<AuditFinding> Analyze(
        string repositoryRoot,
        CancellationToken cancellationToken = default)
    {
        var src = Path.Combine(Path.GetFullPath(repositoryRoot), "src");
        var projects = Directory.EnumerateFiles(
                src, "*.csproj", SearchOption.AllDirectories)
            .Take(257)
            .ToArray();

        if (projects.Length > 256)
            throw new InvalidOperationException(
                "Production project count exceeds auditor limit.");

        foreach (var path in projects.OrderBy(p => p, StringComparer.Ordinal))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (new FileInfo(path).Length > 1024 * 1024)
                throw new InvalidOperationException(
                    $"Project file exceeds auditor limit: {path}");

            var project = Path.GetFileNameWithoutExtension(path);
            var relative = Path.GetRelativePath(repositoryRoot, path);
            var document = XDocument.Load(path, LoadOptions.SetLineInfo);

            foreach (var reference in document.Descendants()
                .Where(e => e.Name.LocalName == "PackageReference"))
            {
                var package =
                    reference.Attribute("Include")?.Value ??
                    "<missing Include>";

                if (IsAllowed(project, package))
                    continue;

                var line = (IXmlLineInfo)reference;

                yield return new AuditFinding(
                    Id, Version, Category,
                    AuditSeverity.High,
                    AuditConfidence.High,
                    AuditAnalysisMode.Syntax,
                    AuditSourceArea.Production,
                    relative,
                    line.HasLineInfo() ? line.LineNumber : 1,
                    line.HasLineInfo() ? line.LinePosition : 1,
                    $"{project} references unapproved package '{package}'.",
                    "Remove the package or explicitly revise the " +
                    "production package dependency boundary.");
            }
        }
    }

    private static bool IsAllowed(string project, string package) =>
        package switch
        {
            "Microsoft.Data.Sqlite" =>
                SqliteProjects.Contains(project),

            "Azure.AI.OpenAI" =>
                project == "EMF.Intelligence.AzureOpenAI",

            "Azure.Identity" =>
                project is "EMF.Intelligence.AzureOpenAI"
                    or "EMF.Security.Azure",

            "Azure.Monitor.Ingestion"
                or "Azure.Security.KeyVault.Keys" =>
                project == "EMF.Security.Azure",

            "DocumentFormat.OpenXml" =>
                project is "EMF.Orchestration"
                    or "EMF.Extensions.VeteransClaims.Orchestration",

            _ =>
                project == "EMF.Orchestration" &&
                OrchestrationPackages.Contains(package)
        };
}

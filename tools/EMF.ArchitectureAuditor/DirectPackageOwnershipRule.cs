using System.Xml.Linq;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EMF.ArchitectureAuditor;

public sealed class DirectPackageOwnershipRule : IAuditRule
{
    private static readonly (string Prefix, string Package)[] Ownership =
    [
        ("Microsoft.Data.Sqlite", "Microsoft.Data.Sqlite"),
        ("Azure.AI.OpenAI", "Azure.AI.OpenAI"),
        ("Azure.Identity", "Azure.Identity"),
        ("Azure.Core", "Azure.Core"),
        ("Azure.Monitor.Ingestion", "Azure.Monitor.Ingestion"),
        ("Azure.Security.KeyVault.Keys", "Azure.Security.KeyVault.Keys"),
        ("DocumentFormat.OpenXml", "DocumentFormat.OpenXml"),
        ("ExcelDataReader", "ExcelDataReader"),
        ("GHSoftware.WordDocTextExtractor", "GHSoftware.WordDocTextExtractor"),
        ("MetadataExtractor", "MetadataExtractor"),
        ("MimeKit", "MimeKit"),
        ("MsgReader", "MsgReader"),
        ("OfficeIMO.Reader.OpenDocument", "OfficeIMO.Reader.OpenDocument"),
        ("OfficeIMO.Reader.PowerPoint", "OfficeIMO.Reader.PowerPoint"),
        ("OfficeIMO.Rtf", "OfficeIMO.Rtf"),
        ("OpenCvSharp", "OpenCvSharp4"),
        ("PDFtoImage", "PDFtoImage"),
        ("Sdcb.PaddleInference", "Sdcb.PaddleInference"),
        ("Sdcb.PaddleOCR.Models.LocalV5", "Sdcb.PaddleOCR.Models.LocalV5"),
        ("Sdcb.PaddleOCR.Models.Local", "Sdcb.PaddleOCR.Models.Local"),
        ("Sdcb.PaddleOCR", "Sdcb.PaddleOCR"),
        ("UglyToad.PdfPig", "PdfPig")
    ];

    private readonly IReadOnlyDictionary<string, HashSet<string>>
        _packagesByProject;

    public DirectPackageOwnershipRule(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        _packagesByProject = LoadPackages(repositoryRoot);
    }

    public string Id => "EMF-ARCH-008";
    public string Version => "1";
    public string Category => "Architecture";

    public IEnumerable<AuditFinding> Analyze(
        ParsedSourceFile source,
        CancellationToken cancellationToken = default)
    {
        if (AuditSourceAreaClassifier.Classify(
                source.Source.RelativePath) != AuditSourceArea.Production)
            yield break;

        var project = GetProject(source.Source.RelativePath);

        if (project is null ||
            !_packagesByProject.TryGetValue(project, out var packages))
            yield break;

        var root = source.SyntaxTree.GetRoot(cancellationToken);

        foreach (var directive in root.DescendantNodes()
                     .OfType<UsingDirectiveSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var namespaceName = directive.Name?.ToString();

            if (namespaceName is null)
                continue;

            var required = Ownership
                .FirstOrDefault(x =>
                    namespaceName.Equals(
                        x.Prefix,
                        StringComparison.Ordinal) ||
                    namespaceName.StartsWith(
                        x.Prefix + ".",
                        StringComparison.Ordinal))
                .Package;

            if (required is null || packages.Contains(required))
                continue;

            var position = directive.GetLocation()
                .GetLineSpan().StartLinePosition;

            yield return new AuditFinding(
                Id,
                Version,
                Category,
                AuditSeverity.High,
                AuditConfidence.High,
                AuditAnalysisMode.Syntax,
                AuditSourceArea.Production,
                source.Source.RelativePath,
                position.Line + 1,
                position.Character + 1,
                $"{project} uses namespace '{namespaceName}' " +
                $"without direct package ownership of '{required}'.",
                "Declare the package directly or remove the " +
                "implementation dependency.");
        }
    }

    private static IReadOnlyDictionary<string, HashSet<string>>
        LoadPackages(string repositoryRoot)
    {
        var result =
            new Dictionary<string, HashSet<string>>(
                StringComparer.Ordinal);

        var src = Path.Combine(
            Path.GetFullPath(repositoryRoot),
            "src");

        foreach (var path in Directory.EnumerateFiles(
                     src, "*.csproj", SearchOption.AllDirectories))
        {
            var project = Path.GetFileNameWithoutExtension(path);
            var document = XDocument.Load(path);

            result[project] = document.Descendants()
                .Where(x => x.Name.LocalName == "PackageReference")
                .Select(x => x.Attribute("Include")?.Value)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(x => x!)
                .ToHashSet(StringComparer.Ordinal);
        }

        return result;
    }

    private static string? GetProject(string relativePath)
    {
        var parts = relativePath
            .Replace('\\', '/')
            .Split('/', StringSplitOptions.RemoveEmptyEntries);

        return parts.Length >= 3 && parts[0] == "src"
            ? parts[1]
            : null;
    }
}

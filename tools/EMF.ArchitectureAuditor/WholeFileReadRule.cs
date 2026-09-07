using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EMF.ArchitectureAuditor;

public sealed class WholeFileReadRule : IAuditRule
{
    private static readonly HashSet<string> WholeFileMethods =
        new(StringComparer.Ordinal)
        {
            "ReadAllBytes",
            "ReadAllLines",
            "ReadAllText"
        };

    public string Id => "EMF-RESOURCE-001";

    public string Version => "1";

    public string Category => "ResourceSafety";

    public IEnumerable<AuditFinding> Analyze(
        ParsedSourceFile source,
        CancellationToken cancellationToken = default)
    {
        var sourceArea =
            AuditSourceAreaClassifier.Classify(
                source.Source.RelativePath);

        if (sourceArea is not AuditSourceArea.Production and
            not AuditSourceArea.Tools)
        {
            yield break;
        }

        var root =
            source.SyntaxTree.GetRoot(cancellationToken);

        foreach (var invocation in
                 root.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (invocation.Expression is not
                MemberAccessExpressionSyntax member)
            {
                continue;
            }

            var target = member.Expression.ToString();
            var method = member.Name.Identifier.ValueText;

            if (!WholeFileMethods.Contains(method) ||
                !IsSystemIoFile(target))
            {
                continue;
            }

            var span =
                member.Name
                    .GetLocation()
                    .GetLineSpan()
                    .StartLinePosition;

            yield return new AuditFinding(
                Id,
                Version,
                Category,
                AuditSeverity.Low,
                AuditConfidence.High,
                AuditAnalysisMode.Syntax,
                sourceArea,
                source.Source.RelativePath,
                span.Line + 1,
                span.Character + 1,
                $"Whole-file materialization call File.{method} detected.",
                "Verify an explicit input-size boundary exists before " +
                "materializing the complete file.");
        }
    }

    private static bool IsSystemIoFile(string target) =>
        target is "File" or
            "System.IO.File" or
            "global::System.IO.File";
}

using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace EMF.ArchitectureAuditor;

public sealed class InputMaterializationRule : IAuditRule
{
    public string Id => "EMF-RESOURCE-002";

    public string Version => "2";

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

        var root = source.SyntaxTree.GetRoot(cancellationToken);

        foreach (var invocation in root.DescendantNodes()
                     .OfType<InvocationExpressionSyntax>())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (invocation.Expression is not
                    MemberAccessExpressionSyntax member ||
                member.Name.Identifier.ValueText != "ToArray" ||
                member.Expression is not IdentifierNameSyntax receiver)
            {
                continue;
            }

            var name = receiver.Identifier.ValueText;

            var method = invocation.Ancestors()
                .OfType<MethodDeclarationSyntax>()
                .FirstOrDefault();

            if (method is null ||
                !IsByteInputParameter(method, name) ||
                HasPriorRejectingLengthGuard(
                    method,
                    invocation,
                    name))
            {
                continue;
            }

            var position = member.Name.GetLocation()
                .GetLineSpan()
                .StartLinePosition;

            yield return new AuditFinding(
                Id,
                Version,
                Category,
                AuditSeverity.Medium,
                AuditConfidence.Medium,
                AuditAnalysisMode.Syntax,
                sourceArea,
                source.Source.RelativePath,
                position.Line + 1,
                position.Character + 1,
                $"Byte-like input '{name}' is materialized with " +
                "ToArray() without a visible prior size-rejection guard.",
                "Add or verify an explicit maximum input-size boundary " +
                "before materializing the complete input.");
        }
    }

    private static bool IsByteInputParameter(
        MethodDeclarationSyntax method,
        string name)
    {
        var parameter = method.ParameterList.Parameters
            .FirstOrDefault(
                p => p.Identifier.ValueText == name);

        var type = parameter?.Type?
            .ToString()
            .Replace(" ", "");

        if (type is null)
            return false;

        return type == "byte[]" ||
               type.EndsWith(
                   "ReadOnlyMemory<byte>",
                   StringComparison.Ordinal) ||
               type.EndsWith(
                   "Memory<byte>",
                   StringComparison.Ordinal) ||
               type.EndsWith(
                   "ReadOnlySpan<byte>",
                   StringComparison.Ordinal) ||
               type.EndsWith(
                   "Span<byte>",
                   StringComparison.Ordinal);
    }

    private static bool HasPriorRejectingLengthGuard(
        MethodDeclarationSyntax method,
        InvocationExpressionSyntax invocation,
        string name)
    {
        return method.DescendantNodes()
            .OfType<IfStatementSyntax>()
            .Where(x => x.SpanStart < invocation.SpanStart)
            .Any(
                statement =>
                    ContainsRejectingLengthComparison(
                        statement.Condition,
                        name) &&
                    statement.Statement
                        .DescendantNodesAndSelf()
                        .OfType<ThrowStatementSyntax>()
                        .Any());
    }

    private static bool ContainsRejectingLengthComparison(
        ExpressionSyntax condition,
        string name)
    {
        return condition.DescendantNodesAndSelf()
            .OfType<BinaryExpressionSyntax>()
            .Where(
                binary =>
                    binary.RawKind ==
                        (int)SyntaxKind.GreaterThanExpression ||
                    binary.RawKind ==
                        (int)SyntaxKind.GreaterThanOrEqualExpression)
            .Any(
                binary =>
                    binary.DescendantNodesAndSelf()
                        .OfType<MemberAccessExpressionSyntax>()
                        .Any(
                            member =>
                                member.Expression is
                                    IdentifierNameSyntax identifier &&
                                identifier.Identifier.ValueText == name &&
                                member.Name.Identifier.ValueText ==
                                    "Length"));
    }
}

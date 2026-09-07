using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace EMF.ArchitectureAuditor;

public sealed class SourceParser
{
    private static readonly CSharpParseOptions ParseOptions =
        new(LanguageVersion.CSharp14);

    private readonly long _maxFileBytes;

    public SourceParser(
        long maxFileBytes = SourceInventory.DefaultMaxFileBytes)
    {
        if (maxFileBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));

        _maxFileBytes = maxFileBytes;
    }

    public ParsedSourceFile Parse(
        SourceFile source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        cancellationToken.ThrowIfCancellationRequested();

        using var stream = new FileStream(
            source.FullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 64 * 1024,
            FileOptions.SequentialScan);

        if (stream.Length > _maxFileBytes)
        {
            throw new InvalidDataException(
                $"Source file exceeds the maximum allowed size: " +
                $"{source.FullPath}");
        }

        var text = SourceText.From(
            stream,
            encoding: null,
            checksumAlgorithm: SourceHashAlgorithm.Sha256);

        cancellationToken.ThrowIfCancellationRequested();

        var tree = CSharpSyntaxTree.ParseText(
            text,
            ParseOptions,
            path: source.FullPath,
            cancellationToken: cancellationToken);

        var diagnostics = tree
            .GetDiagnostics(cancellationToken)
            .Where(x => x.Severity == DiagnosticSeverity.Error)
            .ToArray();

        return new ParsedSourceFile(
            source,
            tree,
            diagnostics);
    }
}

public sealed record ParsedSourceFile(
    SourceFile Source,
    SyntaxTree SyntaxTree,
    IReadOnlyList<Diagnostic> ParseErrors);

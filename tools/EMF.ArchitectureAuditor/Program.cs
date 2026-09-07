using EMF.ArchitectureAuditor;

const int MaxReportedParseErrors = 50;
const int MaxReportedFindings = 100;

var repositoryRoot = Path.GetFullPath(
    args.Length > 0 ? args[0] : Directory.GetCurrentDirectory());

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancellation.Cancel();
};

try
{
    var inventory = new SourceInventory();
    var parser = new SourceParser();

    IAuditRule[] rules =
    [
        new WholeFileReadRule(),
        new InputMaterializationRule()
    ];

    IRepositoryAuditRule[] repositoryRules =
    [
        new CoreProjectDependencyRule(),
        new FoundationalDependencyRule()
    ];

    Console.WriteLine("===== EMF ARCHITECTURE AUDITOR =====");
    Console.WriteLine($"Repository: {repositoryRoot}");
    Console.WriteLine("Mode: Direct C# source analysis");
    Console.WriteLine($"Rules: {rules.Length}");
    Console.WriteLine();

    var sources = inventory.Discover(repositoryRoot);
    var findings = new List<AuditFinding>();

    long totalBytes = 0;
    var parseErrors = 0;
    var reportedParseErrors = 0;

    foreach (var source in sources)
    {
        cancellation.Token.ThrowIfCancellationRequested();

        totalBytes = checked(totalBytes + source.SizeBytes);

        var parsed = parser.Parse(
            source,
            cancellation.Token);

        foreach (var diagnostic in parsed.ParseErrors)
        {
            parseErrors++;

            if (reportedParseErrors >= MaxReportedParseErrors)
                continue;

            var location = diagnostic.Location.GetLineSpan();

            Console.WriteLine(
                $"PARSE ERROR: {source.RelativePath}:" +
                $"{location.StartLinePosition.Line + 1}:" +
                $"{location.StartLinePosition.Character + 1} " +
                $"{diagnostic.Id} {diagnostic.GetMessage()}");

            reportedParseErrors++;
        }

        if (parsed.ParseErrors.Count != 0)
            continue;

        foreach (var rule in rules)
        {
            findings.AddRange(
                rule.Analyze(
                    parsed,
                    cancellation.Token));
        }
    }

    var repositoryResults =
        new List<(IRepositoryAuditRule Rule, AuditFinding[] Findings)>();

    foreach (var rule in repositoryRules)
    {
        cancellation.Token.ThrowIfCancellationRequested();

        var ruleFindings =
            rule.Analyze(
                    repositoryRoot,
                    cancellation.Token)
                .ToArray();

        repositoryResults.Add((rule, ruleFindings));
        findings.AddRange(ruleFindings);
    }

    var ordered = findings
        .OrderByDescending(x => x.Severity)
        .ThenByDescending(x => x.Confidence)
        .ThenBy(x => x.SourceArea)
        .ThenBy(x => x.RuleId, StringComparer.Ordinal)
        .ThenBy(x => x.RelativePath, StringComparer.Ordinal)
        .ThenBy(x => x.Line)
        .ThenBy(x => x.Column)
        .ToArray();

    Console.WriteLine();
    Console.WriteLine("===== SOURCE INVENTORY =====");
    Console.WriteLine($"C# files: {sources.Count}");
    Console.WriteLine($"Source bytes: {totalBytes:N0}");
    Console.WriteLine($"Parse errors: {parseErrors}");

    Console.WriteLine();
    Console.WriteLine("===== REPOSITORY ASSURANCE =====");

    foreach (var result in repositoryResults)
    {
        var status =
            result.Findings.Length == 0
                ? "PASS"
                : $"FINDINGS: {result.Findings.Length}";

        Console.WriteLine(
            $"{result.Rule.Id} [{result.Rule.Category}] {status}");
    }

    Console.WriteLine();
    Console.WriteLine("===== FINDINGS =====");

    foreach (var finding in ordered.Take(MaxReportedFindings))
    {
        Console.WriteLine(
            $"{finding.RuleId} " +
            $"[{finding.Severity}/{finding.Confidence}/" +
            $"{finding.SourceArea}] " +
            $"{finding.RelativePath}:{finding.Line}:{finding.Column}");

        Console.WriteLine($"  {finding.Message}");
        Console.WriteLine($"  Review: {finding.Recommendation}");
    }

    if (ordered.Length > MaxReportedFindings)
    {
        Console.WriteLine(
            $"Additional findings suppressed: " +
            $"{ordered.Length - MaxReportedFindings}");
    }

    Console.WriteLine();
    Console.WriteLine("===== AUDIT SUMMARY =====");
    Console.WriteLine($"Rules executed: {rules.Length + repositoryRules.Length}");
    Console.WriteLine($"Findings: {ordered.Length}");
    Console.WriteLine("===== AUDIT COMPLETE =====");

    return parseErrors == 0 ? 0 : 2;
}
catch (OperationCanceledException)
{
    Console.Error.WriteLine("Audit cancelled.");
    return 130;
}
catch (Exception ex)
{
    Console.Error.WriteLine(
        $"Audit failed: {ex.GetType().Name}: {ex.Message}");

    return 1;
}

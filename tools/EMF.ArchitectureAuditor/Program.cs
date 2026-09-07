using EMF.ArchitectureAuditor;

const int MaxReportedParseErrors = 50;

var repositoryRoot =
    Path.GetFullPath(
        args.Length > 0
            ? args[0]
            : Directory.GetCurrentDirectory());

using var cancellation = new CancellationTokenSource();

Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    cancellation.Cancel();
};

try
{
    var inventory = new SourceInventory();
    var parser = new SourceParser();

    Console.WriteLine("===== EMF ARCHITECTURE AUDITOR =====");
    Console.WriteLine($"Repository: {repositoryRoot}");
    Console.WriteLine("Mode: Direct C# source analysis");
    Console.WriteLine();

    var sources = inventory.Discover(repositoryRoot);

    long totalBytes = 0;
    var parseErrorCount = 0;
    var reportedErrors = 0;

    foreach (var source in sources)
    {
        cancellation.Token.ThrowIfCancellationRequested();

        totalBytes = checked(totalBytes + source.SizeBytes);

        var parsed =
            parser.Parse(
                source,
                cancellation.Token);

        foreach (var diagnostic in parsed.ParseErrors)
        {
            parseErrorCount++;

            if (reportedErrors >= MaxReportedParseErrors)
                continue;

            var line = diagnostic.Location.GetLineSpan();

            Console.WriteLine(
                $"PARSE ERROR: {source.RelativePath}:" +
                $"{line.StartLinePosition.Line + 1}:" +
                $"{line.StartLinePosition.Character + 1} " +
                $"{diagnostic.Id} {diagnostic.GetMessage()}");

            reportedErrors++;
        }
    }

    Console.WriteLine();
    Console.WriteLine("===== SOURCE INVENTORY =====");
    Console.WriteLine($"C# files: {sources.Count}");
    Console.WriteLine($"Source bytes: {totalBytes:N0}");
    Console.WriteLine($"Parse errors: {parseErrorCount}");

    if (parseErrorCount > reportedErrors)
    {
        Console.WriteLine(
            $"Additional parse errors suppressed: " +
            $"{parseErrorCount - reportedErrors}");
    }

    Console.WriteLine();
    Console.WriteLine("===== AUDIT COMPLETE =====");

    return parseErrorCount == 0 ? 0 : 2;
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

using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;

namespace EMF.ConsoleApplication;

internal static class TextInsightConsoleOutputWriter
{
    public static void Write(
        IntelligenceAgentResult<TextInsight> result,
        ArtifactId sourceArtifactId,
        string auditDatabasePath,
        Artifact? evidenceArtifact = null,
        string? evidenceDatabasePath = null)
    {
        global::System.Console.WriteLine(
            "======================================");
        global::System.Console.WriteLine(
            " EMF Text Intelligence");
        global::System.Console.WriteLine(
            "======================================");
        global::System.Console.WriteLine();

        global::System.Console.WriteLine("Summary");
        global::System.Console.WriteLine("-------");
        global::System.Console.WriteLine(
            ConsoleTextSanitizer.Sanitize(
                result.Output!.Summary));
        global::System.Console.WriteLine();

        global::System.Console.WriteLine("Keywords");
        global::System.Console.WriteLine("--------");

        foreach (var keyword in result.Output.Keywords)
        {
            global::System.Console.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    $"- {keyword.Term} " +
                    $"({keyword.Occurrences})"));
        }

        if (result.Warnings.Count > 0)
        {
            global::System.Console.WriteLine();
            global::System.Console.WriteLine("Warnings");
            global::System.Console.WriteLine("--------");

            foreach (var warning in result.Warnings)
                global::System.Console.WriteLine(
                    ConsoleTextSanitizer.Sanitize(
                        $"- {warning}"));
        }

        global::System.Console.WriteLine();
        global::System.Console.WriteLine(
            ConsoleTextSanitizer.Sanitize(
                $"Correlation : {result.CorrelationId.Value}"));

        global::System.Console.WriteLine(
            ConsoleTextSanitizer.Sanitize(
                $"Artifact    : {sourceArtifactId.Value}"));

        global::System.Console.WriteLine(
            ConsoleTextSanitizer.Sanitize(
                $"Audit DB    : {auditDatabasePath}"));

        if (evidenceArtifact is not null)
        {
            global::System.Console.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    $"Evidence    : {evidenceArtifact.Id.Value}"));

            global::System.Console.WriteLine(
                ConsoleTextSanitizer.Sanitize(
                    $"Evidence DB : {evidenceDatabasePath}"));
        }
    }
}

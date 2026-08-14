using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;

namespace EMF.ConsoleApplication;

internal static class TextInsightConsoleOutputWriter
{
    public static void Write(
        IntelligenceAgentResult<TextInsight> result,
        ArtifactId sourceArtifactId,
        string auditDatabasePath)
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
            result.Output!.Summary);
        global::System.Console.WriteLine();

        global::System.Console.WriteLine("Keywords");
        global::System.Console.WriteLine("--------");

        foreach (var keyword in result.Output.Keywords)
        {
            global::System.Console.WriteLine(
                $"- {keyword.Term} " +
                $"({keyword.Occurrences})");
        }

        if (result.Warnings.Count > 0)
        {
            global::System.Console.WriteLine();
            global::System.Console.WriteLine("Warnings");
            global::System.Console.WriteLine("--------");

            foreach (var warning in result.Warnings)
                global::System.Console.WriteLine($"- {warning}");
        }

        global::System.Console.WriteLine();
        global::System.Console.WriteLine(
            $"Correlation : {result.CorrelationId.Value}");
        global::System.Console.WriteLine(
            $"Artifact    : {sourceArtifactId.Value}");
        global::System.Console.WriteLine(
            $"Audit DB    : {auditDatabasePath}");
    }
}

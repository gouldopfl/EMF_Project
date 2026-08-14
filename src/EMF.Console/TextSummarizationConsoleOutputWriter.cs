using EMF.Intelligence.Models;

namespace EMF.ConsoleApplication;

internal static class
    TextSummarizationConsoleOutputWriter
{
    public static void Write(
        IntelligenceCapabilityResult<string> result,
        string auditDatabasePath)
    {
        global::System.Console.WriteLine(
            "======================================");
        global::System.Console.WriteLine(
            " EMF Text Summarization");
        global::System.Console.WriteLine(
            "======================================");
        global::System.Console.WriteLine();
        global::System.Console.WriteLine("Summary");
        global::System.Console.WriteLine("-------");
        global::System.Console.WriteLine(result.Output);
        global::System.Console.WriteLine();
        global::System.Console.WriteLine(
            $"Provider    : {result.Metadata.ProviderId.Value}");
        global::System.Console.WriteLine(
            $"Engine      : {result.Metadata.EngineName}");
        global::System.Console.WriteLine(
            $"Correlation : {result.Metadata.CorrelationId.Value}");
        global::System.Console.WriteLine(
            $"Audit DB    : {auditDatabasePath}");
    }
}

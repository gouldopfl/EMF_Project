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
        global::System.Console.WriteLine(
            ConsoleTextSanitizer.Sanitize(
                result.Output));
        global::System.Console.WriteLine();
        global::System.Console.WriteLine(
            ConsoleTextSanitizer.Sanitize(
                $"Provider    : {result.Metadata.ProviderId.Value}"));

        global::System.Console.WriteLine(
            ConsoleTextSanitizer.Sanitize(
                $"Engine      : {result.Metadata.EngineName}"));

        global::System.Console.WriteLine(
            ConsoleTextSanitizer.Sanitize(
                $"Correlation : {result.Metadata.CorrelationId.Value}"));

        global::System.Console.WriteLine(
            ConsoleTextSanitizer.Sanitize(
                $"Audit DB    : {auditDatabasePath}"));
    }
}

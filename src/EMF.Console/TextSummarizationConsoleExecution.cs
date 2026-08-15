using System.Security.Cryptography;
using System.Text;
using EMF.Core.Models.Identities;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.ConsoleApplication;

internal static class
    TextSummarizationConsoleExecution
{
    public static async Task<int> ExecuteAsync(
        string text)
    {
        var runtime =
            await TextSummarizationConsoleRuntimeFactory
                .CreateAsync();

        var artifactId =
            new ArtifactId(
                "sha256:" +
                Convert.ToHexString(
                        SHA256.HashData(
                            Encoding.UTF8.GetBytes(text)))
                    .ToLowerInvariant());

        var context =
            new IntelligenceExecutionContext(
                runtime.SubjectId,
                new IntelligenceCorrelationId(
                    $"console-{Guid.NewGuid():N}"),
                runtime.ClassificationId,
                [artifactId]);

        var result =
            await runtime
                .TextSummarizationCapabilityExecutor
                .ExecuteAsync(
                    IntelligenceCapabilityIds
                        .TextSummarization,
                    new TextSummarizationRequest(
                        text,
                        1_000),
                    context);

        if (!result.Success ||
            result.Output is null)
        {
            global::System.Console.Error.WriteLine(
                result.Message ??
                "Text summarization failed.");
            return 1;
        }

        TextSummarizationConsoleOutputWriter.Write(
            result,
            runtime.AuditDatabasePath);

        return 0;
    }
}

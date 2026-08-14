using EMF.Intelligence.AzureOpenAI.Composition;
using EMF.Security.Models.Identities;

namespace EMF.ConsoleApplication;

internal sealed class
    TextSummarizationConsoleRuntime
{
    public required
        AzureOpenAITextIntelligenceComposition
        Composition { get; init; }

    public required string SubjectId { get; init; }

    public required ProtectionClassificationId
        ClassificationId { get; init; }

    public required string AuditDatabasePath
    { get; init; }
}

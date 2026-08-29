using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Security.Models.Identities;

namespace EMF.ConsoleApplication;

internal sealed class
    TextSummarizationConsoleRuntime
{
    public required
        IIntelligenceCapabilityExecutor<
            TextSummarizationRequest,
            string>
        TextSummarizationCapabilityExecutor
    { get; init; }

    public required
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string>
        TextStructuredExtractionCapabilityExecutor
    { get; init; }

    public required string SubjectId { get; init; }

    public required ProtectionClassificationId
        ClassificationId { get; init; }

    public required string AuditDatabasePath
    { get; init; }
}

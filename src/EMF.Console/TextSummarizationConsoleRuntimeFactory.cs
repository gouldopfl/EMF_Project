using EMF.Intelligence.AzureOpenAI.Composition;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Security.Persistence.Sqlite.Auditing;

namespace EMF.ConsoleApplication;

internal static class
    TextSummarizationConsoleRuntimeFactory
{
    public static async Task<
        TextSummarizationConsoleRuntime>
        CreateAsync()
    {
        var options =
            AzureOpenAIConsoleOptionsFactory.Create();

        var subjectId =
            Environment.GetEnvironmentVariable(
                "EMF_SUBJECT_ID") ??
            "console-steward";

        var classification =
            new ProtectionClassificationId(
                Environment.GetEnvironmentVariable(
                    "EMF_PROTECTION_CLASSIFICATION") ??
                "confidential");

        var auditPath =
            Environment.GetEnvironmentVariable(
                "EMF_SECURITY_AUDIT_DATABASE") ??
            Path.Combine(
                AppContext.BaseDirectory,
                "emf-security-audit.db");

        var auditSink =
            new SqliteSecurityAuditSink(auditPath);

        await auditSink.InitializeAsync();

        var policy =
            ConsoleAuthorizationPolicyFactory.Create(
                subjectId,
                SecurityPermissions.ArtifactIntelligenceUse);

        var composition =
            new AzureOpenAITextIntelligenceComposition(
                options,
                policy,
                auditSink,
                [classification]);

        return new TextSummarizationConsoleRuntime
        {
            TextSummarizationCapabilityExecutor =
                new AzureOpenAITextSummarizationExecutorAdapter(
                    composition
                        .TextSummarizationCapabilityExecutor),
            TextStructuredExtractionCapabilityExecutor =
                composition
                    .TextStructuredExtractionCapabilityExecutor,

            SubjectId = subjectId,
            ClassificationId = classification,
            AuditDatabasePath = auditPath
        };
    }
}

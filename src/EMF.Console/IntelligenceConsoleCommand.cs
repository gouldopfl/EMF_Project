using System.Security.Cryptography;
using System.Text;
using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Development.Composition;
using EMF.Intelligence.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Security.Persistence.Sqlite.Auditing;

namespace EMF.ConsoleApplication;

public static class IntelligenceConsoleCommand
{
    public static async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length != 2 ||
            args[0] != "analyze")
        {
            global::System.Console.WriteLine(
                "Usage: emf intelligence analyze <text-file>");
            return 2;
        }

        var sourcePath = Path.GetFullPath(args[1]);

        if (!File.Exists(sourcePath))
        {
            global::System.Console.Error.WriteLine(
                $"Text file not found: {sourcePath}");
            return 2;
        }

        var text =
            await File.ReadAllTextAsync(sourcePath);

        if (string.IsNullOrWhiteSpace(text))
        {
            global::System.Console.Error.WriteLine(
                "The text file is empty.");
            return 2;
        }

        var subjectId =
            Environment.GetEnvironmentVariable(
                "EMF_SUBJECT_ID") ??
            "console-steward";

        var classificationId =
            new ProtectionClassificationId(
                Environment.GetEnvironmentVariable(
                    "EMF_PROTECTION_CLASSIFICATION") ??
                "confidential");

        var auditDatabasePath =
            Environment.GetEnvironmentVariable(
                "EMF_SECURITY_AUDIT_DATABASE") ??
            Path.Combine(
                AppContext.BaseDirectory,
                "emf-security-audit.db");

        var auditSink =
            new SqliteSecurityAuditSink(
                auditDatabasePath);

        await auditSink.InitializeAsync();

        var policy =
            new AuthorizationPolicy(
                new InMemoryAuthorizationContextProvider(
                    [
                        new AuthorizationContext
                        {
                            SubjectId = subjectId,
                            RoleIds = Array.Empty<RoleId>(),
                            PermissionIds =
                            [
                                SecurityPermissions
                                    .ArtifactIntelligenceUse
                            ]
                        }
                    ]));

        var composition =
            new DevelopmentTextIntelligenceComposition(
                policy,
                auditSink,
                [classificationId]);

        var contentHash =
            Convert.ToHexString(
                    SHA256.HashData(
                        Encoding.UTF8.GetBytes(text)))
                .ToLowerInvariant();

        var sourceArtifactId =
            new ArtifactId(
                $"sha256:{contentHash}");

        var result =
            await new TextInsightExecutionService(
                    composition
                        .LongTextInsightAgentExecutor)
                .RunAsync(
                    text,
                    subjectId,
                    new IntelligenceCorrelationId(
                        $"console-{Guid.NewGuid():N}"),
                    classificationId,
                    [sourceArtifactId]);

        if (!result.Success ||
            result.Output is null)
        {
            global::System.Console.Error.WriteLine(
                result.Message ??
                "Text intelligence failed.");
            return 1;
        }

        TextInsightConsoleOutputWriter.Write(
            result,
            sourceArtifactId,
            auditDatabasePath);

        return 0;
    }
}

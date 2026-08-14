using System.Security.Cryptography;
using System.Text;
using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Development.Composition;
using EMF.Intelligence.Models.Identities;
using EMF.Laboratory;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Security.Persistence.Sqlite.Auditing;

if (args.Length != 1 ||
    args[0] is "--help" or "-h")
{
    Console.WriteLine(
        "Usage: dotnet run --project " +
        "src/EMF.Laboratory -- <text-file>");

    return;
}

var sourcePath =
    Path.GetFullPath(args[0]);

if (!File.Exists(sourcePath))
{
    throw new FileNotFoundException(
        "The source text file was not found.",
        sourcePath);
}

var text =
    await File.ReadAllTextAsync(sourcePath);

ArgumentException.ThrowIfNullOrWhiteSpace(text);

var subjectId =
    Environment.GetEnvironmentVariable(
        "EMF_LAB_SUBJECT_ID") ??
    "laboratory-steward";

var classificationId =
    new ProtectionClassificationId(
        Environment.GetEnvironmentVariable(
            "EMF_LAB_CLASSIFICATION") ??
        "confidential");

var auditDatabasePath =
    Environment.GetEnvironmentVariable(
        "EMF_LAB_AUDIT_DATABASE") ??
    Path.Combine(
        AppContext.BaseDirectory,
        "emf-laboratory-audit.db");

var auditSink =
    new SqliteSecurityAuditSink(
        auditDatabasePath);

await auditSink.InitializeAsync();

var authorizationPolicy =
    new AuthorizationPolicy(
        new InMemoryAuthorizationContextProvider(
            [
                new AuthorizationContext
                {
                    SubjectId = subjectId,
                    RoleIds =
                        Array.Empty<RoleId>(),
                    PermissionIds =
                    [
                        SecurityPermissions
                            .ArtifactIntelligenceUse
                    ]
                }
            ]));

var composition =
    new DevelopmentTextIntelligenceComposition(
        authorizationPolicy,
        auditSink,
        [classificationId]);

var contentHash =
    Convert.ToHexString(
            SHA256.HashData(
                Encoding.UTF8.GetBytes(text)))
        .ToLowerInvariant();

var artifactId =
    new ArtifactId(
        $"sha256:{contentHash}");

var correlationId =
    new IntelligenceCorrelationId(
        $"laboratory-{Guid.NewGuid():N}");

var runner =
    new TextInsightLaboratoryRunner(
        composition.LongTextInsightAgentExecutor);

var result =
    await runner.RunAsync(
        text,
        subjectId,
        correlationId,
        classificationId,
        [artifactId]);

Console.WriteLine(
    "======================================");
Console.WriteLine(
    " EMF Local Text Intelligence");
Console.WriteLine(
    "======================================");
Console.WriteLine();

if (!result.Success ||
    result.Output is null)
{
    Console.Error.WriteLine(
        result.Message ??
        "Text insight generation failed.");

    Environment.ExitCode = 1;
    return;
}

Console.WriteLine("Summary");
Console.WriteLine("-------");
Console.WriteLine(result.Output.Summary);
Console.WriteLine();

Console.WriteLine("Keywords");
Console.WriteLine("--------");

if (result.Output.Keywords.Count == 0)
{
    Console.WriteLine("(none)");
}
else
{
    foreach (var keyword in
        result.Output.Keywords)
    {
        Console.WriteLine(
            $"- {keyword.Term} " +
            $"({keyword.Occurrences})");
    }
}

if (result.Warnings.Count > 0)
{
    Console.WriteLine();
    Console.WriteLine("Warnings");
    Console.WriteLine("--------");

    foreach (var warning in result.Warnings)
    {
        Console.WriteLine($"- {warning}");
    }
}

Console.WriteLine();
Console.WriteLine(
    $"Correlation : {correlationId.Value}");
Console.WriteLine(
    $"Artifact    : {artifactId.Value}");
Console.WriteLine(
    $"Audit DB    : {auditDatabasePath}");

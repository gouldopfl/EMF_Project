using System.Security.Cryptography;
using System.Text;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Development.Composition;
using EMF.Intelligence.Models.Identities;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Security.Persistence.Sqlite.Auditing;

var promoteToEvidence =
    args.Length == 2 &&
    args[0] == "--promote";

if ((args.Length != 1 && !promoteToEvidence) ||
    args.Any(argument =>
        argument is "--help" or "-h"))
{
    Console.WriteLine(
        "Usage: dotnet run --project " +
        "src/EMF.Laboratory -- " +
        "[--promote] <text-file>");

    return;
}

var sourcePath =
    Path.GetFullPath(args[^1]);

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
    new TextInsightExecutionService(
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

Artifact? evidenceArtifact = null;
string? evidenceDatabasePath = null;

if (promoteToEvidence)
{
    var reviewedBy =
        Environment.GetEnvironmentVariable(
            "EMF_LAB_REVIEWED_BY");

    if (string.IsNullOrWhiteSpace(reviewedBy))
        reviewedBy = null;

    if (result.RequiresReview && reviewedBy is null)
    {
        Console.Error.WriteLine(
            "Evidence promotion requires review. " +
            "Set EMF_LAB_REVIEWED_BY to the reviewer identity.");
        Environment.ExitCode = 1;
        return;
    }

    evidenceDatabasePath =
        Environment.GetEnvironmentVariable(
            "EMF_LAB_EVIDENCE_DATABASE") ??
        Path.Combine(
            AppContext.BaseDirectory,
            "emf-laboratory-evidence.db");

    var evidenceRepository =
        new SqliteEvidenceRepository(
            evidenceDatabasePath);

    await evidenceRepository.InitializeAsync();

    var promotedUtc = DateTimeOffset.UtcNow;

    var sourceArtifact =
        new Artifact
        {
            Id = artifactId,
            Name = Path.GetFileName(sourcePath),
            ArtifactType = "text",
            Fingerprint = new ContentFingerprint
            {
                Algorithm = "SHA-256",
                Value = contentHash
            },
            CreatedUtc = promotedUtc,
            Metadata =
                new Dictionary<string, object>
                {
                    ["sourcePath"] = sourcePath
                }
        };

    await evidenceRepository
        .AddArtifactWithProvenanceAsync(
            sourceArtifact,
            new Provenance
            {
                ArtifactId = artifactId,
                Source = sourcePath,
                RecordedUtc = promotedUtc,
                RecordedBy = subjectId
            });

    evidenceArtifact =
        new TextInsightEvidenceArtifactFactory()
            .Create(
                result.Output,
                $"{Path.GetFileName(sourcePath)} insight",
                promotedUtc);

    await new IntelligenceEvidencePromotionService(
            evidenceRepository)
        .PromoteAsync(
            new IntelligenceEvidencePromotionRequest<
                EMF.Intelligence.Capabilities.TextInsight>
            {
                Artifact = evidenceArtifact,
                IntelligenceResult = result,
                PromotedBy = subjectId,
                PromotedUtc = promotedUtc,
                ReviewedBy = reviewedBy,
                ReviewedUtc =
                    reviewedBy is null
                        ? null
                        : promotedUtc
            });
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

if (evidenceArtifact is not null)
{
    Console.WriteLine(
        $"Evidence    : {evidenceArtifact.Id.Value}");
    Console.WriteLine(
        $"Evidence DB : {evidenceDatabasePath}");
}

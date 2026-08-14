using EMF.Core.Models.Identities;
using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Development.Composition;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Authorization.Services;
using EMF.Security.Models;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class
    DevelopmentTextIntelligenceCompositionTests
{
    [Fact]
    public async Task Composition_ExecutesConfiguredLocalAgent()
    {
        var classificationId =
            new ProtectionClassificationId(
                "confidential");

        var authorizationPolicy =
            new AuthorizationPolicy(
                new InMemoryAuthorizationContextProvider(
                    [
                        new AuthorizationContext
                        {
                            SubjectId =
                                "security-steward",
                            RoleIds =
                                Array.Empty<RoleId>(),
                            PermissionIds =
                            [
                                SecurityPermissions
                                    .ArtifactIntelligenceUse
                            ]
                        }
                    ]));

        var auditSink =
            new RecordingAuditSink();

        var composition =
            new DevelopmentTextIntelligenceComposition(
                authorizationPolicy,
                auditSink,
                [classificationId]);

        var context =
            new IntelligenceExecutionContext(
                "security-steward",
                new IntelligenceCorrelationId(
                    "composition-operation-001"),
                classificationId,
                [
                    new ArtifactId(
                        "artifact-001")
                ],
                IntelligenceAgentIds.LongTextInsight);

        var result =
            await composition
                .LongTextInsightAgentExecutor
                .ExecuteAsync(
                    IntelligenceAgentIds
                        .LongTextInsight,
                    new LongTextInsightRequest(
                        "alpha beta alpha gamma",
                        11,
                        0,
                        7,
                        3,
                        4),
                    context);

        Assert.True(result.Success);
        Assert.NotNull(result.Output);

        Assert.Equal(
            string.Join(
                Environment.NewLine,
                "alpha…",
                "alpha…"),
            result.Output!.Summary);

        Assert.Equal(
            ["alpha", "beta", "gamma"],
            result.Output.Keywords
                .Select(keyword => keyword.Term)
                .ToArray());

        Assert.Equal(5, auditSink.Records.Count);

        Assert.All(
            auditSink.Records,
            record =>
                Assert.Equal(
                    context.CorrelationId.Value,
                    record.Facts["correlationId"]));
    }

    [Fact]
    public void Constructor_RejectsNoClassifications()
    {
        var authorizationPolicy =
            new AuthorizationPolicy(
                new InMemoryAuthorizationContextProvider(
                    Array.Empty<
                        AuthorizationContext>()));

        Assert.Throws<ArgumentException>(
            () =>
                new
                    DevelopmentTextIntelligenceComposition(
                        authorizationPolicy,
                        new RecordingAuditSink(),
                        Array.Empty<
                            ProtectionClassificationId>()));
    }

    private sealed class RecordingAuditSink :
        ISecurityAuditSink
    {
        public List<SecurityAuditRecord> Records
        { get; } = [];

        public Task WriteAsync(
            SecurityAuditRecord record,
            CancellationToken cancellationToken =
                default)
        {
            Records.Add(record);

            return Task.CompletedTask;
        }
    }
}

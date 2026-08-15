using EMF.Intelligence.Contracts;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;

namespace EMF.Tests;

public sealed partial class IntelligenceCapabilityExecutorTests
{
    [Fact]
    public void Constructor_RejectsNullRouter()
    {
        Assert.Throws<ArgumentNullException>(
            () => new IntelligenceCapabilityExecutor<
                string,
                string>(
                null!,
                new RecordingAuthorizationPolicy(),
                new RecordingAuditSink()));
    }

    [Fact]
    public void Constructor_RejectsNullAuthorizationPolicy()
    {
        Assert.Throws<ArgumentNullException>(
            () => new IntelligenceCapabilityExecutor<
                string,
                string>(
                CreateRouter(),
                null!,
                new RecordingAuditSink()));
    }

    [Fact]
    public void Constructor_RejectsNullAuditSink()
    {
        Assert.Throws<ArgumentNullException>(
            () => new IntelligenceCapabilityExecutor<
                string,
                string>(
                CreateRouter(),
                new RecordingAuthorizationPolicy(),
                null!));
    }

    [Fact]
    public async Task ExecuteAsync_RejectsDefaultCapabilityId()
    {
        var executor =
            CreateExecutor(
                out var auditSink);

        await Assert.ThrowsAsync<
            ArgumentNullException>(
            () => executor.ExecuteAsync(
                default,
                "request-content",
                CreateContext()));

        Assert.Empty(auditSink.Records);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNullRequest()
    {
        var executor =
            CreateExecutor(
                out var auditSink);

        await Assert.ThrowsAsync<
            ArgumentNullException>(
            () => executor.ExecuteAsync(
                new IntelligenceCapabilityId(
                    "document-analysis"),
                null!,
                CreateContext()));

        Assert.Empty(auditSink.Records);
    }

    [Fact]
    public async Task ExecuteAsync_RejectsNullContext()
    {
        var executor =
            CreateExecutor(
                out var auditSink);

        await Assert.ThrowsAsync<
            ArgumentNullException>(
            () => executor.ExecuteAsync(
                new IntelligenceCapabilityId(
                    "document-analysis"),
                "request-content",
                null!));

        Assert.Empty(auditSink.Records);
    }

    private static IntelligenceCapabilityExecutor<
        string,
        string> CreateExecutor(
            out RecordingAuditSink auditSink)
    {
        auditSink = new RecordingAuditSink();

        return new IntelligenceCapabilityExecutor<
            string,
            string>(
                CreateRouter(),
                new RecordingAuthorizationPolicy(),
                auditSink);
    }

    private static IntelligenceCapabilityProviderRouter<
        string,
        string> CreateRouter()
    {
        return new IntelligenceCapabilityProviderRouter<
            string,
            string>(
                Array.Empty<
                    IIntelligenceCapabilityProvider<
                        string,
                        string>>(),
                new ConfiguredIntelligenceProviderRoutingPolicy(
                    Array.Empty<
                        IntelligenceProviderRoutingGrant>()));
    }
}

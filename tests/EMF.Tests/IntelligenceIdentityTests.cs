using EMF.Intelligence.Models.Identities;

namespace EMF.Tests;

public sealed class IntelligenceIdentityTests
{
    [Fact]
    public void Identities_PreserveValues()
    {
        var capabilityId =
            new IntelligenceCapabilityId(
                "document.summarize");

        var agentId =
            new AgentId(
                "veterans-claims-review-agent");

        var correlationId =
            new IntelligenceCorrelationId(
                "operation-001");

        Assert.Equal(
            "document.summarize",
            capabilityId.Value);

        Assert.Equal(
            "veterans-claims-review-agent",
            agentId.Value);

        Assert.Equal(
            "operation-001",
            correlationId.Value);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Identities_RejectEmptyValues(
        string value)
    {
        Assert.Throws<ArgumentException>(
            () =>
                new IntelligenceCapabilityId(
                    value));

        Assert.Throws<ArgumentException>(
            () => new AgentId(value));

        Assert.Throws<ArgumentException>(
            () =>
                new IntelligenceCorrelationId(
                    value));
    }
}

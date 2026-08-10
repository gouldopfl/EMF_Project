using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransClaimsIdentityTests
{
    [Fact]
    public void VeteranId_PreservesValue()
    {
        var id = new VeteranId("veteran-001");

        Assert.Equal("veteran-001", id.Value);
        Assert.Equal("veteran-001", id.ToString());
    }

    [Fact]
    public void ClaimId_PreservesValue()
    {
        var id = new ClaimId("claim-001");

        Assert.Equal("claim-001", id.Value);
        Assert.Equal("claim-001", id.ToString());
    }

    [Fact]
    public void ClaimIssueId_PreservesValue()
    {
        var id = new ClaimIssueId("claim-issue-001");

        Assert.Equal("claim-issue-001", id.Value);
        Assert.Equal("claim-issue-001", id.ToString());
    }

    [Fact]
    public void SubmissionId_PreservesValue()
    {
        var id = new SubmissionId("submission-001");

        Assert.Equal("submission-001", id.Value);
        Assert.Equal("submission-001", id.ToString());
    }

    [Fact]
    public void IdentityTypes_UseValueEquality()
    {
        Assert.Equal(
            new VeteranId("veteran-001"),
            new VeteranId("veteran-001"));

        Assert.NotEqual(
            new VeteranId("veteran-001"),
            new VeteranId("veteran-002"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void VeteranId_RejectsBlankValue(string value)
    {
        Assert.Throws<ArgumentException>(() => new VeteranId(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ClaimId_RejectsBlankValue(string value)
    {
        Assert.Throws<ArgumentException>(() => new ClaimId(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ClaimIssueId_RejectsBlankValue(string value)
    {
        Assert.Throws<ArgumentException>(() => new ClaimIssueId(value));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void SubmissionId_RejectsBlankValue(string value)
    {
        Assert.Throws<ArgumentException>(() => new SubmissionId(value));
    }
}

using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Tests;

public sealed class ReviewerPackageOutputBuildProvenanceTests
{
    private static readonly string ProvenanceId = new('A', 64);
    private static readonly string Revision = new('b', 40);

    [Fact]
    public void Create_IsDeterministicForSameBinding()
    {
        var when = DateTimeOffset.Parse("2026-09-29T12:00:00Z");

        var first = ReviewerPackageOutputBuildProvenance.Create(
            ProvenanceId,
            "sha256:" + new string('C', 64),
            Revision,
            when);

        var second = ReviewerPackageOutputBuildProvenance.Create(
            ProvenanceId,
            "sha256:" + new string('C', 64),
            Revision,
            when.AddHours(1));

        Assert.Equal(first.LinkId, second.LinkId);
    }

    [Fact]
    public void SameOutputMayBindToDifferentBuilds()
    {
        var first = ReviewerPackageOutputBuildProvenance.Create(
            ProvenanceId,
            "sha256:" + new string('C', 64),
            Revision,
            DateTimeOffset.UtcNow);

        var second = ReviewerPackageOutputBuildProvenance.Create(
            ProvenanceId,
            "sha256:" + new string('D', 64),
            Revision,
            DateTimeOffset.UtcNow);

        Assert.NotEqual(first.LinkId, second.LinkId);
    }

    [Fact]
    public void ValidateIntegrity_RejectsTamperedBuildId()
    {
        var row = ReviewerPackageOutputBuildProvenance.Create(
            ProvenanceId,
            "sha256:" + new string('C', 64),
            Revision,
            DateTimeOffset.UtcNow);

        Assert.Throws<InvalidDataException>(() =>
            (row with
            {
                BuildId = "sha256:" + new string('D', 64)
            }).ValidateIntegrity());
    }
}

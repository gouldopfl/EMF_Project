using System.Text.Json;
using System.Text.Json.Nodes;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageOutputReuseTests
{
    [Fact]
    public void SameV1Inputs_ReuseSealedIdentityAcrossPackageIdsAndRetrievalTimes()
    {
        var original = Snapshot();
        var current = Change(original, root =>
        {
            root["Details"]!["PackageDetails"]!["Package"]!["Id"] = "fresh";
            foreach (var member in root["Details"]!["PackageDetails"]!["Artifacts"]!.AsArray()) member!["EvidencePackageId"] = "fresh";
            root["Regulations"]![0]!["RetrievedUtc"] = "1970-01-01T12:00:00.0000000+00:00";
        }, "fresh");
        var decision = VeteransReviewerPackageOutputReuse.Decide(new(false, original), current);
        Assert.True(decision.ReuseSealedPackage);
        Assert.Equal(original.PackageId, decision.PackageId);
        Assert.Equal(original.Sha256, decision.SealedSnapshotSha256);
        Assert.Equal(VeteransReviewerPackageOutputReuse.Fingerprint(original), decision.CurrentFingerprint);
    }

    [Theory]
    [InlineData("purpose")]
    [InlineData("role")]
    [InlineData("basis")]
    [InlineData("metadata")]
    [InlineData("request")]
    [InlineData("medications")]
    [InlineData("reconciliation")]
    [InlineData("pages")]
    [InlineData("literature")]
    [InlineData("classification")]
    [InlineData("clarifications")]
    [InlineData("progression")]
    [InlineData("regulatory-text")]
    [InlineData("regulatory-version")]
    [InlineData("regulatory-source")]
    [InlineData("regulatory-source-hash")]
    [InlineData("regulatory-retrieval-date")]
    public void ChangedRendererInputs_RequireNewPendingIdentity(string change)
    {
        var original = Snapshot();
        var current = Change(original, root =>
        {
            var details = root["Details"]!;
            var package = details["PackageDetails"]!["Package"]!;
            switch (change)
            {
                case "purpose": package["Purpose"] = "Other purpose"; break;
                case "role": package["ReviewerRole"] = "Other reviewer"; break;
                case "basis": package["ServiceConnectionBasisId"] = "other-basis"; break;
                case "metadata": details["ArtifactContents"]![0]!["Artifact"]!["Name"] = "Changed reviewer title"; break;
                case "request": details["MedicalOpinionRequested"]!["OpinionText"] = "Changed request"; break;
                case "medications": details["CurrentMedications"]![0]!["Directions"] = "Changed use"; break;
                case "reconciliation": details["MedicationProgressions"]![0]!["IndicationReconciliation"]!["Indication"] = "Changed indication"; break;
                case "pages": details["PackageDetails"]!["Artifacts"]![0]!["ReviewerPageSelection"] = null; details["ArtifactContents"]![0]!["ReviewerPageSelection"] = null; break;
                case "literature": details["ArtifactContents"]![0]!["MedicalLiteratureReviewerText"] = "Changed text"; break;
                case "classification": details["ArtifactContents"]![0]!["ReviewedMedicalLiteratureClassifications"]![0]!["Association"]!["Description"] = "Changed classification"; break;
                case "clarifications": details["SourceClarifications"]![0]!["Clarification"] = "Changed clarification"; break;
                case "progression": details["ClinicalProgressionEvents"]![0]!["Summary"] = "Changed progression"; break;
                case "regulatory-text": root["Regulations"]![0]!["Text"] = "Changed regulation"; break;
                case "regulatory-source": root["Regulations"]![0]!["SourceUri"] = "https://example.invalid/changed"; break;
                case "regulatory-source-hash": root["Regulations"]![0]!["SourceSha256"] = new string('b', 64); break;
                case "regulatory-retrieval-date": root["Regulations"]![0]!["RetrievedUtc"] = "2026-09-26T00:00:00.0000000+00:00"; break;
                case "regulatory-version": root["Regulations"]![0]!["UpToDateAsOf"] = "2026-09-26"; break;
            }
        });
        var decision = VeteransReviewerPackageOutputReuse.Decide(new(false, original), current);
        Assert.False(decision.ReuseSealedPackage);
    }

    [Fact]
    public void LegacyAndPending_AreNeverImmutableOutputReusable()
    {
        Assert.False(VeteransReviewerPackageOutputReuse.Decide(new(true, null), Snapshot()).ReuseSealedPackage);
        Assert.False(VeteransReviewerPackageOutputReuse.Decide(new(false, null), Snapshot()).ReuseSealedPackage);
    }

    [Fact]
    public void CorruptOrUnsupportedSnapshot_FailsClosed()
    {
        var row = Snapshot();
        Assert.Throws<InvalidDataException>(() => VeteransReviewerPackageOutputReuse.Decide(new(false, row with { Sha256 = new string('0', 64) }), row));
        Assert.Throws<InvalidDataException>(() => VeteransReviewerPackageOutputReuse.Decide(new(false, row with { Version = 2 }), row));
    }

    internal static ReviewerPackageSnapshot Snapshot() => VeteransReviewerPackageSnapshot.Capture(
        VeteransReviewerPackageSnapshotTests.Details(literature: true), VeteransReviewerPackageSnapshotTests.Regulations());

    internal static ReviewerPackageSnapshot Change(ReviewerPackageSnapshot row, Action<JsonNode> change, string? packageId = null)
    {
        var node = JsonNode.Parse(row.Payload)!;
        change(node);
        using var document = JsonDocument.Parse(node.ToJsonString());
        var payload = VeteransReviewerPackageSnapshot.Canonical(document.RootElement);
        return row with { PackageId = packageId is null ? row.PackageId : new(packageId), Payload = payload, Sha256 = ReviewerPackageSnapshot.ComputeHash(payload) };
    }
}

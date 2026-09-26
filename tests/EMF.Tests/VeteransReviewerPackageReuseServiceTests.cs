using System.Text.Json.Nodes;
using EMF.Common;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Services;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageReuseServiceTests
{
    [Theory]
    [InlineData(VeteransReviewerPackageOutputFormat.Docx)]
    [InlineData(VeteransReviewerPackageOutputFormat.Pdf)]
    [InlineData(VeteransReviewerPackageOutputFormat.Both)]
    public async Task UnchangedInputs_KeepSealedIdentity_ForEveryOutputFormat(VeteransReviewerPackageOutputFormat format)
    {
        await using var db = await Database.Create();
        var original = SummarySnapshot();
        await db.SeedPackage(original);
        var requested = await db.Preview(original);
        var provider = new Regulations();
        var selection = await new VeteransReviewerPackageReuseService(db.Repository).SelectAsync(original.PackageId, requested,
            (p, _) => Task.FromResult(Current(original, p)), provider, true);
        Assert.True(selection.ReusedSealedPackage);
        Assert.Equal(original.PackageId, selection.PackageId);
        Assert.Equal(original, selection.OutputSnapshot);
        Assert.Single(await db.Repository.GetEvidencePackagesAsync(new("issue")));
        provider.Throw = true;
        var output = await new VeteransReviewerPackageDocumentOutputService(new Converter(), provider, db.Repository)
            .RenderAsync(VeteransReviewerPackageSnapshot.Restore(selection.OutputSnapshot!).Details, format,
                preparedSnapshot: selection.OutputSnapshot);
        Assert.Equal(format != VeteransReviewerPackageOutputFormat.Pdf, output.Docx is not null);
        Assert.Equal(format != VeteransReviewerPackageOutputFormat.Docx, output.Pdf is not null);
    }

    [Fact]
    public async Task ChangedMetadata_PreservesSummaryButCreatesPendingPackage_AndRendersComparedInputs()
    {
        await using var db = await Database.Create();
        var original = SummarySnapshot();
        await db.SeedPackage(original);
        var requested = await db.Preview(original);
        var provider = new Regulations();
        var selection = await new VeteransReviewerPackageReuseService(db.Repository).SelectAsync(original.PackageId, requested,
            (p, _) => Task.FromResult(Current(original, p, "Changed title")), provider, true);
        Assert.False(selection.ReusedSealedPackage);
        Assert.NotEqual(original.PackageId, selection.PackageId);
        Assert.Null(await db.Repository.GetReviewerSnapshotAsync(selection.PackageId));
        Assert.Contains(await db.Repository.GetEvidencePackageArtifactsAsync(selection.PackageId),
            x => x.ArtifactId.Value == "summary" && x.ContentRole == EvidencePackageContentRoles.GeneratedOrganizationalMaterial);
        Assert.Equal(original, await db.Repository.GetReviewerSnapshotAsync(original.PackageId));
        provider.Throw = true;
        // Later DTO/provider changes cannot replace the captured current view.
        await new VeteransReviewerPackageDocumentOutputService(regulatoryTextProvider: provider, snapshotRepository: db.Repository)
            .RenderAsync(Current(original, requested, "Too late"), VeteransReviewerPackageOutputFormat.Docx,
                preparedSnapshot: selection.OutputSnapshot);
        var sealedRow = await db.Repository.GetReviewerSnapshotAsync(selection.PackageId);
        Assert.Equal(selection.OutputSnapshot, sealedRow);
        Assert.Contains("Changed title", sealedRow!.Payload);
        Assert.DoesNotContain("Too late", sealedRow.Payload);
    }

    [Fact]
    public async Task PreparedOutput_RejectsConflictingConcurrentSeal()
    {
        await using var db = await Database.Create();
        var original = SummarySnapshot();
        await db.SeedPackage(original);
        var requested = await db.Preview(original);
        var selection = await new VeteransReviewerPackageReuseService(db.Repository).SelectAsync(original.PackageId, requested,
            (p, _) => Task.FromResult(Current(original, p, "Compared title")), new Regulations(), true);
        var competing = VeteransReviewerPackageSnapshot.Capture(Current(original, requested, "Concurrent title"),
            VeteransReviewerPackageSnapshotTests.Regulations());
        await db.Repository.SaveReviewerSnapshotAsync(competing, requested);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new VeteransReviewerPackageDocumentOutputService(regulatoryTextProvider: new Regulations { Throw = true }, snapshotRepository: db.Repository)
                .RenderAsync(Current(original, requested), VeteransReviewerPackageOutputFormat.Docx,
                    preparedSnapshot: selection.OutputSnapshot));
        Assert.Equal(competing, await db.Repository.GetReviewerSnapshotAsync(requested.Package.Id));
    }

    [Fact]
    public async Task LatestPageSelection_IsCarriedToNewPendingPackage_NotIntoOldSeal()
    {
        await using var db = await Database.Create();
        var original = SummarySnapshot();
        await db.SeedPackage(original);
        var changedSelection = await db.Preview(original);
        await db.Repository.AddEvidencePackageAsync(changedSelection.Package, changedSelection.Artifacts.ToArray());
        await db.Repository.SetReviewerPageSelectionAsync(changedSelection.Package.Id, new("source"), null);
        var requested = await db.Preview(original);
        var selection = await new VeteransReviewerPackageReuseService(db.Repository).SelectAsync(original.PackageId, requested,
            (p, _) => Task.FromResult(Current(original, p)), new Regulations(), true);
        Assert.False(selection.ReusedSealedPackage);
        Assert.Null((await db.Repository.GetEvidencePackageArtifactsAsync(selection.PackageId)).Single(x => x.ArtifactId.Value == "source").ReviewerPageSelection);
        Assert.Equal("2", (await db.Repository.GetEvidencePackageArtifactsAsync(original.PackageId)).Single(x => x.ArtifactId.Value == "source").ReviewerPageSelection);
    }

    [Fact]
    public async Task LegacyOwner_ReusesSummaryOnly_WithoutBackfill()
    {
        await using var db = await Database.Create();
        var original = SummarySnapshot();
        var old = VeteransReviewerPackageSnapshot.Restore(original).Details.PackageDetails;
        await db.Sql("INSERT INTO VeteransClaims_EvidencePackages(Id,ClaimIssueId,Purpose,ReviewerRole,CreationOrdinal) VALUES ('package','issue','Independent medical review','MedicalProfessional',1);");
        foreach (var member in old.Artifacts) await db.Repository.AddEvidencePackageArtifactAsync(member);
        var requested = await db.Preview(original);
        var selection = await new VeteransReviewerPackageReuseService(db.Repository).SelectAsync(original.PackageId, requested,
            (p, _) => Task.FromResult(Current(original, p)), new Regulations(), true);
        Assert.False(selection.ReusedSealedPackage);
        Assert.True((await db.Repository.ReadReviewerSnapshotAsync(original.PackageId)).IsLegacy);
        Assert.Null(await db.Repository.GetReviewerSnapshotAsync(selection.PackageId));
    }

    [Fact]
    public async Task CorruptSeal_FailsBeforeCurrentLookupOrPackageCreation()
    {
        await using var db = await Database.Create();
        var original = SummarySnapshot();
        await db.SeedPackage(original);
        var requested = await db.Preview(original);
        await db.Sql("DROP TRIGGER ReviewerSnapshot_NoUpdate; UPDATE VeteransClaims_ReviewerPackageSnapshots SET Payload='{}', Sha256='" + ReviewerPackageSnapshot.ComputeHash("{}") + "';");
        await Assert.ThrowsAsync<InvalidDataException>(() => new VeteransReviewerPackageReuseService(db.Repository).SelectAsync(original.PackageId, requested,
            (_, _) => throw new Exception("Must not assemble"), new Regulations { Throw = true }, true));
        Assert.Single(await db.Repository.GetEvidencePackagesAsync(new("issue")));
    }

    [Fact]
    public async Task NoOutputRequest_CreatesPendingIdentityWithoutClaimingOutputReuse()
    {
        await using var db = await Database.Create();
        var original = SummarySnapshot();
        await db.SeedPackage(original);
        var requested = await db.Preview(original);
        var selection = await new VeteransReviewerPackageReuseService(db.Repository).SelectAsync(original.PackageId, requested,
            (_, _) => throw new Exception("No output assembly requested"), new Regulations { Throw = true }, false);
        Assert.False(selection.ReusedSealedPackage);
        Assert.Null(selection.OutputSnapshot);
        Assert.Null(await db.Repository.GetReviewerSnapshotAsync(selection.PackageId));
    }

    [Fact]
    public async Task RegulatoryLookupFailure_DoesNotReuseStaleOutputOrCreatePackage()
    {
        await using var db = await Database.Create();
        var original = SummarySnapshot();
        await db.SeedPackage(original);
        var requested = await db.Preview(original);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new VeteransReviewerPackageReuseService(db.Repository).SelectAsync(original.PackageId, requested,
            (p, _) => Task.FromResult(Current(original, p)), new Regulations { Throw = true }, true));
        Assert.Single(await db.Repository.GetEvidencePackagesAsync(new("issue")));
    }

    private static ReviewerPackageSnapshot SummarySnapshot() => VeteransReviewerPackageOutputReuseTests.Change(
        VeteransReviewerPackageOutputReuseTests.Snapshot(), root =>
        {
            var details = root["Details"]!;
            var artifact = details["Artifacts"]![0]!.DeepClone();
            artifact["Id"] = "summary"; artifact["Name"] = "Reviewed summary"; artifact["ArtifactType"] = "text-summary";
            details["Artifacts"]!.AsArray().Add(artifact.DeepClone());
            var content = details["ArtifactContents"]![0]!.DeepClone();
            content["Artifact"] = artifact; content["Text"] = "Existing reviewed organizational summary";
            content["PrintablePages"] = new JsonArray(); content["ReviewerPageSelection"] = null;
            content["ReviewedMedicalLiteratureClassifications"] = new JsonArray(); content["Appendix"] = null;
            content["MedicalLiteratureReviewerText"] = null;
            details["ArtifactContents"]!.AsArray().Add(content);
            details["PackageDetails"]!["Artifacts"]!.AsArray().Add(new JsonObject
            { ["EvidencePackageId"] = "package", ["ArtifactId"] = "summary", ["ContentRole"] = EvidencePackageContentRoles.GeneratedOrganizationalMaterial, ["ReviewerPageSelection"] = null });
        });

    private static VeteransReviewerPackageDetails Current(ReviewerPackageSnapshot original, EvidencePackageDetails requested, string? title = null)
    {
        var row = VeteransReviewerPackageOutputReuseTests.Change(original, root =>
        {
            var details = root["Details"]!;
            details["PackageDetails"]!["Package"]!["Id"] = requested.Package.Id.Value;
            foreach (var member in details["PackageDetails"]!["Artifacts"]!.AsArray())
            {
                member!["EvidencePackageId"] = requested.Package.Id.Value;
                member["ReviewerPageSelection"] = requested.Artifacts.Single(x => x.ArtifactId.Value == member["ArtifactId"]!.GetValue<string>()).ReviewerPageSelection;
            }
            foreach (var content in details["ArtifactContents"]!.AsArray())
                content!["ReviewerPageSelection"] = requested.Artifacts.Single(x => x.ArtifactId.Value == content["Artifact"]!["Id"]!.GetValue<string>()).ReviewerPageSelection;
            if (title is not null) details["ArtifactContents"]![0]!["Artifact"]!["Name"] = title;
        }, requested.Package.Id.Value);
        return VeteransReviewerPackageSnapshot.Restore(row).Details;
    }

    private sealed class Regulations : IVeteransReviewerRegulatoryTextProvider
    {
        public bool Throw;
        public Task<IReadOnlyList<VeteransReviewerApplicableRegulation>> GetCurrentAsync(IReadOnlyList<string> citations, CancellationToken cancellationToken = default) =>
            Throw ? throw new InvalidOperationException("Current provider unavailable") : Task.FromResult(VeteransReviewerPackageSnapshotTests.Regulations());
    }
    private sealed class Converter : IVeteransReviewerPackageDocumentConverter
    {
        public Task<byte[]> ConvertDocxToPdfAsync(ReadOnlyMemory<byte> docx, CancellationToken cancellationToken = default) => Task.FromResult("%PDF-synthetic"u8.ToArray());
    }
    private sealed class Database(string path) : IAsyncDisposable
    {
        public SqliteEvidencePackageRepository Repository { get; } = new(path);
        public static async Task<Database> Create()
        {
            var path = Path.GetTempFileName();
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var db = new Database(path);
            await db.Sql("INSERT INTO VeteransClaims_Veterans VALUES ('veteran'); INSERT INTO VeteransClaims_Claims VALUES ('claim','veteran'); INSERT INTO VeteransClaims_ClaimIssues VALUES ('issue','claim','ServiceConnection');");
            return db;
        }
        public async Task SeedPackage(ReviewerPackageSnapshot row)
        {
            var details = VeteransReviewerPackageSnapshot.Restore(row).Details.PackageDetails;
            await Repository.AddEvidencePackageAsync(details.Package, details.Artifacts.ToArray());
            await Repository.SaveReviewerSnapshotAsync(row, details);
        }
        public Task<EvidencePackageDetails> Preview(ReviewerPackageSnapshot original)
        {
            var package = VeteransReviewerPackageSnapshot.Restore(original).Details.PackageDetails.Package;
            return new EvidencePackageService(Repository, new GuidIdGenerator()).PrepareDetailsAsync(package.ClaimIssueId,
                package.Purpose, package.ReviewerRole, [new("source")], [new("summary")], package.ServiceConnectionBasisId);
        }
        public async Task Sql(string sql)
        {
            await using var c = new SqliteConnection($"Data Source={path}"); await c.OpenAsync();
            await using var command = c.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
        }
        public ValueTask DisposeAsync() { SqliteConnection.ClearAllPools(); File.Delete(path); return ValueTask.CompletedTask; }
    }
}

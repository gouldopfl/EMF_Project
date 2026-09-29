using EMF.Common;
using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

[Collection(ReviewerDeploymentEnvironmentCollection.Name)]
public sealed class ReviewerDeploymentCompositionTests
{
    [Theory]
    [InlineData("unset")]
    [InlineData("relative")]
    [InlineData("missing")]
    [InlineData("malformed")]
    [InlineData("null-entry")]
    [InlineData("stale")]
    [InlineData("unrelated")]
    public async Task Console_NewGenerationFailsWithoutValidExternalExpectation(string failure)
    {
        using var deployment = new ExpectedReviewerDeployment();
        switch (failure)
        {
            case "unset":
                Environment.SetEnvironmentVariable(ReviewerDeploymentVerification.ManifestEnvironmentVariable, null);
                break;
            case "relative":
                Environment.SetEnvironmentVariable(ReviewerDeploymentVerification.ManifestEnvironmentVariable, "manifest.json");
                break;
            case "missing": File.Delete(deployment.PathValue); break;
            case "malformed": File.WriteAllText(deployment.PathValue, "{bad"); break;
            case "null-entry": File.WriteAllText(deployment.PathValue, "{\"Artifacts\":[null,null]}"); break;
            case "stale":
                EmfBuildManifestFile.Save(deployment.PathValue, EmfBuildManifestIdentity.Create(
                    ReviewerDeploymentTestSupport.ExpectedManifest().Artifacts.Select(a => a with { Sha256 = new string('A', 64) })));
                break;
            case "unrelated":
                EmfBuildManifestFile.Save(deployment.PathValue, EmfBuildManifestIdentity.Capture(typeof(EmfBuildManifest).Assembly));
                break;
        }
        var manifestBefore = File.Exists(deployment.PathValue) ? File.ReadAllBytes(deployment.PathValue) : null;
        await using var db = await Database.Create();
        Assert.Equal(2, await db.Generate());
        Assert.False(File.Exists(db.OutputPath));
        Assert.Null(await db.Repository.GetReviewerSnapshotAsync(Database.PackageId));
        Assert.Empty(await db.Repository.GetReviewerOutputProvenanceAsync(Database.PackageId));
        Assert.Equal(0L, await db.Scalar("SELECT count(*) FROM VeteransClaims_ReviewerPackageOutputBuildProvenance;"));
        Assert.Equal(0L, await db.Scalar("SELECT count(*) FROM VeteransClaims_ReviewerBuildManifests;"));
        // No implicit creation, fallback or repair of a supplied expectation.
        Assert.Equal(manifestBefore, File.Exists(deployment.PathValue) ? File.ReadAllBytes(deployment.PathValue) : null);
    }

    [Fact]
    public async Task Console_ArchivesExternalExpectation_AndReusesWithoutConfiguration()
    {
        using var deployment = new ExpectedReviewerDeployment();
        var expected = EmfBuildManifestFile.Load(deployment.PathValue);
        await using var db = await Database.Create();
        Assert.Equal(0, await db.Generate());
        var bytes = File.ReadAllBytes(db.OutputPath);
        var output = Assert.Single(await db.Repository.GetReviewerOutputProvenanceAsync(Database.PackageId));
        var link = Assert.Single(await db.Repository.GetReviewerOutputBuildProvenanceAsync(output.ProvenanceId));
        Assert.Equal(expected.BuildId, link.BuildId);
        Assert.NotNull(await db.Repository.GetBuildManifestAsync(expected.BuildId));
        Environment.SetEnvironmentVariable(ReviewerDeploymentVerification.ManifestEnvironmentVariable, null);
        File.Delete(deployment.PathValue);
        Assert.Equal(0, await db.Generate());
        Assert.Equal(bytes, File.ReadAllBytes(db.OutputPath));
        Assert.Equal(output, Assert.Single(await db.Repository.GetReviewerOutputProvenanceAsync(Database.PackageId)));
        Assert.Equal(link, Assert.Single(await db.Repository.GetReviewerOutputBuildProvenanceAsync(output.ProvenanceId)));
        Assert.Equal(1L, await db.Scalar("SELECT count(*) FROM VeteransClaims_ReviewerBuildManifests;"));
    }

    [Fact]
    public void ExpectedManifest_IsLoadedLazilyAndPinnedAfterFirstUse()
    {
        var missing = ReviewerDeploymentVerification.Create(null, AppContext.BaseDirectory, typeof(ConsoleCommandRouter).Assembly);
        Assert.Throws<InvalidDataException>(() => missing());
        using var deployment = new ExpectedReviewerDeployment();
        var verify = ReviewerDeploymentVerification.Create(deployment.PathValue, AppContext.BaseDirectory, typeof(ConsoleCommandRouter).Assembly);
        var first = verify();
        EmfBuildManifestFile.Save(deployment.PathValue, EmfBuildManifestIdentity.Capture(typeof(EmfBuildManifest).Assembly));
        Assert.Equal(first.BuildId, verify().BuildId);
    }

    private sealed class Database : IAsyncDisposable
    {
        public static readonly EvidencePackageId PackageId = new("deployment-package");
        private readonly string _path = Path.GetTempFileName();
        public string OutputPath => _path + ".docx";
        public SqliteEvidencePackageRepository Repository => new(_path);

        public static async Task<Database> Create()
        {
            var db = new Database();
            await new VeteransClaimsSqliteSchema(db._path).InitializeAsync();
            await db.Scalar("INSERT INTO VeteransClaims_Veterans VALUES ('veteran'); " +
                "INSERT INTO VeteransClaims_Claims VALUES ('claim','veteran'); " +
                "INSERT INTO VeteransClaims_ClaimIssues VALUES ('issue','claim','ServiceConnection');");
            await db.Repository.AddEvidencePackageAsync(new EvidencePackage
            {
                Id = PackageId,
                ClaimIssueId = new("issue"),
                Purpose = "Medical review",
                ReviewerRole = "MedicalProfessional"
            }, []);
            return db;
        }

        public Task<int> Generate() => VeteransConsoleCommand.RunEvidencePackageDocumentAsync(
            _path, PackageId,
            new VeteransReviewerPackageOutputRequest(VeteransReviewerPackageOutputFormat.Docx, OutputPath, null),
            contentStoreFactory: () => null,
            sourceReviewDate: new DateOnly(2026, 9, 29));

        public async Task<object?> Scalar(string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={_path}");
            await connection.OpenAsync();
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            return await command.ExecuteScalarAsync();
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            File.Delete(OutputPath);
            File.Delete(_path);
            return ValueTask.CompletedTask;
        }
    }
}

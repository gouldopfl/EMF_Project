using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;
using static EMF.Tests.VeteransReviewerPackageSnapshotTests;

namespace EMF.Tests;

public sealed class VeteransReviewerOutputProvenanceTests
{
    private static readonly DateOnly ReviewDate = new(2026, 9, 28);
    private static readonly DateTimeOffset Generated = new(2026, 9, 28, 23, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Identity_IsStableAcrossRetryTime_AndCoversOutputAndBuild()
    {
        var first = Docx(new("package"), new string('A', 64), [1, 2, 3], Generated);
        var retry = Docx(new("package"), new string('A', 64), [1, 2, 3], Generated.AddHours(1));
        var changedBytes = Docx(new("package"), new string('A', 64), [1, 2, 4], Generated);
        var changedBuild = first with { RendererBuild = "build-2" };
        changedBuild = changedBuild with { ProvenanceId = ReviewerPackageOutputProvenance.ComputeIdentity(changedBuild) };

        Assert.Equal(first.ProvenanceId, retry.ProvenanceId);
        Assert.NotEqual(first.ProvenanceId, changedBytes.ProvenanceId);
        Assert.NotEqual(first.ProvenanceId, changedBuild.ProvenanceId);
        first.ValidateIntegrity();
        retry.ValidateIntegrity();
        changedBuild.ValidateIntegrity();
    }

    [Fact]
    public void FormatAndConverterContract_FailsClosed()
    {
        Assert.Throws<InvalidDataException>(() => ReviewerPackageOutputProvenance.Create(
            new("package"), ReviewerPackageOutputFormats.Docx, new string('A', 64),
            "reviewer-docx-v1", "build-1", "LibreOffice", "25.2",
            ReviewDate, new byte[] { 1 }, Generated));

        Assert.Throws<InvalidDataException>(() => ReviewerPackageOutputProvenance.Create(
            new("package"), ReviewerPackageOutputFormats.Pdf, new string('A', 64),
            "reviewer-docx-v1", "build-1", null, null,
            ReviewDate, new byte[] { 1 }, Generated));

        Assert.Throws<InvalidDataException>(() => ReviewerPackageOutputProvenance.Create(
            new("package"), "html", new string('A', 64),
            "reviewer-docx-v1", "build-1", null, null,
            ReviewDate, new byte[] { 1 }, Generated));
    }

    [Fact]
    public async Task PersistedProvenance_RoundTripsAndExactRetryIsIdempotent()
    {
        await using var db = await Database.Create();
        var details = Details();
        var snapshot = await Seal(db, details);

        var docx = Docx(snapshot.PackageId, snapshot.Sha256, [1, 2, 3], Generated);
        await db.Repository.SaveReviewerOutputProvenanceAsync(docx);
        await db.Repository.SaveReviewerOutputProvenanceAsync(
            Docx(snapshot.PackageId, snapshot.Sha256, [1, 2, 3], Generated.AddHours(1)));

        var pdf = ReviewerPackageOutputProvenance.Create(
            snapshot.PackageId,
            ReviewerPackageOutputFormats.Pdf,
            snapshot.Sha256,
            "reviewer-docx-v1",
            "build-1",
            "LibreOffice",
            "25.2.5",
            ReviewDate,
            new byte[] { 4, 5, 6 },
            Generated.AddMinutes(1));
        await db.Repository.SaveReviewerOutputProvenanceAsync(pdf);

        var rows = await db.Repository.GetReviewerOutputProvenanceAsync(snapshot.PackageId);
        Assert.Equal(2, rows.Count);
        Assert.Equal(docx, rows.Single(row => row.Format == ReviewerPackageOutputFormats.Docx));
        Assert.Equal(pdf, rows.Single(row => row.Format == ReviewerPackageOutputFormats.Pdf));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task RepositoryRejectsDifferentVisibleDateAcrossFormatsAndAtomicSavePaths(bool atomic, bool pdf)
    {
        await using var db = await Database.Create();
        var snapshot = await Seal(db, Details());
        var original = Docx(snapshot.PackageId, snapshot.Sha256, [1, 2, 3], Generated);
        await db.Repository.SaveReviewerOutputProvenanceAsync(original);
        var changed = ReviewerPackageOutputProvenance.Create(snapshot.PackageId,
            pdf ? ReviewerPackageOutputFormats.Pdf : ReviewerPackageOutputFormats.Docx,
            snapshot.Sha256, "reviewer-docx-v1", "build-2",
            pdf ? "Synthetic converter" : null, pdf ? "1.0" : null,
            ReviewDate.AddDays(1), new byte[] { 4, 5, 6 }, Generated.AddDays(1));
        var build = ReviewerPackageOutputBuildProvenance.Create(changed.ProvenanceId,
            "sha256:" + new string('C', 64), new string('b', 40), Generated.AddDays(1));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => atomic
            ? db.Repository.SaveReviewerOutputWithBuildProvenanceAsync(changed, build)
            : db.Repository.SaveReviewerOutputProvenanceAsync(changed));
        Assert.Contains("new package version", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(original, Assert.Single(await db.Repository.GetReviewerOutputProvenanceAsync(snapshot.PackageId)));
        Assert.Empty(await db.Repository.GetReviewerOutputBuildProvenanceAsync(changed.ProvenanceId));
    }

    [Fact]
    public async Task Save_RequiresMatchingSealedSnapshot()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());
        var snapshot = VeteransReviewerPackageSnapshot.Capture(details, Regulations());
        var row = Docx(snapshot.PackageId, snapshot.Sha256, [1], Generated);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            db.Repository.SaveReviewerOutputProvenanceAsync(row));

        await db.Repository.SaveReviewerSnapshotAsync(snapshot, details.PackageDetails);
        var wrong = Docx(snapshot.PackageId, new string('F', 64), [1], Generated);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            db.Repository.SaveReviewerOutputProvenanceAsync(wrong));
    }

    [Fact]
    public async Task DatabaseTrigger_RejectsOutputWithoutMatchingSealedSnapshot()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        var sql =
            "INSERT INTO VeteransClaims_ReviewerPackageOutputProvenance " +
            "(ProvenanceId,EvidencePackageId,Version,Format,SnapshotSha256,RendererContract,RendererBuild," +
            "ConverterIdentity,ConverterVersion,SourceReviewDate,OutputSha256,ByteLength,GeneratedUtc) VALUES (" +
            "'" + new string('A', 64) + "','package',1,'docx','" + new string('B', 64) +
            "','reviewer-docx-v1','build-1',NULL,NULL,'2026-09-28','" + new string('C', 64) +
            "',1,'2026-09-28T23:00:00.0000000+00:00');";

        await Assert.ThrowsAsync<SqliteException>(() => db.Sql(sql));

        var snapshot = VeteransReviewerPackageSnapshot.Capture(details, Regulations());
        await db.Repository.SaveReviewerSnapshotAsync(snapshot, details.PackageDetails);
        await Assert.ThrowsAsync<SqliteException>(() => db.Sql(sql));
    }

    [Fact]
    public async Task DatabaseTriggers_KeepOutputProvenanceImmutableAndSnapshotBound()
    {
        await using var db = await Database.Create();
        var details = Details();
        var snapshot = await Seal(db, details);
        var row = Docx(snapshot.PackageId, snapshot.Sha256, [1, 2, 3], Generated);
        await db.Repository.SaveReviewerOutputProvenanceAsync(row);

        await Assert.ThrowsAsync<SqliteException>(() => db.Sql(
            "UPDATE VeteransClaims_ReviewerPackageOutputProvenance SET GeneratedUtc='2027-01-01T00:00:00Z';"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Sql(
            "DELETE FROM VeteransClaims_ReviewerPackageOutputProvenance;"));
        await Assert.ThrowsAsync<SqliteException>(() => db.Sql(
            "INSERT OR REPLACE INTO VeteransClaims_ReviewerPackageOutputProvenance SELECT * FROM VeteransClaims_ReviewerPackageOutputProvenance;"));

        Assert.Single(await db.Repository.GetReviewerOutputProvenanceAsync(snapshot.PackageId));
    }


    [Fact]
    public async Task DatabaseRejectsBuildLinkWithoutOutputProvenance()
    {
        await using var db = await Database.Create();

        var sql =
            "INSERT INTO VeteransClaims_ReviewerPackageOutputBuildProvenance " +
            "(LinkId,ProvenanceId,Version,BuildId,SourceRevisionId,LinkedUtc) VALUES (" +
            "'" + new string('A', 64) + "','" + new string('B', 64) + "',1," +
            "'sha256:" + new string('C', 64) + "','" + new string('d', 40) + "'," +
            "'2026-09-29T12:00:00Z');";

        await Assert.ThrowsAsync<SqliteException>(() => db.Sql(sql));
    }

    [Fact]
    public async Task DatabaseKeepsOutputBuildProvenanceImmutable()
    {
        await using var db = await Database.Create();
        var details = Details();
        var snapshot = await Seal(db, details);
        var row = Docx(snapshot.PackageId, snapshot.Sha256, [1, 2, 3], Generated);
        await db.Repository.SaveReviewerOutputProvenanceAsync(row);

        var insert =
            "INSERT INTO VeteransClaims_ReviewerPackageOutputBuildProvenance " +
            "(LinkId,ProvenanceId,Version,BuildId,SourceRevisionId,LinkedUtc) VALUES (" +
            "'" + new string('D', 64) + "','" + row.ProvenanceId + "',1," +
            "'sha256:" + new string('C', 64) + "','" + new string('b', 40) + "'," +
            "'2026-09-29T12:00:00Z');";

        await db.Sql(insert);

        await Assert.ThrowsAsync<SqliteException>(() => db.Sql(
            "UPDATE VeteransClaims_ReviewerPackageOutputBuildProvenance " +
            "SET LinkedUtc='2027-01-01T00:00:00Z';"));

        await Assert.ThrowsAsync<SqliteException>(() => db.Sql(
            "DELETE FROM VeteransClaims_ReviewerPackageOutputBuildProvenance;"));

        await Assert.ThrowsAsync<SqliteException>(() => db.Sql(
            insert.Replace("INSERT INTO", "INSERT OR REPLACE INTO")));
    }


    [Fact]
    public async Task BuildProvenance_RoundTripsAndExactRetryIsIdempotent()
    {
        await using var db = await Database.Create();
        var details = Details();
        var snapshot = await Seal(db, details);
        var output = Docx(snapshot.PackageId, snapshot.Sha256, [1, 2, 3], Generated);
        await db.Repository.SaveReviewerOutputProvenanceAsync(output);

        var link = ReviewerPackageOutputBuildProvenance.Create(
            output.ProvenanceId,
            "sha256:" + new string('C', 64),
            new string('b', 40),
            Generated);

        await db.Repository.SaveReviewerOutputBuildProvenanceAsync(link);
        await db.Repository.SaveReviewerOutputBuildProvenanceAsync(
            link with { LinkedUtc = Generated.AddHours(1) });

        var rows =
            await db.Repository.GetReviewerOutputBuildProvenanceAsync(
                output.ProvenanceId);

        var stored = Assert.Single(rows);
        Assert.Equal(link.LinkId, stored.LinkId);
        Assert.Equal(link.BuildId, stored.BuildId);
        Assert.Equal(link.SourceRevisionId, stored.SourceRevisionId);
    }


    [Fact]
    public async Task AtomicOutputBuildSave_RoundTripsBothRows()
    {
        await using var db = await Database.Create();
        var details = Details();
        var snapshot = await Seal(db, details);

        var output = Docx(
            snapshot.PackageId,
            snapshot.Sha256,
            [1, 2, 3],
            Generated);

        var build = ReviewerPackageOutputBuildProvenance.Create(
            output.ProvenanceId,
            "sha256:" + new string('C', 64),
            new string('b', 40),
            Generated);

        await db.Repository.SaveReviewerOutputWithBuildProvenanceAsync(
            output,
            build);

        Assert.Equal(
            output,
            Assert.Single(
                await db.Repository.GetReviewerOutputProvenanceAsync(
                    snapshot.PackageId)));

        Assert.Equal(
            build,
            Assert.Single(
                await db.Repository.GetReviewerOutputBuildProvenanceAsync(
                    output.ProvenanceId)));
    }

    [Fact]
    public async Task AtomicOutputBuildSave_RollsBackOutputWhenLinkInsertFails()
    {
        await using var db = await Database.Create();
        var details = Details();
        var snapshot = await Seal(db, details);

        var output = Docx(
            snapshot.PackageId,
            snapshot.Sha256,
            [1, 2, 3],
            Generated);

        var build = ReviewerPackageOutputBuildProvenance.Create(
            output.ProvenanceId,
            "sha256:" + new string('C', 64),
            new string('b', 40),
            Generated);

        await db.Sql(
            "CREATE TRIGGER TestRejectReviewerOutputBuild " +
            "BEFORE INSERT ON VeteransClaims_ReviewerPackageOutputBuildProvenance " +
            "BEGIN SELECT RAISE(ABORT, 'test rejection'); END;");

        await Assert.ThrowsAsync<SqliteException>(() =>
            db.Repository.SaveReviewerOutputWithBuildProvenanceAsync(
                output,
                build));

        Assert.Empty(
            await db.Repository.GetReviewerOutputProvenanceAsync(
                snapshot.PackageId));

        Assert.Empty(
            await db.Repository.GetReviewerOutputBuildProvenanceAsync(
                output.ProvenanceId));
    }

    [Fact]
    public async Task CorruptedPersistedProvenance_FailsClosed()
    {
        await using var db = await Database.Create();
        var details = Details();
        var snapshot = await Seal(db, details);
        var row = Docx(snapshot.PackageId, snapshot.Sha256, [1, 2, 3], Generated);
        await db.Repository.SaveReviewerOutputProvenanceAsync(row);

        await db.Sql(
            "DROP TRIGGER ReviewerOutputProvenance_NoUpdate; " +
            "UPDATE VeteransClaims_ReviewerPackageOutputProvenance SET OutputSha256='" + new string('0', 64) + "';");

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            db.Repository.GetReviewerOutputProvenanceAsync(snapshot.PackageId));
    }

    private static ReviewerPackageOutputProvenance Docx(
        EvidencePackageId packageId,
        string snapshotSha256,
        byte[] bytes,
        DateTimeOffset generatedUtc) =>
        ReviewerPackageOutputProvenance.Create(
            packageId,
            ReviewerPackageOutputFormats.Docx,
            snapshotSha256,
            "reviewer-docx-v1",
            "build-1",
            null,
            null,
            ReviewDate,
            bytes,
            generatedUtc);

    private static async Task<ReviewerPackageSnapshot> Seal(
        Database db,
        VeteransReviewerPackageDetails details)
    {
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());
        var snapshot = VeteransReviewerPackageSnapshot.Capture(details, Regulations());
        await db.Repository.SaveReviewerSnapshotAsync(snapshot, details.PackageDetails);
        return snapshot;
    }

    private sealed class Database(string path) : IAsyncDisposable
    {
        public SqliteEvidencePackageRepository Repository { get; } = new(path);
        public string PathValue => path;

        public static async Task<Database> Create()
        {
            var db = new Database(Path.GetTempFileName());
            await new VeteransClaimsSqliteSchema(db.PathValue).InitializeAsync();
            await db.Sql(
                "INSERT INTO VeteransClaims_Veterans VALUES ('veteran'); " +
                "INSERT INTO VeteransClaims_Claims VALUES ('claim','veteran'); " +
                "INSERT INTO VeteransClaims_ClaimIssues VALUES ('issue','claim','ServiceConnection');");
            return db;
        }

        public async Task Sql(string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }

        public ValueTask DisposeAsync()
        {
            SqliteConnection.ClearAllPools();
            File.Delete(path);
            return ValueTask.CompletedTask;
        }
    }
}

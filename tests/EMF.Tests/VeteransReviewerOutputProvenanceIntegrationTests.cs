using System.Text.Json;
using System.Security.Cryptography;
using EMF.Common;
using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;
using static EMF.Tests.VeteransReviewerPackageSnapshotTests;

namespace EMF.Tests;

#pragma warning disable CS0618 // Exercise compatibility overloads that must reject new M92 generation.
public sealed class VeteransReviewerOutputProvenanceIntegrationTests
{
    private static readonly DateOnly ReviewDate = new(2026, 9, 29);

    private static EmfBuildManifest CurrentBuildManifest() =>
        EmfBuildManifestIdentity.CaptureFirstPartyClosure(
            typeof(ConsoleCommandRouter).Assembly);

    private static EmfVerifiedFirstPartyDeploymentIdentity VerifyCurrentBuild(
        EmfBuildManifest manifest) =>
        EmfBuildManifestIdentity.VerifyFirstPartyDeployment(
            manifest,
            AppContext.BaseDirectory,
            typeof(ConsoleCommandRouter).Assembly);

    private static VeteransReviewerPackageDocumentOutputService CreateVerifiedService(
        IVeteransReviewerPackageDocumentConverter? converter = null,
        IVeteransReviewerRegulatoryTextProvider? regulatoryTextProvider = null,
        IEvidencePackageRepository? snapshotRepository = null,
        EmfBuildManifest? buildManifest = null)
    {
        var expected = buildManifest ?? CurrentBuildManifest();
        return VeteransReviewerPackageDocumentOutputService.CreateForVerifiedDeployment(
            () => VerifyCurrentBuild(expected), converter, regulatoryTextProvider, snapshotRepository);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RuntimeToken_CannotAuthorizeNewM92Generation(bool legacyClosureToken)
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var renderer = typeof(VeteransReviewerPackageDocxRenderer).Assembly;
        var manifest = legacyClosureToken ? CurrentBuildManifest() : EmfBuildManifestIdentity.Capture(renderer);
        var token = legacyClosureToken
            ? EmfBuildManifestIdentity.VerifyFirstPartyClosureRuntime(manifest, typeof(ConsoleCommandRouter).Assembly)
            : EmfBuildManifestIdentity.VerifyRuntime(manifest, renderer);
        var converter = new Converter();
        var service = new VeteransReviewerPackageDocumentOutputService(converter, new Regulations(), db.Repository, manifest, token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RenderAsync(details, VeteransReviewerPackageOutputFormat.Both, sourceReviewDate: ReviewDate));
        Assert.Equal(0, converter.Conversions);
        Assert.Null(await db.Repository.GetReviewerSnapshotAsync(details.PackageDetails.Package.Id));
        Assert.Empty(await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id));
        Assert.Null(await db.Repository.GetBuildManifestAsync(manifest.BuildId));
    }

    [Fact]
    public async Task DeploymentWithoutActualRenderer_IsRejectedBeforeGeneration()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var root = typeof(EmfBuildManifest).Assembly;
        var expected = EmfBuildManifestIdentity.CaptureFirstPartyClosure(root);
        var service = VeteransReviewerPackageDocumentOutputService.CreateForVerifiedDeployment(
            () => EmfBuildManifestIdentity.VerifyFirstPartyDeployment(expected, AppContext.BaseDirectory, root),
            regulatoryTextProvider: new Regulations(), snapshotRepository: db.Repository);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.RenderAsync(details, VeteransReviewerPackageOutputFormat.Docx, sourceReviewDate: ReviewDate));
        Assert.Null(await db.Repository.GetReviewerSnapshotAsync(details.PackageDetails.Package.Id));
        Assert.Empty(await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id));
    }

    [Theory]
    [InlineData(VeteransReviewerPackageOutputFormat.Docx)]
    [InlineData(VeteransReviewerPackageOutputFormat.Pdf)]
    [InlineData(VeteransReviewerPackageOutputFormat.Both)]
    public async Task PureReuse_DoesNotInvokeDeploymentVerification(VeteransReviewerPackageOutputFormat format)
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var first = await CreateVerifiedService(converter, new Regulations(), db.Repository)
            .RenderAsync(details, format, sourceReviewDate: ReviewDate);
        var rows = await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id);
        var beforeConversions = converter.Conversions;
        var service = VeteransReviewerPackageDocumentOutputService.CreateForVerifiedDeployment(
            () => throw new InvalidOperationException("Pure reuse must not verify deployment."),
            converter, new Regulations(), db.Repository);
        var reused = await service.RenderAsync(details, format, sourceReviewDate: ReviewDate,
            existingOutput: new VeteransReviewerPackageExistingOutput(first.Docx, first.Pdf));
        Assert.Equal(first.Docx, reused.Docx);
        Assert.Equal(first.Pdf, reused.Pdf);
        Assert.Equal(beforeConversions, converter.Conversions);
        Assert.Equal(rows, await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id));
        foreach (var row in rows)
            Assert.Single(await db.Repository.GetReviewerOutputBuildProvenanceAsync(row.ProvenanceId));
    }

    [Theory]
    [InlineData(VeteransReviewerPackageOutputFormat.Docx)]
    [InlineData(VeteransReviewerPackageOutputFormat.Pdf)]
    public async Task MixedReuse_VerifiesOnlyForNewOutput(VeteransReviewerPackageOutputFormat initialFormat)
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var expected = CurrentBuildManifest();
        var first = await CreateVerifiedService(converter, new Regulations(), db.Repository, expected)
            .RenderAsync(details, initialFormat, sourceReviewDate: ReviewDate);
        var calls = 0;
        var service = VeteransReviewerPackageDocumentOutputService.CreateForVerifiedDeployment(
            () => { calls++; return VerifyCurrentBuild(expected); }, converter, new Regulations(), db.Repository);
        var result = await service.RenderAsync(details, VeteransReviewerPackageOutputFormat.Both,
            sourceReviewDate: ReviewDate, existingOutput: new VeteransReviewerPackageExistingOutput(first.Docx, first.Pdf));
        Assert.Equal(1, calls);
        Assert.Equal(initialFormat == VeteransReviewerPackageOutputFormat.Docx, result.ReusedDocx);
        Assert.Equal(initialFormat == VeteransReviewerPackageOutputFormat.Pdf, result.ReusedPdf);
        var rows = await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id);
        Assert.Equal(2, rows.Count);
        foreach (var row in rows)
            Assert.Single(await db.Repository.GetReviewerOutputBuildProvenanceAsync(row.ProvenanceId));
    }

    [Fact]
    public async Task CallerMutation_CannotChangeArchivedVerifiedManifest()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var captured = CurrentBuildManifest();
        var callerArtifacts = captured.Artifacts.ToArray();
        var expected = captured with { Artifacts = callerArtifacts };
        var verified = VerifyCurrentBuild(expected);
        callerArtifacts[0] = callerArtifacts[0] with { Sha256 = new string('A', 64) };
        await VeteransReviewerPackageDocumentOutputService.CreateForVerifiedDeployment(
                () => verified, regulatoryTextProvider: new Regulations(), snapshotRepository: db.Repository)
            .RenderAsync(details, VeteransReviewerPackageOutputFormat.Docx, sourceReviewDate: ReviewDate);
        var archived = await db.Repository.GetBuildManifestAsync(captured.BuildId);
        Assert.NotNull(archived);
        var manifest = JsonSerializer.Deserialize<EmfBuildManifest>(archived!.Json)!;
        EmfBuildManifestIdentity.Validate(manifest);
        Assert.Equal(captured.Artifacts.ToArray(), manifest.Artifacts.ToArray());
    }

    [Fact]
    public async Task ConcurrentExactRetries_PreserveAllThreeRecords()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var expected = CurrentBuildManifest();
        await CreateVerifiedService(regulatoryTextProvider: new Regulations(), snapshotRepository: db.Repository, buildManifest: expected)
            .RenderAsync(details, VeteransReviewerPackageOutputFormat.Docx, sourceReviewDate: ReviewDate);
        var row = Assert.Single(await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id));
        var link = Assert.Single(await db.Repository.GetReviewerOutputBuildProvenanceAsync(row.ProvenanceId));
        var archive = await db.Repository.GetBuildManifestAsync(expected.BuildId);
        await Task.WhenAll(Enumerable.Range(1, 6).Select(i => Task.Run(() =>
            db.Repository.SaveReviewerOutputWithBuildProvenanceAndManifestAsync(
                row with { GeneratedUtc = row.GeneratedUtc.AddMinutes(i) },
                link with { LinkedUtc = link.LinkedUtc.AddMinutes(i) }, JsonSerializer.Serialize(expected)))));
        Assert.Equal(row, Assert.Single(await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id)));
        Assert.Equal(link, Assert.Single(await db.Repository.GetReviewerOutputBuildProvenanceAsync(row.ProvenanceId)));
        Assert.Equal(archive!.Json, (await db.Repository.GetBuildManifestAsync(expected.BuildId))!.Json);
    }

    [Fact]
    public async Task PopulatedM91Database_UpgradesWithoutChangingHistoricalIdentities()
    {
        await using var db = new Database(Path.GetTempFileName());
        await new VeteransClaimsSqliteMigrator(db.PathValue,
            VeteransClaimsSqliteMigrations.All.Where(m => m.Version <= 91).ToArray()).MigrateAsync();
        await db.Sql("INSERT INTO VeteransClaims_Veterans VALUES ('veteran'); " +
            "INSERT INTO VeteransClaims_Claims VALUES ('claim','veteran'); " +
            "INSERT INTO VeteransClaims_ClaimIssues VALUES ('issue','claim','ServiceConnection');");
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var snapshot = VeteransReviewerPackageSnapshot.Capture(details, VeteransReviewerPackageSnapshotTests.Regulations());
        await db.Repository.SaveReviewerSnapshotAsync(snapshot, details.PackageDetails);
        var historical = ReviewerPackageOutputProvenance.Create(snapshot.PackageId, "docx", snapshot.Sha256,
            VeteransReviewerPackageRendererIdentity.Contract, "mvid:11111111-1111-1111-1111-111111111111",
            null, null, ReviewDate, new byte[] { 1, 2, 3 }, DateTimeOffset.UtcNow);
        await db.Repository.SaveReviewerOutputProvenanceAsync(historical);
        await new VeteransClaimsSqliteSchema(db.PathValue).InitializeAsync();
        await new VeteransClaimsSqliteSchema(db.PathValue).InitializeAsync();
        Assert.Equal(historical, Assert.Single(await db.Repository.GetReviewerOutputProvenanceAsync(snapshot.PackageId)));
        Assert.Equal(snapshot, await db.Repository.GetReviewerSnapshotAsync(snapshot.PackageId));
        Assert.Empty(await db.Repository.GetReviewerOutputBuildProvenanceAsync(historical.ProvenanceId));
    }

    [Fact]
    public async Task RenderAsync_Docx_PersistsExactBytesBuildAndReviewDateAfterSealing()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        var output = await CreateVerifiedService(
                converter: null,
                regulatoryTextProvider: new Regulations(),
                snapshotRepository: db.Repository,
                buildManifest: CurrentBuildManifest())
            .RenderAsync(
                details,
                VeteransReviewerPackageOutputFormat.Docx,
                sourceReviewDate: ReviewDate);

        var snapshot =
            await db.Repository.GetReviewerSnapshotAsync(details.PackageDetails.Package.Id);
        Assert.NotNull(snapshot);
        var row = Assert.Single(
            await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id));

        Assert.Equal(ReviewerPackageOutputFormats.Docx, row.Format);
        Assert.Equal(snapshot!.Sha256, row.SnapshotSha256);
        Assert.Equal(VeteransReviewerPackageRendererIdentity.Contract, row.RendererContract);
        Assert.Equal(VeteransReviewerPackageRendererIdentity.Build, row.RendererBuild);
        Assert.Null(row.ConverterIdentity);
        Assert.Null(row.ConverterVersion);
        Assert.Equal(ReviewDate, row.SourceReviewDate);
        Assert.Equal(output.Docx!.LongLength, row.ByteLength);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(output.Docx)), row.OutputSha256);
    }

    [Fact]
    public async Task RenderAsync_Both_PersistsDistinctExactOutputsAndDeduplicatesExactRetries()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var service = CreateVerifiedService(
            converter,
            new Regulations(),
            db.Repository,
            CurrentBuildManifest());

        var first = await service.RenderAsync(
            details,
            VeteransReviewerPackageOutputFormat.Both,
            sourceReviewDate: ReviewDate);
        var retry = await service.RenderAsync(
            Details("changed current state"),
            VeteransReviewerPackageOutputFormat.Both,
            sourceReviewDate: ReviewDate);

        // Reprints materialize the frozen plan and PDF, even at a new path.
        // M92 still verifies exact bytes; it does not normalize provenance hashes.
        Assert.Equal(first.Docx, retry.Docx);
        Assert.Equal(first.Pdf, retry.Pdf);
        Assert.Equal(1, converter.Conversions);

        var firstDocxHash = Convert.ToHexString(SHA256.HashData(first.Docx!));
        var retryDocxHash = Convert.ToHexString(SHA256.HashData(retry.Docx!));
        var pdfHash = Convert.ToHexString(SHA256.HashData(first.Pdf!));

        var rows = await db.Repository.GetReviewerOutputProvenanceAsync(
            details.PackageDetails.Package.Id);

        var docxRows = rows
            .Where(row => row.Format == ReviewerPackageOutputFormats.Docx)
            .ToArray();

        var pdf = Assert.Single(
            rows.Where(row => row.Format == ReviewerPackageOutputFormats.Pdf));

        var expectedDocxRows =
            string.Equals(firstDocxHash, retryDocxHash, StringComparison.Ordinal)
                ? 1
                : 2;

        Assert.Equal(expectedDocxRows, docxRows.Length);
        Assert.Equal(expectedDocxRows + 1, rows.Count);

        Assert.Contains(docxRows, row =>
            row.OutputSha256 == firstDocxHash &&
            row.ByteLength == first.Docx!.LongLength);

        Assert.Contains(docxRows, row =>
            row.OutputSha256 == retryDocxHash &&
            row.ByteLength == retry.Docx!.LongLength);

        Assert.All(docxRows, row =>
        {
            Assert.Null(row.ConverterIdentity);
            Assert.Null(row.ConverterVersion);
        });

        Assert.Equal("Synthetic PDF Converter", pdf.ConverterIdentity);
        Assert.Equal("1.2.3", pdf.ConverterVersion);
        Assert.Equal(first.Pdf!.LongLength, pdf.ByteLength);
        Assert.Equal(pdfHash, pdf.OutputSha256);
    }


    [Theory]
    [InlineData(VeteransReviewerPackageOutputFormat.Docx, 2000)]
    [InlineData(VeteransReviewerPackageOutputFormat.Pdf, 2000)]
    [InlineData(VeteransReviewerPackageOutputFormat.Both, 2000)]
    [InlineData(VeteransReviewerPackageOutputFormat.Both, 2050)]
    public async Task PreservedReprintWithoutNewDateRetainsRecordedDateAndExactBytes(
        VeteransReviewerPackageOutputFormat format, int originalYear)
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var service = CreateVerifiedService(converter, new Regulations(), db.Repository, CurrentBuildManifest());
        var originalDate = new DateOnly(originalYear, 1, 1);
        var first = await service.RenderAsync(details, format, sourceReviewDate: originalDate);
        var retry = await service.RenderAsync(details, format,
            existingOutput: new VeteransReviewerPackageExistingOutput(first.Docx, first.Pdf));
        Assert.Equal(first.Docx, retry.Docx);
        Assert.Equal(first.Pdf, retry.Pdf);
        Assert.Equal(format != VeteransReviewerPackageOutputFormat.Pdf, retry.ReusedDocx);
        Assert.Equal(format != VeteransReviewerPackageOutputFormat.Docx, retry.ReusedPdf);
        Assert.Equal(format == VeteransReviewerPackageOutputFormat.Docx ? 0 : 1, converter.Conversions);
        Assert.All(await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id),
            row => Assert.Equal(originalDate, row.SourceReviewDate));
    }

    [Theory]
    [InlineData(VeteransReviewerPackageOutputFormat.Docx)]
    [InlineData(VeteransReviewerPackageOutputFormat.Pdf)]
    public async Task ReprintToNewOutputWithoutExistingBytesRetainsPackageReviewDate(
        VeteransReviewerPackageOutputFormat secondFormat)
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var service = CreateVerifiedService(new Converter(), new Regulations(), db.Repository, CurrentBuildManifest());
        var originalDate = new DateOnly(2000, 1, 1);
        await service.RenderAsync(details, VeteransReviewerPackageOutputFormat.Docx, sourceReviewDate: originalDate);
        await service.RenderAsync(details, secondFormat);
        Assert.All(await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id),
            row => Assert.Equal(originalDate, row.SourceReviewDate));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldPreservedOutputTamperingFailsClosedWithoutNewReviewDate(bool tamperPdf)
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var service = CreateVerifiedService(converter, new Regulations(), db.Repository, CurrentBuildManifest());
        var first = await service.RenderAsync(details, VeteransReviewerPackageOutputFormat.Both,
            sourceReviewDate: new(2000, 1, 1));
        var docx = first.Docx!.ToArray();
        var pdf = first.Pdf!.ToArray();
        if (tamperPdf) pdf[^1] ^= 1;
        else docx[^1] ^= 1;
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.RenderAsync(details,
            VeteransReviewerPackageOutputFormat.Both,
            existingOutput: new VeteransReviewerPackageExistingOutput(docx, pdf)));
        Assert.Contains("does not match persisted provenance", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, converter.Conversions);
        Assert.Equal(2, (await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id)).Count);
    }

    [Fact]
    public async Task ConflictingHistoricalPackageDatesFailClosedInsteadOfChoosingAReprintDate()
    {
        var details = Details();
        var snapshot = VeteransReviewerPackageSnapshot.Capture(details, VeteransReviewerPackageSnapshotTests.Regulations());
        var repository = new M91OnlyRepository();
        await repository.SaveReviewerSnapshotAsync(snapshot, details.PackageDetails);
        foreach (var date in new[] { new DateOnly(2000, 1, 1), new DateOnly(2001, 1, 1) })
            repository.OutputProvenance.Add(ReviewerPackageOutputProvenance.Create(snapshot.PackageId,
                ReviewerPackageOutputFormats.Docx, snapshot.Sha256, VeteransReviewerPackageRendererIdentity.Contract,
                VeteransReviewerPackageRendererIdentity.Build, null, null, date, new byte[] { 1, 2, 3 }, DateTimeOffset.UnixEpoch));
        var service = new VeteransReviewerPackageDocumentOutputService(snapshotRepository: repository);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.RenderAsync(details,
            VeteransReviewerPackageOutputFormat.Docx, sourceReviewDate: new(2000, 1, 1)));
        Assert.Contains("conflicting review dates", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, repository.OutputProvenance.Count);
    }

    [Fact]
    public async Task RefreshedReviewDateRequiresDistinctPackageIdentityAndPreservesOriginal()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var service = CreateVerifiedService(null, new Regulations(), db.Repository, CurrentBuildManifest());
        await service.RenderAsync(details, VeteransReviewerPackageOutputFormat.Docx, sourceReviewDate: ReviewDate);
        var refreshed = VeteransReviewerPackageOutputReuseTests.Change(
            VeteransReviewerPackageSnapshot.Capture(details, VeteransReviewerPackageSnapshotTests.Regulations()), root =>
            {
                var package = root["Details"]!["PackageDetails"]!;
                package["Package"]!["Id"] = "package-refreshed-date";
                foreach (var member in package["Artifacts"]!.AsArray())
                    member!["EvidencePackageId"] = "package-refreshed-date";
            }, "package-refreshed-date");
        var refreshedDetails = VeteransReviewerPackageSnapshot.Restore(refreshed).Details;
        await db.Repository.AddEvidencePackageAsync(refreshedDetails.PackageDetails.Package,
            refreshedDetails.PackageDetails.Artifacts.ToArray());
        await service.RenderAsync(refreshedDetails, VeteransReviewerPackageOutputFormat.Docx,
            preparedSnapshot: refreshed, sourceReviewDate: ReviewDate.AddDays(1));
        Assert.All(await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id),
            row => Assert.Equal(ReviewDate, row.SourceReviewDate));
        Assert.All(await db.Repository.GetReviewerOutputProvenanceAsync(refreshed.PackageId),
            row => Assert.Equal(ReviewDate.AddDays(1), row.SourceReviewDate));
    }

    [Fact]
    public async Task RenderAsync_Both_VerifiedExistingOutputsAreReusedWithoutPdfConversion()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var service = CreateVerifiedService(
            converter,
            new Regulations(),
            db.Repository,
            CurrentBuildManifest());

        var first = await service.RenderAsync(
            details,
            VeteransReviewerPackageOutputFormat.Both,
            sourceReviewDate: ReviewDate);
        var retry = await service.RenderAsync(
            Details("changed current state"),
            VeteransReviewerPackageOutputFormat.Both,
            sourceReviewDate: ReviewDate,
            existingOutput: new VeteransReviewerPackageExistingOutput(
                first.Docx,
                first.Pdf));

        Assert.False(first.ReusedDocx);
        Assert.False(first.ReusedPdf);
        Assert.True(retry.ReusedDocx);
        Assert.True(retry.ReusedPdf);
        Assert.Equal(first.Docx, retry.Docx);
        Assert.Equal(first.Pdf, retry.Pdf);
        Assert.Equal(1, converter.Conversions);
        Assert.Equal(2, (await db.Repository.GetReviewerOutputProvenanceAsync(
            details.PackageDetails.Package.Id)).Count);
    }

    [Fact]
    public async Task RenderAsync_Docx_ArchivesLinkedBuildManifest()
    {
        await using var db = await Database.Create();
        var details = Details();

        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        var build = CurrentBuildManifest();

        await CreateVerifiedService(
                converter: null,
                regulatoryTextProvider: new Regulations(),
                snapshotRepository: db.Repository,
                buildManifest: build)
            .RenderAsync(
                details,
                VeteransReviewerPackageOutputFormat.Docx,
                sourceReviewDate: ReviewDate);

        var output = Assert.Single(
            await db.Repository.GetReviewerOutputProvenanceAsync(
                details.PackageDetails.Package.Id));

        var link = Assert.Single(
            await db.Repository.GetReviewerOutputBuildProvenanceAsync(
                output.ProvenanceId));

        var archived =
            await db.Repository.GetBuildManifestAsync(link.BuildId);

        Assert.NotNull(archived);
        Assert.Equal(build.BuildId, archived!.BuildId);
        Assert.Equal(
            build.SourceRevisionId,
            archived.SourceRevisionId);
        archived.ValidateIntegrity();
        Assert.Equal(JsonSerializer.Serialize(build), archived.Json);
    }

    [Fact]
    public async Task RenderAsync_Docx_ManifestArchiveFailureRollsBackAllProvenance()
    {
        await using var db = await Database.Create();
        var details = Details();

        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        await using (var connection =
            new SqliteConnection($"Data Source={db.PathValue}"))
        {
            await connection.OpenAsync();

            await using var triggerCommand = connection.CreateCommand();
            triggerCommand.CommandText = """
                CREATE TRIGGER M93_TestRejectBuildManifestInsert
                BEFORE INSERT ON VeteransClaims_ReviewerBuildManifests
                BEGIN
                    SELECT RAISE(
                        ABORT,
                        'test manifest archive failure');
                END;
                """;
            await triggerCommand.ExecuteNonQueryAsync();
        }

        var build = CurrentBuildManifest();

        await Assert.ThrowsAsync<SqliteException>(() =>
            CreateVerifiedService(
                    converter: null,
                    regulatoryTextProvider: new Regulations(),
                    snapshotRepository: db.Repository,
                    buildManifest: build)
                .RenderAsync(
                    details,
                    VeteransReviewerPackageOutputFormat.Docx,
                    sourceReviewDate: ReviewDate));

        Assert.Empty(
            await db.Repository.GetReviewerOutputProvenanceAsync(
                details.PackageDetails.Package.Id));

        Assert.Null(
            await db.Repository.GetBuildManifestAsync(build.BuildId));

        await using var verify =
            new SqliteConnection($"Data Source={db.PathValue}");
        await verify.OpenAsync();

        await using var command = verify.CreateCommand();
        command.CommandText = """
            SELECT COUNT(*)
            FROM VeteransClaims_ReviewerPackageOutputBuildProvenance;
            """;

        Assert.Equal(
            0L,
            Convert.ToInt64(
                await command.ExecuteScalarAsync()));
    }

    [Fact]
    public async Task RenderAsync_Docx_UnverifiedManifestFailsClosedForNewM92Generation()
    {
        await using var db = await Database.Create();
        var details = Details();

        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new VeteransReviewerPackageDocumentOutputService(
                    converter: null,
                    regulatoryTextProvider: new Regulations(),
                    snapshotRepository: db.Repository,
                    buildManifest: CurrentBuildManifest())
                .RenderAsync(
                    details,
                    VeteransReviewerPackageOutputFormat.Docx,
                    sourceReviewDate: ReviewDate));

        Assert.Contains(
            "verified first-party deployment identity",
            error.Message,
            StringComparison.OrdinalIgnoreCase);

        Assert.Empty(
            await db.Repository.GetReviewerOutputProvenanceAsync(
                details.PackageDetails.Package.Id));
    }

    [Fact]
    public void Constructor_VerifiedRuntimeMustContainExactReviewerRenderer()
    {
        var manifest =
            EmfBuildManifestIdentity.CaptureFirstPartyClosure(
                typeof(EmfAssemblyBuildIdentity).Assembly);

        var verified =
            EmfBuildManifestIdentity.VerifyFirstPartyClosureRuntime(
                manifest,
                typeof(EmfAssemblyBuildIdentity).Assembly);

        var error = Assert.Throws<InvalidDataException>(() =>
            new VeteransReviewerPackageDocumentOutputService(
                converter: null,
                regulatoryTextProvider: null,
                snapshotRepository: null,
                buildManifest: manifest,
                verifiedRuntimeIdentity: verified));

        Assert.Contains(
            "reviewer renderer assembly",
            error.Message,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RenderAsync_Docx_M91OnlyRepositoryAcceptsSuppliedBuildManifest()
    {
        var repository = new M91OnlyRepository();
        var details = Details();

        var output = await new VeteransReviewerPackageDocumentOutputService(
                    converter: null,
                regulatoryTextProvider: new Regulations(),
                snapshotRepository: repository,
                buildManifest: CurrentBuildManifest())
            .RenderAsync(
                details,
                VeteransReviewerPackageOutputFormat.Docx,
                sourceReviewDate: ReviewDate);

        Assert.NotNull(output.Docx);
        Assert.NotNull(repository.Snapshot);

        var provenance = Assert.Single(repository.OutputProvenance);
        Assert.Equal(details.PackageDetails.Package.Id, provenance.PackageId);

        Assert.False(
            ((IEvidencePackageRepository)repository)
                .SupportsReviewerOutputBuildProvenance);
    }

    [Fact]
    public async Task RenderAsync_Docx_M92RepositoryWithoutBuildManifestFailsClosed()
    {
        await using var db = await Database.Create();
        var details = Details();

        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new VeteransReviewerPackageDocumentOutputService(
                    regulatoryTextProvider: new Regulations(),
                    snapshotRepository: db.Repository)
                .RenderAsync(
                    details,
                    VeteransReviewerPackageOutputFormat.Docx,
                    sourceReviewDate: ReviewDate));

        Assert.Contains(
            "requires a build manifest",
            error.Message,
            StringComparison.Ordinal);

        Assert.Null(
            await db.Repository.GetReviewerSnapshotAsync(
                details.PackageDetails.Package.Id));

        Assert.Empty(
            await db.Repository.GetReviewerOutputProvenanceAsync(
                details.PackageDetails.Package.Id));
    }

    [Fact]
    public async Task RenderAsync_Docx_VerifiedReuseDoesNotRequireLaterBuildManifest()
    {
        await using var db = await Database.Create();
        var details = Details();

        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        var first = await CreateVerifiedService(
                converter: null,
                regulatoryTextProvider: new Regulations(),
                snapshotRepository: db.Repository,
                buildManifest: CurrentBuildManifest())
            .RenderAsync(
                details,
                VeteransReviewerPackageOutputFormat.Docx,
                sourceReviewDate: ReviewDate);

        var output = Assert.Single(
            await db.Repository.GetReviewerOutputProvenanceAsync(
                details.PackageDetails.Package.Id));

        var retry = await new VeteransReviewerPackageDocumentOutputService(
                regulatoryTextProvider: new Regulations(),
                snapshotRepository: db.Repository)
            .RenderAsync(
                Details("changed current state"),
                VeteransReviewerPackageOutputFormat.Docx,
                sourceReviewDate: ReviewDate,
                existingOutput:
                    new VeteransReviewerPackageExistingOutput(first.Docx, null));

        Assert.True(retry.ReusedDocx);
        Assert.Single(
            await db.Repository.GetReviewerOutputBuildProvenanceAsync(
                output.ProvenanceId));
    }

    [Fact]
    public async Task RenderAsync_Docx_LinksOnlyTheBuildThatGeneratedTheOutput()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        var build = CurrentBuildManifest();

        var first = await CreateVerifiedService(
                converter: null,
                regulatoryTextProvider: new Regulations(),
                snapshotRepository: db.Repository,
                buildManifest: build)
            .RenderAsync(
                details,
                VeteransReviewerPackageOutputFormat.Docx,
                sourceReviewDate: ReviewDate);

        var output = Assert.Single(
            await db.Repository.GetReviewerOutputProvenanceAsync(
                details.PackageDetails.Package.Id));

        var link = Assert.Single(
            await db.Repository.GetReviewerOutputBuildProvenanceAsync(
                output.ProvenanceId));

        Assert.Equal(build.BuildId, link.BuildId);

        var secondBuild = EmfBuildManifestIdentity.Capture(
            typeof(EmfAssemblyBuildIdentity).Assembly,
            typeof(VeteransReviewerPackageRendererIdentity).Assembly);

        Assert.NotEqual(build.BuildId, secondBuild.BuildId);

        var retry = await new VeteransReviewerPackageDocumentOutputService(
                    converter: null,
                regulatoryTextProvider: new Regulations(),
                snapshotRepository: db.Repository,
                buildManifest: secondBuild)
            .RenderAsync(
                Details("changed"),
                VeteransReviewerPackageOutputFormat.Docx,
                sourceReviewDate: ReviewDate,
                existingOutput:
                    new VeteransReviewerPackageExistingOutput(first.Docx, null));

        Assert.True(retry.ReusedDocx);
        Assert.Single(
            await db.Repository.GetReviewerOutputBuildProvenanceAsync(
                output.ProvenanceId));
    }

    [Fact]
    public async Task RenderAsync_Docx_BuildLinkFailureRollsBackOutputProvenance()
    {
        await using var db = await Database.Create();
        var details = Details();

        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        await using (var connection =
            new SqliteConnection($"Data Source={db.PathValue}"))
        {
            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                CREATE TRIGGER M92_TestRejectBuildProvenanceInsert
                BEFORE INSERT ON VeteransClaims_ReviewerPackageOutputBuildProvenance
                BEGIN
                    SELECT RAISE(
                        ABORT,
                        'forced build provenance insert failure');
                END;
                """;

            await command.ExecuteNonQueryAsync();
        }

        var build = CurrentBuildManifest();

        await Assert.ThrowsAsync<SqliteException>(() =>
            CreateVerifiedService(
                    converter: null,
                    regulatoryTextProvider: new Regulations(),
                    snapshotRepository: db.Repository,
                    buildManifest: build)
                .RenderAsync(
                    details,
                    VeteransReviewerPackageOutputFormat.Docx,
                    sourceReviewDate: ReviewDate));

        Assert.Empty(
            await db.Repository.GetReviewerOutputProvenanceAsync(
                details.PackageDetails.Package.Id));

        Assert.Null(await db.Repository.GetBuildManifestAsync(build.BuildId));

        await using var verifyConnection =
            new SqliteConnection($"Data Source={db.PathValue}");
        await verifyConnection.OpenAsync();

        await using var verifyCommand = verifyConnection.CreateCommand();
        verifyCommand.CommandText =
            """
            SELECT COUNT(*)
            FROM VeteransClaims_ReviewerPackageOutputBuildProvenance;
            """;

        Assert.Equal(
            0L,
            Convert.ToInt64(await verifyCommand.ExecuteScalarAsync()));
    }
    [Fact]
    public async Task RenderAsync_Pdf_TamperedExistingOutputFailsClosedBeforeConversion()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var service = CreateVerifiedService(
            converter,
            new Regulations(),
            db.Repository,
            CurrentBuildManifest());

        var first = await service.RenderAsync(
            details,
            VeteransReviewerPackageOutputFormat.Pdf,
            sourceReviewDate: ReviewDate);
        var tampered = first.Pdf!.ToArray();
        tampered[^1] ^= 0x01;

        var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
            service.RenderAsync(
                Details("later mutable state"),
                VeteransReviewerPackageOutputFormat.Pdf,
                sourceReviewDate: ReviewDate,
                existingOutput: new VeteransReviewerPackageExistingOutput(
                    null,
                    tampered)));

        Assert.Contains("does not match persisted provenance", error.Message, StringComparison.Ordinal);
        Assert.Equal(1, converter.Conversions);
    }

    [Fact]
    public async Task RenderAsync_Pdf_ChangedConverterIdentityRequiresNewPackageVersion()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());
        var firstConverter = new Converter(version: "1.2.3");
        var first = await CreateVerifiedService(
                firstConverter,
                new Regulations(),
                db.Repository,
                CurrentBuildManifest())
            .RenderAsync(
                details,
                VeteransReviewerPackageOutputFormat.Pdf,
                sourceReviewDate: ReviewDate);

        var secondConverter = new Converter(version: "2.0.0");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => CreateVerifiedService(
                secondConverter, new Regulations(), db.Repository, CurrentBuildManifest())
            .RenderAsync(Details("later mutable state"), VeteransReviewerPackageOutputFormat.Pdf,
                sourceReviewDate: ReviewDate,
                existingOutput: new VeteransReviewerPackageExistingOutput(null, first.Pdf)));
        Assert.Contains("new package version", error.Message);
        Assert.Equal(1, firstConverter.Conversions);
        Assert.Equal(0, secondConverter.Conversions);
        Assert.Single(await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id));
    }

    [Fact]
    public async Task RenderAsync_Pdf_ChangedReviewDateRequiresNewPackageVersion()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var service = CreateVerifiedService(
            converter,
            new Regulations(),
            db.Repository,
            CurrentBuildManifest());

        var first = await service.RenderAsync(
            details,
            VeteransReviewerPackageOutputFormat.Pdf,
            sourceReviewDate: ReviewDate);
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => service.RenderAsync(
            Details("later mutable state"),
            VeteransReviewerPackageOutputFormat.Pdf,
            sourceReviewDate: ReviewDate.AddDays(1),
            existingOutput: new VeteransReviewerPackageExistingOutput(null, first.Pdf)));
        Assert.Contains("new package version", error.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(1, converter.Conversions);
        Assert.Single(await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id));
    }

    [Fact]
    public async Task RenderAsync_PdfWithRepository_FailsClosedWithoutConverterIdentityAndDoesNotSeal()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            CreateVerifiedService(
                    new AnonymousConverter(),
                    new Regulations(),
                    db.Repository,
                    CurrentBuildManifest())
                .RenderAsync(
                    details,
                    VeteransReviewerPackageOutputFormat.Pdf,
                    sourceReviewDate: ReviewDate));

        Assert.Contains("converter identity metadata", error.Message, StringComparison.Ordinal);
        Assert.Null(await db.Repository.GetReviewerSnapshotAsync(details.PackageDetails.Package.Id));
        Assert.Empty(await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id));
    }

    [Fact]
    public async Task RenderAsync_PdfOnly_DoesNotRecordIntermediateDocx()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        await CreateVerifiedService(
                new Converter(),
                new Regulations(),
                db.Repository,
                CurrentBuildManifest())
            .RenderAsync(
                details,
                VeteransReviewerPackageOutputFormat.Pdf,
                sourceReviewDate: ReviewDate);

        var row = Assert.Single(
            await db.Repository.GetReviewerOutputProvenanceAsync(details.PackageDetails.Package.Id));
        Assert.Equal(ReviewerPackageOutputFormats.Pdf, row.Format);
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOfficeConverter_ReportsConcreteRuntimeIdentity()
    {
        var info =
            await new LibreOfficeVeteransReviewerPackageDocumentConverter()
                .GetDocumentConverterInfoAsync();

        Assert.Equal("LibreOffice", info.Identity);
        Assert.False(string.IsNullOrWhiteSpace(info.Version));
    }

    [Fact]
    public void RendererBuildIdentity_IsStableWithinTheRunningBuild()
    {
        var first = VeteransReviewerPackageRendererIdentity.Build;
        var second = VeteransReviewerPackageRendererIdentity.Build;

        Assert.Equal(first, second);
        Assert.StartsWith("mvid:", first);
        Assert.True(Guid.TryParse(first["mvid:".Length..], out _));
    }

    private sealed class Regulations : IVeteransReviewerRegulatoryTextProvider
    {
        public Task<IReadOnlyList<VeteransReviewerApplicableRegulation>> GetCurrentAsync(
            IReadOnlyList<string> citations,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(VeteransReviewerPackageSnapshotTests.Regulations());
    }

    private sealed class Converter :
        IVeteransReviewerPackageDocumentConverter,
        IVeteransReviewerPackageDocumentConverterInfoProvider
    {
        private readonly string _version;

        public Converter(string version = "1.2.3")
        {
            _version = version;
        }

        public int Conversions { get; private set; }

        public Task<byte[]> ConvertDocxToPdfAsync(
            ReadOnlyMemory<byte> docx,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Conversions++;
            return Task.FromResult(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
        }

        public Task<VeteransReviewerPackageDocumentConverterInfo> GetDocumentConverterInfoAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new VeteransReviewerPackageDocumentConverterInfo(
                "Synthetic PDF Converter",
                _version));
        }
    }

    private sealed class AnonymousConverter : IVeteransReviewerPackageDocumentConverter
    {
        public Task<byte[]> ConvertDocxToPdfAsync(
            ReadOnlyMemory<byte> docx,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\n%%EOF\n"));
    }

    private sealed class M91OnlyRepository : IEvidencePackageRepository
    {
        public bool SupportsReviewerOutputProvenance => true;

        public ReviewerPackageSnapshot? Snapshot { get; private set; }

        public List<ReviewerPackageOutputProvenance> OutputProvenance { get; } = [];

        public Task<ReviewerPackageSnapshot?> GetReviewerSnapshotAsync(
            EvidencePackageId packageId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (Snapshot is not null && Snapshot.PackageId != packageId)
                return Task.FromResult<ReviewerPackageSnapshot?>(null);

            return Task.FromResult(Snapshot);
        }

        public Task SaveReviewerSnapshotAsync(
            ReviewerPackageSnapshot snapshot,
            EvidencePackageDetails expectedMembership,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            snapshot.ValidateIntegrity();
            Snapshot = snapshot;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ReviewerPackageOutputProvenance>>
            GetReviewerOutputProvenanceAsync(
                EvidencePackageId packageId,
                CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            IReadOnlyList<ReviewerPackageOutputProvenance> rows =
                OutputProvenance
                    .Where(row => row.PackageId == packageId)
                    .ToArray();

            return Task.FromResult(rows);
        }

        public Task SaveReviewerOutputProvenanceAsync(
            ReviewerPackageOutputProvenance provenance,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            provenance.ValidateIntegrity();
            OutputProvenance.Add(provenance);
            return Task.CompletedTask;
        }

        public Task AddEvidencePackageAsync(
            EvidencePackage evidencePackage,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<EvidencePackage?> GetEvidencePackageAsync(
            EvidencePackageId evidencePackageId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<EvidencePackage?>(null);

        public Task<IReadOnlyList<EvidencePackage>> GetEvidencePackagesAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidencePackage>>([]);
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

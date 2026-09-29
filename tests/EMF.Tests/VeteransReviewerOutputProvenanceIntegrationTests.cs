using System.Security.Cryptography;
using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;
using static EMF.Tests.VeteransReviewerPackageSnapshotTests;

namespace EMF.Tests;

public sealed class VeteransReviewerOutputProvenanceIntegrationTests
{
    private static readonly DateOnly ReviewDate = new(2026, 9, 29);

    [Fact]
    public async Task RenderAsync_Docx_PersistsExactBytesBuildAndReviewDateAfterSealing()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());

        var output = await new VeteransReviewerPackageDocumentOutputService(
                regulatoryTextProvider: new Regulations(),
                snapshotRepository: db.Repository)
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
        var service = new VeteransReviewerPackageDocumentOutputService(
            converter,
            new Regulations(),
            db.Repository);

        var first = await service.RenderAsync(
            details,
            VeteransReviewerPackageOutputFormat.Both,
            sourceReviewDate: ReviewDate);
        var retry = await service.RenderAsync(
            Details("changed current state"),
            VeteransReviewerPackageOutputFormat.Both,
            sourceReviewDate: ReviewDate);

        // Exact-byte provenance must not assume DOCX container determinism.
        Assert.Equal(first.Pdf, retry.Pdf);
        Assert.Equal(2, converter.Conversions);

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


    [Fact]
    public async Task RenderAsync_Both_VerifiedExistingOutputsAreReusedWithoutPdfConversion()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var service = new VeteransReviewerPackageDocumentOutputService(
            converter,
            new Regulations(),
            db.Repository);

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
    public async Task RenderAsync_Pdf_TamperedExistingOutputFailsClosedBeforeConversion()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var service = new VeteransReviewerPackageDocumentOutputService(
            converter,
            new Regulations(),
            db.Repository);

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
    public async Task RenderAsync_Pdf_ChangedConverterIdentityDoesNotReuseExistingOutput()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());
        var firstConverter = new Converter(version: "1.2.3");
        var first = await new VeteransReviewerPackageDocumentOutputService(
                firstConverter,
                new Regulations(),
                db.Repository)
            .RenderAsync(
                details,
                VeteransReviewerPackageOutputFormat.Pdf,
                sourceReviewDate: ReviewDate);

        var secondConverter = new Converter(version: "2.0.0");
        var second = await new VeteransReviewerPackageDocumentOutputService(
                secondConverter,
                new Regulations(),
                db.Repository)
            .RenderAsync(
                Details("later mutable state"),
                VeteransReviewerPackageOutputFormat.Pdf,
                sourceReviewDate: ReviewDate,
                existingOutput: new VeteransReviewerPackageExistingOutput(
                    null,
                    first.Pdf));

        Assert.False(second.ReusedPdf);
        Assert.Equal(1, firstConverter.Conversions);
        Assert.Equal(1, secondConverter.Conversions);
        Assert.Equal(2, (await db.Repository.GetReviewerOutputProvenanceAsync(
            details.PackageDetails.Package.Id)).Count);
    }

    [Fact]
    public async Task RenderAsync_Pdf_ChangedReviewDateDoesNotReuseExistingOutput()
    {
        await using var db = await Database.Create();
        var details = Details();
        await db.Repository.AddEvidencePackageAsync(
            details.PackageDetails.Package,
            details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var service = new VeteransReviewerPackageDocumentOutputService(
            converter,
            new Regulations(),
            db.Repository);

        var first = await service.RenderAsync(
            details,
            VeteransReviewerPackageOutputFormat.Pdf,
            sourceReviewDate: ReviewDate);
        var second = await service.RenderAsync(
            Details("later mutable state"),
            VeteransReviewerPackageOutputFormat.Pdf,
            sourceReviewDate: ReviewDate.AddDays(1),
            existingOutput: new VeteransReviewerPackageExistingOutput(
                null,
                first.Pdf));

        Assert.False(second.ReusedPdf);
        Assert.Equal(2, converter.Conversions);
        Assert.Equal(2, (await db.Repository.GetReviewerOutputProvenanceAsync(
            details.PackageDetails.Package.Id)).Count);
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
            new VeteransReviewerPackageDocumentOutputService(
                    new AnonymousConverter(),
                    new Regulations(),
                    db.Repository)
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

        await new VeteransReviewerPackageDocumentOutputService(
                new Converter(),
                new Regulations(),
                db.Repository)
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

        private async Task Sql(string sql)
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

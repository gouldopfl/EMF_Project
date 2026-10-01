using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using EMF.Common;
using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;
using static EMF.Tests.VeteransReviewerPackageDeterminismTests;

namespace EMF.Tests;

// docs/REVIEWER_PACKAGE_DETERMINISM_CONTRACT.md: resolve once, freeze, print.
// These are synthetic semantic cases, never motivating claim/page-specific fixtures.
[Trait("Category", "ReviewerDeterminism")]
public sealed class VeteransReviewerPackageArchitectureTests
{
    private static readonly DateOnly Date = new(2026, 9, 29);
    private static ReviewerPackageSnapshot Source() =>
        Fixture("Synthetic joint condition", true, "Daily activity questionnaire", 3, true);
    private static ReviewerPackageCover Cover(ReviewerPackageSnapshot source, string type = "Secondary service connection")
    {
        var details = VeteransReviewerPackageSnapshot.Restore(source).Details;
        return new(details.VeteranDisplayName, type, "Typed synthetic condition", "Typed synthetic basis",
            details.PackagePreparedBy, details.PackageDetails.Package.ReviewerRole);
    }

    [Theory]
    [InlineData("Direct", "Primary service connection", "Synthetic joint condition")]
    [InlineData("Secondary", "Secondary service connection", "Synthetic respiratory condition")]
    [InlineData("Presumptive", "Presumptive service connection", "Synthetic digestive condition")]
    public async Task PreparationResolvesTypedScopeFromRecordsBeforeOpinionFormatting(
        string theory, string expectedType, string conditionName)
    {
        await using var db = await Database.Create();
        await db.Sql("UPDATE VeteransClaims_ServiceConnectionTheories SET TheoryType = '" + theory + "';");
        var conditions = new SqliteConditionRepository(db.PathValue);
        var connections = new SqliteServiceConnectionRepository(db.PathValue);
        await conditions.AddClaimedConditionAsync(new()
        {
            Id = new("synthetic-claimed-condition"), ClaimIssueId = new("issue"), Name = conditionName
        });
        await connections.AddBasisClaimedConditionAsync(new()
        {
            ServiceConnectionBasisId = new("synthetic-basis"), ClaimedConditionId = new("synthetic-claimed-condition")
        });
        await connections.AddBasisPrescribedMedicationAsync(new()
        {
            ServiceConnectionBasisId = new("synthetic-basis"), MedicationName = "Synthetic selected medication"
        });
        var service = new VeteransReviewerMedicalOpinionRequestService(connections, conditions,
            new SqliteRegulatoryRepository(db.PathValue));
        var package = VeteransReviewerPackageSnapshot.Restore(Source()).Details.PackageDetails.Package;
        var scope = await service.GetCoverScopeAsync(package);
        Assert.Equal(expectedType, scope.ClaimType);
        Assert.Equal(conditionName, scope.Condition);
        Assert.Equal("Synthetic selected medication", scope.Basis);
        var noSelectedTheory = new EvidencePackage
        {
            Id = package.Id, ClaimIssueId = package.ClaimIssueId, Purpose = package.Purpose,
            ReviewerRole = package.ReviewerRole
        };
        var unknown = await service.GetCoverScopeAsync(noSelectedTheory);
        Assert.Null(unknown.ClaimType);
        Assert.Equal(conditionName, unknown.Condition);
        Assert.Null(unknown.Basis);
    }

    [Fact]
    public void CompilationRejectsImplicitOrDefaultVisibleDates()
    {
        var details = VeteransReviewerPackageSnapshot.Restore(Source()).Details;
        Assert.Throws<ArgumentException>(() => VeteransReviewerPackageDocxRenderer.Render(details));
        Assert.Throws<ArgumentException>(() => VeteransReviewerPackageDocxRenderer.Render(details, sourceReviewDate: default(DateOnly)));
    }

    [Theory]
    [InlineData("Primary service connection")]
    [InlineData("Secondary service connection")]
    [InlineData("Presumptive service connection")]
    public void TypedScopeIsIndependentOfOpinionWordingAndCanonicalPreparationRepeats(string type)
    {
        var source = Source();
        var first = VeteransReviewerPackagePresentationPreparation.Prepare(source, new(Date), Cover(source, type));
        var second = VeteransReviewerPackagePresentationPreparation.Prepare(source, new(Date), Cover(source, type));
        Assert.Equal(first, second);
        Assert.Equal(first.MaterializeDocx(), second.MaterializeDocx());
        using var doc = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(new MemoryStream(first.MaterializeDocx()), false);
        var text = doc.MainDocumentPart!.Document!.InnerText;
        Assert.Contains("Claim type: " + type, text);
        Assert.Contains("Claimed condition: Typed synthetic condition", text);
        Assert.Contains("Basis: Typed synthetic basis", text);
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc));
    }

    [Fact]
    public void PurePrintingIgnoresAmbientCultureAndReturnsIndependentBytes()
    {
        var source = Source();
        var plan = VeteransReviewerPackagePresentationPreparation.Prepare(source, new(Date), Cover(source));
        var before = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("th-TH");
            var bytes = VeteransReviewerPackageDocxRenderer.RenderPrepared(plan);
            Assert.Equal(plan.MaterializeDocx(), bytes);
            bytes[^1] ^= 1;
            Assert.NotEqual(bytes, VeteransReviewerPackageDocxRenderer.RenderPrepared(plan));
        }
        finally { CultureInfo.CurrentCulture = before; }
        var restored = JsonSerializer.Deserialize<ReviewerPackagePresentationSnapshot>(JsonSerializer.Serialize(plan))!;
        Assert.Equal(plan, restored);
        restored.ValidateIntegrity();
    }

    [Theory]
    [InlineData("cover")]
    [InlineData("date")]
    [InlineData("bytes")]
    [InlineData("source")]
    public void TamperedPresentationFailsBeforePrinting(string field)
    {
        var source = Source();
        var plan = VeteransReviewerPackagePresentationPreparation.Prepare(source, new(Date), Cover(source));
        var bad = field switch
        {
            "cover" => plan with { Cover = plan.Cover with { ClaimedCondition = "Changed" } },
            "date" => plan with { PackagePreparedDate = Date.AddDays(1) },
            "bytes" => plan with { DocxBase64 = Convert.ToBase64String(new byte[] { 80, 75, 1, 2 }) },
            _ => plan with { SourceSnapshotSha256 = new string('A', 64) }
        };
        Assert.Throws<InvalidDataException>(() => VeteransReviewerPackageDocxRenderer.RenderPrepared(bad));
    }

    [Fact]
    public async Task NewPathsUseFrozenDocxAndPdfWithoutReinterpretingOrReConverting()
    {
        await using var db = await Database.Create();
        var source = Source();
        var details = VeteransReviewerPackageSnapshot.Restore(source).Details;
        await db.Packages.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        var converter = new Converter();
        var output = Service(db, converter);
        var first = await output.RenderAsync(details, VeteransReviewerPackageOutputFormat.Both,
            preparedSnapshot: source, sourceReviewDate: Date, preparedCover: Cover(source));
        var plan = await db.Packages.GetReviewerPresentationAsync(source.PackageId);
        var pdf = await db.Packages.GetReviewerFrozenPdfAsync(source.PackageId);
        Assert.NotNull(plan);
        Assert.NotNull(pdf);
        Assert.Equal(plan.Sha256, pdf.PresentationSha256);
        var second = await output.RenderAsync(details, VeteransReviewerPackageOutputFormat.Both);
        Assert.Equal(first.Docx, second.Docx);
        Assert.Equal(first.Pdf, second.Pdf);
        Assert.Equal(1, converter.Conversions);
        Assert.Equal(2, (await db.Packages.GetReviewerOutputProvenanceAsync(source.PackageId)).Count);
        await Assert.ThrowsAsync<InvalidDataException>(() => output.RenderAsync(details,
            VeteransReviewerPackageOutputFormat.Docx, preparedCover: Cover(source) with { Basis = "Changed" }));
    }

    [Theory]
    [InlineData("VeteransClaims_ReviewerPresentations", "UPDATE")]
    [InlineData("VeteransClaims_ReviewerPresentations", "DELETE")]
    [InlineData("VeteransClaims_ReviewerPresentations", "REPLACE")]
    [InlineData("VeteransClaims_ReviewerFrozenPdfs", "UPDATE")]
    [InlineData("VeteransClaims_ReviewerFrozenPdfs", "DELETE")]
    [InlineData("VeteransClaims_ReviewerFrozenPdfs", "REPLACE")]
    public async Task SqlCannotRewriteFrozenPresentations(string table, string operation)
    {
        await using var db = await Database.Create();
        var source = Source();
        var details = VeteransReviewerPackageSnapshot.Restore(source).Details;
        await db.Packages.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        await Service(db, new Converter()).RenderAsync(details, VeteransReviewerPackageOutputFormat.Both,
            preparedSnapshot: source, sourceReviewDate: Date);
        var sql = operation switch
        {
            "UPDATE" => $"UPDATE {table} SET Sha256 = Sha256;",
            "DELETE" => $"DELETE FROM {table};",
            _ => $"INSERT OR REPLACE INTO {table} SELECT * FROM {table};"
        };
        await Assert.ThrowsAsync<SqliteException>(() => db.Sql(sql));
        Assert.NotNull(await db.Packages.GetReviewerPresentationAsync(source.PackageId));
        Assert.NotNull(await db.Packages.GetReviewerFrozenPdfAsync(source.PackageId));
    }

    // M94 must reject structural attacks even outside the repository with FK checks off.
    // Shared package contract: docs/REVIEWER_PACKAGE_DETERMINISM_CONTRACT.md.
    [Theory]
    [InlineData("self", false)]
    [InlineData("self", true)]
    [InlineData("missing-prior", false)]
    [InlineData("missing-prior", true)]
    [InlineData("null-date", false)]
    [InlineData("invalid-date", false)]
    [InlineData("missing-date", false)]
    [InlineData("invalid-json", false)]
    [InlineData("missing-docx", false)]
    [InlineData("orphan", false)]
    [InlineData("nonhex", false)]
    [InlineData("bool-version", false)]
    [InlineData("real-version", false)]
    [InlineData("object-docx", false)]
    [InlineData("numeric-role", false)]
    [InlineData("numeric-profile", false)]
    [InlineData("duplicate-version", false)]
    [InlineData("duplicate-cover-role", false)]
    public async Task SqlRejectsInvalidFrozenPresentation(string attack, bool foreignKeys)
    {
        await using var db = await Database.Create();
        var source = Source();
        var details = VeteransReviewerPackageSnapshot.Restore(source).Details;
        await db.Packages.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        await db.Packages.SaveReviewerSnapshotAsync(source, details.PackageDetails);
        var plan = VeteransReviewerPackagePresentationPreparation.Prepare(source, new(Date));
        var node = JsonNode.Parse(JsonSerializer.Serialize(plan))!;
        object date = Date.ToString("yyyy-MM-dd");
        var id = plan.PackageId.Value;
        string? previous = null;
        if (attack == "self") previous = id;
        if (attack == "missing-prior") previous = "missing";
        node["PreviousPackageId"] = previous;
        if (attack == "null-date") { date = DBNull.Value; node["PackagePreparedDate"] = null; }
        if (attack == "invalid-date") { date = "2026-02-30"; node["PackagePreparedDate"] = "2026-02-30"; }
        if (attack == "missing-date") node.AsObject().Remove("PackagePreparedDate");
        if (attack == "missing-docx") node.AsObject().Remove("DocxBase64");
        if (attack == "bool-version") node["Version"] = true;
        if (attack == "real-version") node["Version"] = JsonNode.Parse("1.0");
        if (attack == "object-docx") node["DocxBase64"] = new JsonObject();
        if (attack == "numeric-role") node["Cover"]!["ReviewerRole"] = 7;
        if (attack == "numeric-profile") node["RenderProfile"] = 7;
        if (attack == "orphan") { id = "missing"; node["PackageId"] = id; }
        var hash = attack == "nonhex" ? new string('Z', 64) : plan.Sha256;
        node["Sha256"] = hash;
        await using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
            { DataSource = db.PathValue, ForeignKeys = foreignKeys }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = "INSERT INTO VeteransClaims_ReviewerPresentations VALUES ($id,$source,$date,$payload,$hash,$docx,$previous);";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$source", source.Sha256);
        command.Parameters.AddWithValue("$date", date);
        var payload = attack == "invalid-json" ? "x" : node.ToJsonString();
        if (attack == "duplicate-version") payload = payload[..^1] + ",\"Version\":2}";
        if (attack == "duplicate-cover-role") payload = payload.Replace("\"ReviewerRole\":", "\"ReviewerRole\":\"duplicate\",\"ReviewerRole\":", StringComparison.Ordinal);
        command.Parameters.AddWithValue("$payload", payload);
        command.Parameters.AddWithValue("$hash", hash);
        command.Parameters.AddWithValue("$docx", plan.DocxSha256);
        command.Parameters.AddWithValue("$previous", (object?)previous ?? DBNull.Value);
        await Assert.ThrowsAsync<SqliteException>(() => command.ExecuteNonQueryAsync());
        Assert.Null(await db.Packages.GetReviewerPresentationAsync(plan.PackageId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RepositoryRejectsCompetingDatesAndWrongSource(bool wrongSource)
    {
        await using var db = await Database.Create();
        var source = Source();
        var details = VeteransReviewerPackageSnapshot.Restore(source).Details;
        await db.Packages.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        await db.Packages.SaveReviewerSnapshotAsync(source, details.PackageDetails);
        var plan = VeteransReviewerPackagePresentationPreparation.Prepare(source, new(Date));
        await db.Packages.SaveReviewerPresentationAsync(plan);
        await db.Packages.SaveReviewerPresentationAsync(plan); // exact retry
        var alternativeSource = source;
        if (wrongSource)
        {
            var node = JsonNode.Parse(source.Payload)!;
            node["Details"]!["PackagePreparedBy"] = "Changed synthetic preparer";
            using var json = JsonDocument.Parse(node.ToJsonString());
            var payload = VeteransReviewerPackageSnapshot.Canonical(json.RootElement);
            alternativeSource = source with { Payload = payload, Sha256 = ReviewerPackageSnapshot.ComputeHash(payload) };
        }
        var competing = VeteransReviewerPackagePresentationPreparation.Prepare(alternativeSource, new(Date.AddDays(1)));
        await Assert.ThrowsAsync<InvalidDataException>(() => db.Packages.SaveReviewerPresentationAsync(competing));
        Assert.Equal(plan, await db.Packages.GetReviewerPresentationAsync(source.PackageId));
    }

    [Fact]
    public async Task DateRefreshHasNewIdentityAndPreservesOriginalPackage()
    {
        await using var db = await Database.Create();
        var source = Source();
        var details = VeteransReviewerPackageSnapshot.Restore(source).Details;
        await db.Packages.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        await Service(db).RenderAsync(details, VeteransReviewerPackageOutputFormat.Docx,
            preparedSnapshot: source, sourceReviewDate: Date, preparedCover: Cover(source));
        var original = await db.Packages.GetReviewerPresentationAsync(source.PackageId);
        var next = WithPackageId(source, new("new-package-version"));
        var preparation = new VeteransReviewerPresentationVersionService(db.Packages, Verify);
        var prepared = await preparation.PrepareNewVersionAsync(source.PackageId, next, new(Date.AddDays(1)), Cover(next));
        Assert.Equal(source.PackageId.Value, prepared.PreviousPackageId);
        Assert.Equal(Date.AddDays(1), prepared.PackagePreparedDate);
        Assert.Equal(original, await preparation.PreserveAsync(source.PackageId));
        Assert.Equal(source, await db.Packages.GetReviewerSnapshotAsync(source.PackageId));
        Assert.Equal(next, await db.Packages.GetReviewerSnapshotAsync(next.PackageId));
        await Assert.ThrowsAsync<InvalidDataException>(() => preparation.PrepareNewVersionAsync(
            source.PackageId, source, new(Date.AddDays(1)), Cover(source)));
        var nextOutput = await Service(db).RenderAsync(VeteransReviewerPackageSnapshot.Restore(next).Details,
            VeteransReviewerPackageOutputFormat.Docx);
        Assert.Equal(prepared.MaterializeDocx(), nextOutput.Docx);
        Assert.Equal(Date.AddDays(1), Assert.Single(await db.Packages.GetReviewerOutputProvenanceAsync(next.PackageId)).SourceReviewDate);
    }

    [Theory]
    [InlineData("VeteransClaims_EvidencePackages", "BEFORE")]
    [InlineData("VeteransClaims_EvidencePackages", "AFTER")]
    [InlineData("VeteransClaims_EvidencePackageArtifacts", "BEFORE")]
    [InlineData("VeteransClaims_EvidencePackageArtifacts", "AFTER")]
    [InlineData("VeteransClaims_ReviewerPackageSnapshots", "BEFORE")]
    [InlineData("VeteransClaims_ReviewerPackageSnapshots", "AFTER")]
    [InlineData("VeteransClaims_ReviewerPresentations", "BEFORE")]
    [InlineData("VeteransClaims_ReviewerPresentations", "AFTER")]
    public async Task InterruptedVersionCreationRollsBackEveryRowAndSameIdentityCanRetry(string table, string timing)
    {
        await using var db = await Database.Create();
        var source = Source();
        var details = VeteransReviewerPackageSnapshot.Restore(source).Details;
        await db.Packages.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        await Service(db).RenderAsync(details, VeteransReviewerPackageOutputFormat.Docx,
            preparedSnapshot: source, sourceReviewDate: Date, preparedCover: Cover(source));
        var original = await db.Packages.GetReviewerPresentationAsync(source.PackageId);
        var originalMembership = await db.Packages.GetEvidencePackageArtifactsAsync(source.PackageId);
        var originalOutputs = await db.Packages.GetReviewerOutputProvenanceAsync(source.PackageId);
        var originalPackage = await db.Packages.GetEvidencePackageAsync(source.PackageId);
        var next = WithPackageId(source, new("interrupted-package-version"));
        var preparation = new VeteransReviewerPresentationVersionService(db.Packages, Verify);
        await db.Sql($"CREATE TRIGGER InjectVersionFailure {timing} INSERT ON {table} BEGIN SELECT RAISE(ABORT, 'injected version failure'); END;");

        var failure = await Assert.ThrowsAsync<SqliteException>(() => preparation.PrepareNewVersionAsync(
            source.PackageId, next, new(Date.AddDays(1)), Cover(next)));
        Assert.Contains("injected version failure", failure.Message);
        Assert.Null(await db.Packages.GetEvidencePackageAsync(next.PackageId));
        Assert.Empty(await db.Packages.GetEvidencePackageArtifactsAsync(next.PackageId));
        Assert.Null(await db.Packages.GetReviewerPresentationAsync(next.PackageId));
        Assert.Equal(0L, await db.CountVersionRows(next.PackageId));
        Assert.Equal(original, await preparation.PreserveAsync(source.PackageId));
        Assert.Equal(source, await db.Packages.GetReviewerSnapshotAsync(source.PackageId));

        Assert.Equal(JsonSerializer.Serialize(originalPackage),
            JsonSerializer.Serialize(await db.Packages.GetEvidencePackageAsync(source.PackageId)));
        Assert.Equal(JsonSerializer.Serialize(originalMembership),
            JsonSerializer.Serialize(await db.Packages.GetEvidencePackageArtifactsAsync(source.PackageId)));
        Assert.Equal(JsonSerializer.Serialize(originalOutputs),
            JsonSerializer.Serialize(await db.Packages.GetReviewerOutputProvenanceAsync(source.PackageId)));

        await db.Sql("DROP TRIGGER InjectVersionFailure;");
        var prepared = await preparation.PrepareNewVersionAsync(source.PackageId, next,
            new(Date.AddDays(1)), Cover(next));
        Assert.Equal(next, await db.Packages.GetReviewerSnapshotAsync(next.PackageId));
        Assert.Equal(prepared, await preparation.PreserveAsync(next.PackageId));
        Assert.Equal(source.PackageId.Value, prepared.PreviousPackageId);
        Assert.Equal(3L + details.PackageDetails.Artifacts.Count, await db.CountVersionRows(next.PackageId));
        await Assert.ThrowsAsync<InvalidDataException>(() => preparation.PrepareNewVersionAsync(
            source.PackageId, next, new(Date.AddDays(1)), Cover(next)));
        Assert.Equal(prepared, await preparation.PreserveAsync(next.PackageId));
        Assert.Equal(3L + details.PackageDetails.Artifacts.Count, await db.CountVersionRows(next.PackageId));
        Assert.Equal(original, await preparation.PreserveAsync(source.PackageId));
        Assert.Equal(JsonSerializer.Serialize(originalPackage),
            JsonSerializer.Serialize(await db.Packages.GetEvidencePackageAsync(source.PackageId)));
        Assert.Equal(JsonSerializer.Serialize(originalMembership),
            JsonSerializer.Serialize(await db.Packages.GetEvidencePackageArtifactsAsync(source.PackageId)));
        Assert.Equal(JsonSerializer.Serialize(originalOutputs),
            JsonSerializer.Serialize(await db.Packages.GetReviewerOutputProvenanceAsync(source.PackageId)));
    }

    [Theory]
    [InlineData(false, "VeteransClaims_ReviewerPackageSnapshots", "BEFORE")]
    [InlineData(false, "VeteransClaims_ReviewerPackageSnapshots", "AFTER")]
    [InlineData(false, "VeteransClaims_ReviewerPresentations", "BEFORE")]
    [InlineData(false, "VeteransClaims_ReviewerPresentations", "AFTER")]
    [InlineData(true, "VeteransClaims_ReviewerPresentations", "BEFORE")]
    [InlineData(true, "VeteransClaims_ReviewerPresentations", "AFTER")]
    public async Task RecoveryRollsBackToOriginalIncompleteStateAndRetriesWithoutDuplicates(
        bool sourceSaved, string table, string timing)
    {
        var (database, prior, intended) = await IncompleteVersion(sourceSaved);
        await using var db = database;
        var preparation = new VeteransReviewerPresentationVersionService(db.Packages, Verify);
        var original = await preparation.PreserveAsync(prior.PackageId);
        var priorOutputs = JsonSerializer.Serialize(await db.Packages.GetReviewerOutputProvenanceAsync(prior.PackageId));
        var membership = JsonSerializer.Serialize(await db.Packages.GetEvidencePackageArtifactsAsync(intended.PackageId));
        var package = JsonSerializer.Serialize(await db.Packages.GetEvidencePackageAsync(intended.PackageId));
        var beforeCount = await db.CountVersionRows(intended.PackageId);
        await db.Sql($"CREATE TRIGGER InjectRecoveryFailure {timing} INSERT ON {table} BEGIN SELECT RAISE(ABORT, 'injected recovery failure'); END;");

        var failure = await Assert.ThrowsAsync<SqliteException>(() => preparation.RecoverIncompleteVersionAsync(
            prior.PackageId, intended, new(Date.AddDays(1)), Cover(intended)));
        Assert.Contains("injected recovery failure", failure.Message);
        Assert.Equal(beforeCount, await db.CountVersionRows(intended.PackageId));
        Assert.Equal(sourceSaved ? intended : null, await db.Packages.GetReviewerSnapshotAsync(intended.PackageId));
        Assert.Null(await db.Packages.GetReviewerPresentationAsync(intended.PackageId));
        Assert.Equal(package, JsonSerializer.Serialize(await db.Packages.GetEvidencePackageAsync(intended.PackageId)));
        Assert.Equal(membership, JsonSerializer.Serialize(await db.Packages.GetEvidencePackageArtifactsAsync(intended.PackageId)));

        await db.Sql("DROP TRIGGER InjectRecoveryFailure;");
        var recovered = await preparation.RecoverIncompleteVersionAsync(prior.PackageId, intended,
            new(Date.AddDays(1)), Cover(intended));
        Assert.Equal(intended, await db.Packages.GetReviewerSnapshotAsync(intended.PackageId));
        Assert.Equal(recovered, await preparation.PreserveAsync(intended.PackageId));
        Assert.Equal(prior.PackageId.Value, recovered.PreviousPackageId);
        Assert.Equal(beforeCount + (sourceSaved ? 1 : 2), await db.CountVersionRows(intended.PackageId));
        await Assert.ThrowsAsync<InvalidDataException>(() => preparation.RecoverIncompleteVersionAsync(
            prior.PackageId, intended, new(Date.AddDays(2)), Cover(intended)));
        Assert.Equal(recovered, await preparation.PreserveAsync(intended.PackageId));
        Assert.Equal(beforeCount + (sourceSaved ? 1 : 2), await db.CountVersionRows(intended.PackageId));
        Assert.Equal(package, JsonSerializer.Serialize(await db.Packages.GetEvidencePackageAsync(intended.PackageId)));
        Assert.Equal(membership, JsonSerializer.Serialize(await db.Packages.GetEvidencePackageArtifactsAsync(intended.PackageId)));
        Assert.Equal(original, await preparation.PreserveAsync(prior.PackageId));
        Assert.Equal(prior, await db.Packages.GetReviewerSnapshotAsync(prior.PackageId));
        Assert.Equal(priorOutputs, JsonSerializer.Serialize(await db.Packages.GetReviewerOutputProvenanceAsync(prior.PackageId)));
    }

    [Theory]
    [InlineData("source")]
    [InlineData("membership")]
    [InlineData("package")]
    [InlineData("claim")]
    [InlineData("self")]
    [InlineData("missing-prior")]
    public async Task RecoveryRejectsChangedIntendedInputsAndInvalidLineage(string mismatch)
    {
        var (database, prior, intended) = await IncompleteVersion(true);
        await using var db = database;
        var beforeCount = await db.CountVersionRows(intended.PackageId);
        var candidate = VeteransReviewerPackageOutputReuseTests.Change(intended, root =>
        {
            if (mismatch == "source") root["Details"]!["PackagePreparedBy"] = "Changed preparer";
            if (mismatch == "membership") root["Details"]!["PackageDetails"]!["Artifacts"]![0]!["ContentRole"] = "Changed role";
            if (mismatch == "package") root["Details"]!["PackageDetails"]!["Package"]!["Purpose"] = "Changed purpose";
            if (mismatch == "claim") root["Details"]!["PackageDetails"]!["Package"]!["ClaimIssueId"] = "other-issue";
        });
        var previousId = mismatch == "self" ? intended.PackageId :
            mismatch == "missing-prior" ? new EvidencePackageId("missing-prior") : prior.PackageId;
        var preparation = new VeteransReviewerPresentationVersionService(db.Packages, Verify);
        await Assert.ThrowsAsync<InvalidDataException>(() => preparation.RecoverIncompleteVersionAsync(
            previousId, candidate, new(Date.AddDays(1)), Cover(candidate)));
        Assert.Equal(beforeCount, await db.CountVersionRows(intended.PackageId));
        Assert.Equal(intended, await db.Packages.GetReviewerSnapshotAsync(intended.PackageId));
        Assert.Null(await db.Packages.GetReviewerPresentationAsync(intended.PackageId));
        Assert.Equal(prior, await db.Packages.GetReviewerSnapshotAsync(prior.PackageId));
    }

    [Fact]
    public async Task RecoveryDoesNotFabricatePresentationForHistoricalOutput()
    {
        var (database, prior, intended) = await IncompleteVersion(true);
        await using var db = database;
        var bytes = VeteransReviewerPackageDocxRenderer.RenderSnapshot(intended, new(Date));
        var output = ReviewerPackageOutputProvenance.Create(intended.PackageId, "docx", intended.Sha256,
            VeteransReviewerPackageRendererIdentity.Contract, VeteransReviewerPackageRendererIdentity.Build,
            null, null, Date, bytes, DateTimeOffset.UtcNow);
        await db.Packages.SaveReviewerOutputProvenanceAsync(output);
        var preparation = new VeteransReviewerPresentationVersionService(db.Packages, Verify);
        await Assert.ThrowsAsync<InvalidDataException>(() => preparation.RecoverIncompleteVersionAsync(
            prior.PackageId, intended, new(Date), Cover(intended)));
        Assert.Null(await db.Packages.GetReviewerPresentationAsync(intended.PackageId));
        Assert.Equal(output, Assert.Single(await db.Packages.GetReviewerOutputProvenanceAsync(intended.PackageId)));
        Assert.Equal(intended, await db.Packages.GetReviewerSnapshotAsync(intended.PackageId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryCancellationLeavesIncompleteIdentityUnchanged(bool sourceSaved)
    {
        var (database, prior, intended) = await IncompleteVersion(sourceSaved);
        await using var db = database;
        var count = await db.CountVersionRows(intended.PackageId);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var preparation = new VeteransReviewerPresentationVersionService(db.Packages, Verify);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => preparation.RecoverIncompleteVersionAsync(
            prior.PackageId, intended, new(Date.AddDays(1)), Cover(intended), cancellation.Token));
        Assert.Equal(count, await db.CountVersionRows(intended.PackageId));
        Assert.Null(await db.Packages.GetReviewerPresentationAsync(intended.PackageId));
        Assert.Equal(sourceSaved ? intended : null, await db.Packages.GetReviewerSnapshotAsync(intended.PackageId));
    }

    [Fact]
    public async Task RecoveryRequiresExistingIdentityAndVerifiedDeployment()
    {
        var (database, prior, intended) = await IncompleteVersion(false);
        await using var db = database;
        var preparation = new VeteransReviewerPresentationVersionService(db.Packages, Verify);
        var missing = WithPackageId(intended, new("missing-version"));
        await Assert.ThrowsAsync<InvalidDataException>(() => preparation.RecoverIncompleteVersionAsync(
            prior.PackageId, missing, new(Date.AddDays(1)), Cover(missing)));
        var unverified = new VeteransReviewerPresentationVersionService(db.Packages);
        await Assert.ThrowsAsync<InvalidOperationException>(() => unverified.RecoverIncompleteVersionAsync(
            prior.PackageId, intended, new(Date.AddDays(1)), Cover(intended)));
        Assert.Null(await db.Packages.GetEvidencePackageAsync(missing.PackageId));
        Assert.Null(await db.Packages.GetReviewerSnapshotAsync(intended.PackageId));
        Assert.Null(await db.Packages.GetReviewerPresentationAsync(intended.PackageId));
    }

    [Fact]
    public async Task RecoveryRejectsLegacyIdentityWithoutFabricatingSourceOrPresentation()
    {
        var (database, prior, intended) = await IncompleteVersion(false);
        await using var db = database;
        var legacy = WithPackageId(intended, new("legacy-recovery-version"));
        await db.Sql("""
            INSERT INTO VeteransClaims_EvidencePackages
                (Id, ClaimIssueId, Purpose, ReviewerRole, ServiceConnectionBasisId, ReviewerSnapshotVersion, CreationOrdinal)
            SELECT 'legacy-recovery-version', ClaimIssueId, Purpose, ReviewerRole, ServiceConnectionBasisId, 0,
                (SELECT MAX(CreationOrdinal) + 1 FROM VeteransClaims_EvidencePackages)
            FROM VeteransClaims_EvidencePackages WHERE Id = 'recoverable-package-version';
            """);
        var preparation = new VeteransReviewerPresentationVersionService(db.Packages, Verify);
        await Assert.ThrowsAsync<InvalidDataException>(() => preparation.RecoverIncompleteVersionAsync(
            prior.PackageId, legacy, new(Date.AddDays(1)), Cover(legacy)));
        Assert.True((await db.Packages.ReadReviewerSnapshotAsync(legacy.PackageId)).IsLegacy);
        Assert.Equal(1L, await db.CountVersionRows(legacy.PackageId));
        Assert.Null(await db.Packages.GetReviewerPresentationAsync(legacy.PackageId));
    }

    private static async Task<(Database Db, ReviewerPackageSnapshot Prior, ReviewerPackageSnapshot Intended)>
        IncompleteVersion(bool sourceSaved)
    {
        var db = await Database.Create();
        var prior = Source();
        var priorDetails = VeteransReviewerPackageSnapshot.Restore(prior).Details;
        await db.Packages.AddEvidencePackageAsync(priorDetails.PackageDetails.Package, priorDetails.PackageDetails.Artifacts.ToArray());
        await Service(db).RenderAsync(priorDetails, VeteransReviewerPackageOutputFormat.Docx,
            preparedSnapshot: prior, sourceReviewDate: Date, preparedCover: Cover(prior));
        var intended = WithPackageId(prior, new("recoverable-package-version"));
        var details = VeteransReviewerPackageSnapshot.Restore(intended).Details;
        await db.Packages.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        if (sourceSaved) await db.Packages.SaveReviewerSnapshotAsync(intended, details.PackageDetails);
        return (db, prior, intended);
    }

    [Fact]
    public async Task HistoricalSnapshotWithoutPreservedDateRequiresExplicitPreparation()
    {
        await using var db = await Database.Create();
        var source = Source();
        var details = VeteransReviewerPackageSnapshot.Restore(source).Details;
        await db.Packages.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        await db.Packages.SaveReviewerSnapshotAsync(source, details.PackageDetails);
        await Assert.ThrowsAsync<InvalidDataException>(() => Service(db).RenderAsync(details, VeteransReviewerPackageOutputFormat.Docx));
        Assert.Null(await db.Packages.GetReviewerPresentationAsync(source.PackageId));
        Assert.Empty(await db.Packages.GetReviewerOutputProvenanceAsync(source.PackageId));
    }

    [Fact]
    public async Task ProvenanceCannotDescribeBytesThatDifferFromFrozenPlan()
    {
        await using var db = await Database.Create();
        var source = Source();
        var details = VeteransReviewerPackageSnapshot.Restore(source).Details;
        await db.Packages.AddEvidencePackageAsync(details.PackageDetails.Package, details.PackageDetails.Artifacts.ToArray());
        await Service(db).RenderAsync(details, VeteransReviewerPackageOutputFormat.Docx,
            preparedSnapshot: source, sourceReviewDate: Date);
        var bytes = (await db.Packages.GetReviewerPresentationAsync(source.PackageId))!.MaterializeDocx();
        bytes[^1] ^= 1;
        var row = ReviewerPackageOutputProvenance.Create(source.PackageId, "docx", source.Sha256,
            VeteransReviewerPackageRendererIdentity.Contract, VeteransReviewerPackageRendererIdentity.Build,
            null, null, Date, bytes, DateTimeOffset.UtcNow);
        await Assert.ThrowsAsync<SqliteException>(() => db.Packages.SaveReviewerOutputProvenanceAsync(row));
        Assert.Single(await db.Packages.GetReviewerOutputProvenanceAsync(source.PackageId));
    }

    private static ReviewerPackageSnapshot WithPackageId(ReviewerPackageSnapshot source, EvidencePackageId id)
    {
        var node = JsonNode.Parse(source.Payload)!;
        node["Details"]!["PackageDetails"]!["Package"]!["Id"] = id.Value;
        foreach (var member in node["Details"]!["PackageDetails"]!["Artifacts"]!.AsArray()) member!["EvidencePackageId"] = id.Value;
        using var json = JsonDocument.Parse(node.ToJsonString());
        var payload = VeteransReviewerPackageSnapshot.Canonical(json.RootElement);
        return new(id, 1, payload, ReviewerPackageSnapshot.ComputeHash(payload));
    }
    private static EmfVerifiedFirstPartyDeploymentIdentity Verify()
    {
        var root = typeof(ConsoleCommandRouter).Assembly;
        return EmfBuildManifestIdentity.VerifyFirstPartyDeployment(
            EmfBuildManifestIdentity.CaptureFirstPartyClosure(root), AppContext.BaseDirectory, root);
    }
    private static VeteransReviewerPackageDocumentOutputService Service(Database db, Converter? converter = null) =>
        VeteransReviewerPackageDocumentOutputService.CreateForVerifiedDeployment(Verify, converter,
            snapshotRepository: db.Packages);
    private sealed class Converter : IVeteransReviewerPackageDocumentConverter, IVeteransReviewerPackageDocumentConverterInfoProvider
    {
        public int Conversions { get; private set; }
        public Task<byte[]> ConvertDocxToPdfAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
        { Conversions++; return Task.FromResult("%PDF-1.7 Synthetic frozen PDF"u8.ToArray()); }
        public Task<VeteransReviewerPackageDocumentConverterInfo> GetDocumentConverterInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new VeteransReviewerPackageDocumentConverterInfo("Synthetic controlled converter", "profile-1"));
    }
    private sealed class Database(string path) : IAsyncDisposable
    {
        public SqliteEvidencePackageRepository Packages { get; } = new(path);
        public static async Task<Database> Create()
        {
            var db = new Database(Path.GetTempFileName());
            await new VeteransClaimsSqliteSchema(db.PathValue).InitializeAsync();
            await db.Sql("INSERT INTO VeteransClaims_Veterans VALUES ('veteran'); INSERT INTO VeteransClaims_Claims VALUES ('claim','veteran'); INSERT INTO VeteransClaims_ClaimIssues VALUES ('issue','claim','ServiceConnection'); INSERT INTO VeteransClaims_ServiceConnectionTheories (Id, ClaimIssueId, TheoryType) VALUES ('synthetic-theory','issue','Secondary'); INSERT INTO VeteransClaims_ServiceConnectionBases (Id, ClaimIssueId, ServiceConnectionTheoryId) VALUES ('synthetic-basis','issue','synthetic-theory');");
            return db;
        }
        public string PathValue => path;
        public async Task Sql(string sql)
        {
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand(); command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        public async Task<long> CountVersionRows(EvidencePackageId id)
        {
            await using var connection = new SqliteConnection($"Data Source={path}");
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT (SELECT COUNT(*) FROM VeteransClaims_EvidencePackages WHERE Id = $id)
                     + (SELECT COUNT(*) FROM VeteransClaims_EvidencePackageArtifacts WHERE EvidencePackageId = $id)
                     + (SELECT COUNT(*) FROM VeteransClaims_ReviewerPackageSnapshots WHERE EvidencePackageId = $id)
                     + (SELECT COUNT(*) FROM VeteransClaims_ReviewerPresentations WHERE EvidencePackageId = $id);
                """;
            command.Parameters.AddWithValue("$id", id.Value);
            return (long)(await command.ExecuteScalarAsync())!;
        }
        public ValueTask DisposeAsync()
        { SqliteConnection.ClearAllPools(); File.Delete(path); return ValueTask.CompletedTask; }
    }
}

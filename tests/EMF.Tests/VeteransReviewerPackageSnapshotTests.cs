using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

[Collection(ReviewerDeploymentEnvironmentCollection.Name)]
public sealed class VeteransReviewerPackageSnapshotTests
{
    [Fact]
    public void V1_RoundTripsEveryMeaningfulInputIncludingInternalMatchingFields()
    {
        var details = Details();
        var row = VeteransReviewerPackageSnapshot.Capture(details, Regulations());
        var restored = VeteransReviewerPackageSnapshot.Restore(row);
        Assert.Equal(row, VeteransReviewerPackageSnapshot.Capture(restored.Details, restored.Regulations));
        Assert.Equal("rx-synthetic", restored.Details.MedicationClinicalContexts.Single().PrescriptionNumber);
        Assert.Equal("source", restored.Details.SourceClarifications.Single().ReviewerArtifactId.Value);
        Assert.Equal("source", restored.Details.ClinicalProgressionEvents.Single().ReviewerArtifactId.Value);
        Assert.Equal("replace", restored.Details.SourceClarifications.Single().ReviewerReplacementText);
        Assert.Equal("2", restored.Details.PackageDetails.Artifacts.Single().ReviewerPageSelection);
        Assert.Equal(2, restored.Details.ArtifactContents.Single().PrintablePages.Single().PageNumber);
        Assert.Equal("original indication", restored.Details.MedicationProgressions.Single().IndicationReconciliation!.Indication);
        Assert.Equal("entry", restored.Details.MedicationProgressions.Single().EntrySources.Single().Key.Value);
        Assert.Contains("Bilateral pes planus", restored.Details.MedicalOpinionRequested!.OpinionText);
        Assert.Equal("classification original", restored.Details.ArtifactContents.Single().ReviewedMedicalLiteratureClassifications.Single().Association.Description);
    }

    [Fact]
    public void CanonicalV1_IsIndependentOfCultureAndDictionaryInsertionOrder()
    {
        var old = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            var a = VeteransReviewerPackageSnapshot.Capture(Details(metadata: new() { ["z"] = 1.00m, ["a"] = "text" }), Regulations());
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var b = VeteransReviewerPackageSnapshot.Capture(Details(metadata: new() { ["a"] = "text", ["z"] = 1m }), Regulations());
            Assert.Equal(a, b);
            Assert.Contains("\"Version\":1", a.Payload);
            Assert.Contains("\"VeteranDisplayName\":null", a.Payload);
        }
        finally { CultureInfo.CurrentCulture = old; }
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("null")]
    [InlineData("unknown")]
    [InlineData("version")]
    [InlineData("identity")]
    [InlineData("pages")]
    [InlineData("duplicate-member")]
    [InlineData("hash")]
    [InlineData("duplicate-json")]
    public void Restore_FailsClosedOnCorruptOrIncompleteContract(string change)
    {
        var row = VeteransReviewerPackageSnapshot.Capture(Details(), Regulations());
        var node = JsonNode.Parse(row.Payload)!;
        switch (change)
        {
            case "missing": node["Details"]!.AsObject().Remove("CurrentMedications"); break;
            case "null": node["Details"]!["CurrentMedications"] = null; break;
            case "unknown": node["Extra"] = true; break;
            case "version": node["Version"] = 2; break;
            case "identity": node["Details"]!["PackageDetails"]!["Package"]!["Id"] = "other"; break;
            case "pages": node["Details"]!["ArtifactContents"]![0]!["PrintablePages"] = new JsonArray(); break;
            case "duplicate-member":
                var members = node["Details"]!["PackageDetails"]!["Artifacts"]!.AsArray();
                members.Add(members[0]!.DeepClone()); break;
        }
        var payload = node.ToJsonString();
        if (change == "duplicate-json") payload = row.Payload.Replace("\"Version\":1", "\"Version\":1,\"Version\":1");
        row = row with { Payload = payload, Sha256 = change == "hash" ? new string('0', 64) : ReviewerPackageSnapshot.ComputeHash(payload) };
        Assert.Throws<InvalidDataException>(() => VeteransReviewerPackageSnapshot.Restore(row));
    }

    [Fact]
    public async Task Rerender_UsesSealedInputsAfterAllCurrentInputsChange_AndDoesNotFetchRegulations()
    {
        await using var db = await Database.Create();
        var original = Details(literature: true);
        await db.Repository.AddEvidencePackageAsync(original.PackageDetails.Package, original.PackageDetails.Artifacts.ToArray());
        var provider = new RegulatoryProvider();
        var service = ReviewerDeploymentTestSupport.CreateService(regulatoryTextProvider: provider, snapshotRepository: db.Repository);
        var first = await service.RenderAsync(original, VeteransReviewerPackageOutputFormat.Docx);
        provider.Throw = true;
        var later = Details("later changed", literature: true);
        var again = await service.RenderAsync(later, VeteransReviewerPackageOutputFormat.Docx);
        Assert.Equal(DocumentText(first.Docx!), DocumentText(again.Docx!));
        Assert.Contains("classification original", DocumentText(again.Docx!));
        Assert.Contains("Bilateral pes planus", DocumentText(again.Docx!));
        Assert.DoesNotContain("later changed", DocumentText(again.Docx!));
        Assert.Equal(1, provider.Calls);
        var saved = VeteransReviewerPackageSnapshot.Restore((await db.Repository.GetReviewerSnapshotAsync(original.PackageDetails.Package.Id))!);
        Assert.Equal("source original", saved.Details.Artifacts.Single().Name);
        Assert.Equal("clarification original", saved.Details.SourceClarifications.Single().Clarification);
        Assert.Equal("progression original", saved.Details.ClinicalProgressionEvents.Single().Summary);
        Assert.Equal("original indication", saved.Details.CurrentMedications.Single().Indication);
        Assert.Equal("classification original", saved.Details.ArtifactContents.Single().ReviewedMedicalLiteratureClassifications.Single().Association.Description);
    }

    [Fact]
    public async Task Seal_IsIdempotent_RejectsConflict_AndLocksAllPersistenceMutationPaths()
    {
        await using var db = await Database.Create();
        var d = Details();
        await db.Repository.AddEvidencePackageAsync(d.PackageDetails.Package, d.PackageDetails.Artifacts.ToArray());
        var row = VeteransReviewerPackageSnapshot.Capture(d, Regulations());
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => db.Repository.SaveReviewerSnapshotAsync(row, d.PackageDetails))));
        var conflict = VeteransReviewerPackageSnapshot.Capture(Details("later"), Regulations());
        await Assert.ThrowsAsync<InvalidDataException>(() => db.Repository.SaveReviewerSnapshotAsync(conflict, d.PackageDetails));
        await Assert.ThrowsAsync<SqliteException>(() => db.Repository.SetReviewerPageSelectionAsync(row.PackageId, new("source"), "1"));
        foreach (var sql in new[]
        {
            "UPDATE VeteransClaims_EvidencePackageArtifacts SET ReviewerPageSelection='1' WHERE EvidencePackageId='package';",
            "UPDATE VeteransClaims_EvidencePackageArtifacts SET ArtifactId='other' WHERE EvidencePackageId='package';",
            "INSERT OR REPLACE INTO VeteransClaims_ReviewerPackageSnapshots SELECT * FROM VeteransClaims_ReviewerPackageSnapshots;",
            "INSERT OR REPLACE INTO VeteransClaims_EvidencePackages SELECT * FROM VeteransClaims_EvidencePackages;",
            "UPDATE VeteransClaims_EvidencePackages SET Purpose='changed' WHERE Id='package';",
            "DELETE FROM VeteransClaims_EvidencePackageArtifacts WHERE EvidencePackageId='package';",
            "INSERT INTO VeteransClaims_EvidencePackageArtifacts VALUES ('package','another','UnderlyingEvidence',NULL);",
            "UPDATE VeteransClaims_ReviewerPackageSnapshots SET Sha256='bad' WHERE EvidencePackageId='package';",
            "DELETE FROM VeteransClaims_ReviewerPackageSnapshots WHERE EvidencePackageId='package';"
        }) await Assert.ThrowsAsync<SqliteException>(() => db.Sql(sql));
        Assert.Equal(row, await db.Repository.GetReviewerSnapshotAsync(row.PackageId));
    }

    [Fact]
    public async Task Capture_RejectsMembershipRaceWithoutLeavingPartialSeal()
    {
        await using var db = await Database.Create();
        var d = Details();
        await db.Repository.AddEvidencePackageAsync(d.PackageDetails.Package, d.PackageDetails.Artifacts.ToArray());
        await db.Repository.SetReviewerPageSelectionAsync(new("package"), new("source"), "1");
        await Assert.ThrowsAsync<InvalidDataException>(() => db.Repository.SaveReviewerSnapshotAsync(
            VeteransReviewerPackageSnapshot.Capture(d, Regulations()), d.PackageDetails));
        Assert.Null(await db.Repository.GetReviewerSnapshotAsync(new("package")));
        await db.Repository.SetReviewerPageSelectionAsync(new("package"), new("source"), "2");
    }

    [Fact]
    public async Task FailedPdfConversion_DoesNotSeal_AndSuccessfulRetrySeals()
    {
        await using var db = await Database.Create();
        var d = Details();
        await db.Repository.AddEvidencePackageAsync(d.PackageDetails.Package, d.PackageDetails.Artifacts.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => ReviewerDeploymentTestSupport.CreateService(
            new InvalidConverter(), new RegulatoryProvider(), db.Repository).RenderAsync(d, VeteransReviewerPackageOutputFormat.Pdf));
        Assert.Null(await db.Repository.GetReviewerSnapshotAsync(new("package")));
        await ReviewerDeploymentTestSupport.CreateService(regulatoryTextProvider: new RegulatoryProvider(), snapshotRepository: db.Repository)
            .RenderAsync(d, VeteransReviewerPackageOutputFormat.Docx);
        Assert.NotNull(await db.Repository.GetReviewerSnapshotAsync(new("package")));
    }

    [Fact]
    public async Task Migration_IsConcurrentIdempotent_AndNeverBackfillsLegacyMeaning()
    {
        var path = Path.GetTempFileName();
        try
        {
            await new VeteransClaimsSqliteMigrator(path, VeteransClaimsSqliteMigrations.All.Where(x => x.Version < 90).ToArray()).MigrateAsync();
            await using var db = new Database(path);
            await db.Seed();
            await db.Sql("INSERT INTO VeteransClaims_EvidencePackages(Id,ClaimIssueId,Purpose,ReviewerRole,CreationOrdinal) VALUES ('legacy','issue','Old','MedicalProfessional',1);");
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => new VeteransClaimsSqliteSchema(path).InitializeAsync())));
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            var error = await Assert.ThrowsAsync<InvalidDataException>(() => db.Repository.GetReviewerSnapshotAsync(new("legacy")));
            Assert.Contains("Legacy", error.Message);
            await Assert.ThrowsAsync<SqliteException>(() => db.Sql("UPDATE VeteransClaims_EvidencePackages SET ReviewerSnapshotVersion=1 WHERE Id='legacy';"));
            Assert.Equal(0L, await db.Scalar("SELECT count(*) FROM VeteransClaims_ReviewerPackageSnapshots;"));
            Assert.Equal(1L, await db.Scalar("SELECT count(*) FROM VeteransClaims_SchemaMigrations WHERE Version=90;"));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task PublicationFailureAfterSeal_RetryPublishesSnapshotWithoutConsultingMutableInputs()
    {
        using var deployment = new ExpectedReviewerDeployment();
        await using var db = await Database.Create();
        var d = Details();
        await db.Repository.AddEvidencePackageAsync(d.PackageDetails.Package, d.PackageDetails.Artifacts.ToArray());
        var original = await ReviewerDeploymentTestSupport.CreateService(
            regulatoryTextProvider: new RegulatoryProvider(), snapshotRepository: db.Repository)
            .RenderAsync(d, VeteransReviewerPackageOutputFormat.Docx);
        var sealedRow = await db.Repository.GetReviewerSnapshotAsync(new("package"));
        var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            // An existing directory at the output filename forces atomic publication
            // to fail after rendering. No current-input provider is permitted to run.
            var output = System.IO.Path.Combine(root, "retry.docx");
            Directory.CreateDirectory(output);
            var request = new EMF.ConsoleApplication.VeteransReviewerPackageOutputRequest(
                VeteransReviewerPackageOutputFormat.Docx, output, null);
            var regulatory = new RegulatoryProvider { Throw = true };
            Task<int> Publish() => EMF.ConsoleApplication.VeteransConsoleCommand.RunEvidencePackageDocumentAsync(
                db.PathValue, new("package"), request,
                contentStoreFactory: () => throw new InvalidOperationException("Current evidence must not be consulted"),
                suppliedRegulatoryTextProvider: regulatory);
            await Assert.ThrowsAnyAsync<IOException>(Publish);
            Assert.Equal(sealedRow, await db.Repository.GetReviewerSnapshotAsync(new("package")));
            var evidence = new EMF.Persistence.Repositories.SqliteEvidenceRepository(db.PathValue);
            await evidence.InitializeAsync();
            await evidence.AddArtifactAsync(Details("later changed metadata").Artifacts.Single());
            Directory.Delete(output);
            Assert.Equal(0, await Publish());
            Assert.Equal(DocumentText(original.Docx!), DocumentText(await File.ReadAllBytesAsync(output)));
            Assert.Equal(0, regulatory.Calls);
            Assert.Equal(sealedRow, await db.Repository.GetReviewerSnapshotAsync(new("package")));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void ExplicitV1FieldMap_CoversRendererInputPropertiesIncludingJsonIgnore()
    {
        // A new DTO field demands an explicit contract review, never silent adoption
        // by an ambient serializer. The checked-in V1 map defines the wire fields.
        foreach (var (type, fields) in VeteransReviewerSnapshotV1Contract.Fields)
        {
            // Reviewed preparation-only field: typed cover belongs to the immutable
            // presentation envelope (migration 94), never the historical V1 wire.
            // All other DTO fields still demand explicit V1 contract coverage.
            var properties = type.GetProperties().Where(p =>
                type != typeof(VeteransReviewerPackageDetails) ||
                p.Name != nameof(VeteransReviewerPackageDetails.ResolvedCover)).ToArray();
            if (type == typeof(VeteransReviewerPackageDetails))
                Assert.DoesNotContain(nameof(VeteransReviewerPackageDetails.ResolvedCover), fields);
            Assert.Equal(properties.Select(p => p.Name).OrderBy(x => x, StringComparer.Ordinal),
                fields.OrderBy(x => x, StringComparer.Ordinal));
            foreach (var property in properties)
                Assert.Equal(new System.Reflection.NullabilityInfoContext().Create(property).WriteState == System.Reflection.NullabilityState.Nullable,
                    VeteransReviewerSnapshotV1Contract.NullableFields.Contains((type, property.Name)));
        }
    }

    [Fact]
    public async Task ConflictingConcurrentSeals_HaveExactlyOneWinner()
    {
        await using var db = await Database.Create();
        var d = Details();
        await db.Repository.AddEvidencePackageAsync(d.PackageDetails.Package, d.PackageDetails.Artifacts.ToArray());
        var candidates = new[] { VeteransReviewerPackageSnapshot.Capture(d, Regulations()),
            VeteransReviewerPackageSnapshot.Capture(Details("competing"), Regulations()) };
        var outcomes = await Task.WhenAll(candidates.Select(row => Task.Run(async () =>
        {
            try { await db.Repository.SaveReviewerSnapshotAsync(row, d.PackageDetails); return true; }
            catch (InvalidDataException) { return false; }
        })));
        Assert.Single(outcomes.Where(x => x));
        Assert.Contains(await db.Repository.GetReviewerSnapshotAsync(new("package")), candidates);
    }

    [Fact]
    public async Task CorruptedPersistedPayload_IsRejectedWithoutCurrentStateFallback()
    {
        await using var db = await Database.Create();
        var d = Details();
        await db.Repository.AddEvidencePackageAsync(d.PackageDetails.Package, d.PackageDetails.Artifacts.ToArray());
        await db.Sql("INSERT INTO VeteransClaims_ReviewerPackageSnapshots VALUES ('package',1,'{}','" + ReviewerPackageSnapshot.ComputeHash("{}") + "');");
        var provider = new RegulatoryProvider { Throw = true };
        await Assert.ThrowsAsync<InvalidDataException>(() => ReviewerDeploymentTestSupport.CreateService(
            regulatoryTextProvider: provider, snapshotRepository: db.Repository).RenderAsync(d, VeteransReviewerPackageOutputFormat.Docx));
        Assert.Equal(0, provider.Calls);
    }

    [Fact]
    public void V1_PreservesPrintableBytesGeometryAndRotation()
    {
        var d = Details();
        var original = d.ArtifactContents.Single();
        var bytes = new byte[] { 0, 1, 2, 255 };
        var copy = new VeteransReviewerPackageDetails
        {
            PackageDetails = d.PackageDetails, Artifacts = d.Artifacts,
            ArtifactContents = [new VeteransReviewerArtifactContent
            {
                Artifact = original.Artifact, Text = original.Text, ReviewerPageSelection = "2",
                PrintableSourceArtifactId = new("parent"), IsExtractedTextFallback = true,
                PrintablePages = [new PrintableArtifactPage
                {
                    PageNumber = 2, ContentType = "image/png", Content = bytes, SuggestedClockwiseRotation = 90,
                    TextGeometry = new PrintableArtifactTextGeometry(100.5, 200.25, true,
                        [new PrintableArtifactGlyph("text", 1, 2, 3, 4, 5, 6, 7, "font", 12.5)])
                }]
            }]
        };
        var row = VeteransReviewerPackageSnapshot.Capture(copy, []);
        bytes[0] = 123;
        var restored = VeteransReviewerPackageSnapshot.Restore(row).Details.ArtifactContents.Single();
        var page = restored.PrintablePages.Single();
        Assert.Equal(new byte[] { 0, 1, 2, 255 }, page.Content.ToArray());
        Assert.Equal(90, page.SuggestedClockwiseRotation);
        Assert.Equal(100.5, page.TextGeometry!.Width);
        Assert.Equal(12.5, page.TextGeometry.Glyphs.Single().FontSize);
        Assert.Equal("parent", restored.PrintableSourceArtifactId!.Value.Value);
    }

    [Fact]
    public async Task SealMarkerAndPayload_RollBackTogether_WhenTriggerFails()
    {
        await using var db = await Database.Create();
        var d = Details();
        await db.Repository.AddEvidencePackageAsync(d.PackageDetails.Package, d.PackageDetails.Artifacts.ToArray());
        await db.Sql("CREATE TRIGGER SimulatedSealFailure BEFORE UPDATE ON VeteransClaims_EvidencePackages WHEN NEW.ReviewerSnapshotSealed=1 BEGIN SELECT RAISE(ABORT, 'simulated failure'); END;");
        var row = VeteransReviewerPackageSnapshot.Capture(d, Regulations());
        await Assert.ThrowsAsync<SqliteException>(() => db.Repository.SaveReviewerSnapshotAsync(row, d.PackageDetails));
        Assert.Null(await db.Repository.GetReviewerSnapshotAsync(new("package")));
        Assert.Equal(0L, await db.Scalar("SELECT count(*) FROM VeteransClaims_ReviewerPackageSnapshots;"));
        Assert.Equal(0L, await db.Scalar("SELECT ReviewerSnapshotSealed FROM VeteransClaims_EvidencePackages WHERE Id='package';"));
        await db.Sql("DROP TRIGGER SimulatedSealFailure;");
        await db.Repository.SaveReviewerSnapshotAsync(row, d.PackageDetails);
        Assert.Equal(1L, await db.Scalar("SELECT ReviewerSnapshotSealed FROM VeteransClaims_EvidencePackages WHERE Id='package';"));
    }

    [Fact]
    public async Task MissingRowOnSealedPackage_IsCorruption_NotPermissionToRecapture()
    {
        await using var db = await Database.Create();
        var d = Details();
        await db.Repository.AddEvidencePackageAsync(d.PackageDetails.Package, d.PackageDetails.Artifacts.ToArray());
        var row = VeteransReviewerPackageSnapshot.Capture(d, Regulations());
        await db.Repository.SaveReviewerSnapshotAsync(row, d.PackageDetails);
        // Simulate offline corruption only in this disposable test database.
        await db.Sql("DROP TRIGGER ReviewerSnapshot_NoDelete; DELETE FROM VeteransClaims_ReviewerPackageSnapshots;");
        await Assert.ThrowsAsync<InvalidDataException>(() => db.Repository.GetReviewerSnapshotAsync(new("package")));
        await Assert.ThrowsAsync<InvalidDataException>(() => db.Repository.SaveReviewerSnapshotAsync(row, d.PackageDetails));
        await Assert.ThrowsAsync<SqliteException>(() => db.Repository.SetReviewerPageSelectionAsync(new("package"), new("source"), "1"));
    }

    [Fact]
    public async Task HistoricalAssembly_IgnoresLaterPersistedClinicalMetadataAndClassificationChanges()
    {
        await using var db = await Database.Create();
        var d = Details();
        await db.Repository.AddEvidencePackageAsync(d.PackageDetails.Package, d.PackageDetails.Artifacts.ToArray());
        var row = VeteransReviewerPackageSnapshot.Capture(d, Regulations());
        await db.Repository.SaveReviewerSnapshotAsync(row, d.PackageDetails);
        var evidence = new EMF.Persistence.Repositories.SqliteEvidenceRepository(db.PathValue);
        await evidence.InitializeAsync();
        await evidence.AddArtifactAsync(Details("changed metadata").Artifacts.Single());
        var classifications = new SqliteEvidenceClassificationRepository(db.PathValue);
        await classifications.AddEvidenceClassificationAsync(new EvidenceClassification
        { Id = new("later-classification"), ArtifactId = new("source"), ClaimIssueId = new("issue"), Classification = "MedicalOpinion" });
        var literature = new SqliteMedicalLiteratureRepository(db.PathValue);
        await literature.AddMedicalLiteratureSourceAsync(new MedicalLiteratureSource
        { Id = new("later-literature"), Title = "Changed literature title", Authors = "Synthetic author", Publication = "Synthetic publication", PublicationYear = 2026 });
        await literature.AddMedicalLiteratureSourceArtifactAsync(new MedicalLiteratureSourceArtifact
        { MedicalLiteratureSourceId = new("later-literature"), ArtifactId = new("source") });
        var medications = new SqliteMedicationRepository(db.PathValue);
        await medications.AddMedicationIndicationReconciliationAsync(new MedicationIndicationReconciliation
        { Id = "later-reconciliation", VeteranId = new("veteran"), MedicationName = "Synthetic medication", ReconciliationDate = new(2026, 2, 1), Indication = "changed indication", Source = "Synthetic later report" });
        var clarifications = new SqliteSourceClarificationRepository(db.PathValue);
        await clarifications.AddAsync(new SourceClarification
        { Id = new("later-clarification"), ClaimIssueId = new("issue"), SourceArtifactId = new("source"), EvidenceDate = new(2026, 2, 1), SourceStartPage = 2, SourceEndPage = 2,
            RecordTitle = "Synthetic record", Category = SourceClarificationCategories.InternalConflict, OriginalText = "old", Clarification = "changed clarification" });
        var progression = new SqliteClinicalProgressionRepository(db.PathValue);
        await progression.AddAsync(new EMF.Extensions.VeteransClaims.Models.Clinical.ClinicalProgressionEvent
        { Id = new("later-progression"), ClaimIssueId = new("issue"), SourceArtifactId = new("source"), EventDate = new(2026, 2, 1), SourceStartPage = 2, SourceEndPage = 2,
            RecordTitle = "Synthetic record", EventType = "DiagnosticFinding", Summary = "changed progression" });
        var claims = new SqliteClaimRepository(db.PathValue);
        var issues = new SqliteClaimIssueRepository(db.PathValue);
        var bases = new SqliteServiceConnectionRepository(db.PathValue);
        var assembly = new VeteransReviewerPackageAssemblyService(
            new VeteransReviewerPackageDetailsService(new EMF.Extensions.VeteransClaims.Services.EvidencePackageService(db.Repository, new EMF.Common.GuidIdGenerator()),
                evidence, classifications, new SqliteMedicalLiteratureRepository(db.PathValue)),
            new VeteransReviewerPackageCurrentMedicationService(issues, claims,
                new EMF.Extensions.VeteransClaims.Services.ReconciledCurrentMedicationLedgerService(
                    new EMF.Extensions.VeteransClaims.Services.CurrentMedicationLedgerService(medications), medications)),
            new VeteransReviewerPackageMedicationProgressionService(issues, claims, bases, medications),
            new VeteransReviewerPackageMedicationClinicalContextService(issues, claims, medications, evidence),
            new VeteransReviewerPackageSourceClarificationService(clarifications),
            new VeteransReviewerPackageClinicalProgressionService(progression),
            new VeteransReviewerMedicalOpinionRequestService(bases, new SqliteConditionRepository(db.PathValue), new SqliteRegulatoryRepository(db.PathValue)),
            db.Repository);
        var historical = await assembly.AssembleAsync(new("package"), "changed preparer", "changed display name");
        Assert.NotNull(historical);
        Assert.Equal(row, VeteransReviewerPackageSnapshot.Capture(historical!, Regulations()));
    }

    [Fact]
    public void V1_EmptyContractHasPinnedCanonicalBytesAndVersionCoveredHash()
    {
        var details = new VeteransReviewerPackageDetails
        {
            PackageDetails = new EvidencePackageDetails
            {
                Package = new EvidencePackage { Id = new("package"), ClaimIssueId = new("issue"), Purpose = "Review", ReviewerRole = "MedicalProfessional" },
                Artifacts = []
            },
            Artifacts = []
        };
        var row = VeteransReviewerPackageSnapshot.Capture(details, []);
        const string expected = """
        {"Details":{"ArtifactContents":[],"Artifacts":[],"ClinicalProgressionEvents":[],"CurrentMedications":[],"CurrentPrescribedMedications":[],"MedicalOpinionRequested":null,"MedicationClinicalContexts":[],"MedicationProgressions":[],"PackageDetails":{"Artifacts":[],"Package":{"ClaimIssueId":"issue","Id":"package","Purpose":"Review","ReviewerRole":"MedicalProfessional","ServiceConnectionBasisId":null}},"PackagePreparedBy":null,"SourceClarifications":[],"VeteranDisplayName":null},"Regulations":[],"Version":1}
        """;
        Assert.Equal(expected, row.Payload);
        Assert.Equal("A4568DA4BB6E7833A52E07EBEBBC5D4DD76F4A9386A38A269D59AE4004240D33", row.Sha256);
        Assert.NotEqual(row.Sha256, ReviewerPackageSnapshot.ComputeHash(row.Payload.Replace("\"Version\":1", "\"Version\":2")));
    }

    [Theory]
    [InlineData("1.000e2", "100")]
    [InlineData("-0.0012300e1", "-0.0123")]
    [InlineData("123456789012345678901234567890123456789", "123456789012345678901234567890123456789")]
    [InlineData("1.23456789012345678901234567890123456789e-30", "0.00000000000000000000000000000123456789012345678901234567890123456789")]
    public void CanonicalNumbers_AreExactAndUnambiguous(string input, string expected)
    {
        using var document = System.Text.Json.JsonDocument.Parse(input);
        var a = VeteransReviewerPackageSnapshot.Capture(Details(metadata: new() { ["number"] = document.RootElement.Clone() }), Regulations());
        using var normalized = System.Text.Json.JsonDocument.Parse(expected);
        var b = VeteransReviewerPackageSnapshot.Capture(Details(metadata: new() { ["number"] = normalized.RootElement.Clone() }), Regulations());
        Assert.Equal(a, b);
        Assert.Contains("\"number\":" + expected, a.Payload);
    }

    private static string DocumentText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var doc = WordprocessingDocument.Open(stream, false);
        return doc.MainDocumentPart!.Document!.InnerText;
    }

    internal static VeteransReviewerPackageDetails Details(string state = "original", Dictionary<string, object>? metadata = null, bool literature = false)
    {
        var artifact = new Artifact { Id = new("source"), Name = "source " + state, ArtifactType = "file", CreatedUtc = DateTimeOffset.UnixEpoch,
            Metadata = metadata ?? new() { [EMF.Extensions.VeteransClaims.Models.VeteransArtifactMetadataKeys.EvidenceTitle] = "title " + state } };
        var entry = new MedicationLedgerEntry { Id = new("entry"), MedicationLedgerId = new("ledger"), EntryOrdinal = 1,
            SourceStartPage = 2, SourceEndPage = 2, MedicationName = "Synthetic medication", Status = "active", PrescriptionNumber = "rx-synthetic", Indication = state + " indication" };
        return new VeteransReviewerPackageDetails
        {
            PackageDetails = new EvidencePackageDetails
            {
                Package = new EvidencePackage { Id = new("package"), ClaimIssueId = new("issue"), Purpose = "Independent medical review", ReviewerRole = "MedicalProfessional" },
                Artifacts = [new EvidencePackageArtifact { EvidencePackageId = new("package"), ArtifactId = artifact.Id, ContentRole = EvidencePackageContentRoles.UnderlyingEvidence, ReviewerPageSelection = "2" }]
            },
            Artifacts = [artifact],
            ArtifactContents = [new VeteransReviewerArtifactContent { Artifact = artifact, Text = "Factual source " + state, ReviewerPageSelection = "2", SourceName = "record " + state,
                PrintablePages = [new PrintableArtifactPage { PageNumber = 2, ContentType = "text/plain", Content = Encoding.UTF8.GetBytes("Factual source " + state) }],
                MedicalLiteratureReviewerText = "Literature text " + state, Appendix = literature ? VeteransReviewerPackageAppendix.MedicalLiterature : null,
                ReviewedMedicalLiteratureClassifications = [new ReviewedMedicalLiteratureClassification {
                    ArtifactId = artifact.Id, Association = new RequirementMedicalLiterature { RequirementId = new("requirement"), MedicalLiteratureSourceId = new("literature"), GuidanceRole = "Context", Description = "classification " + state },
                    PromotedBy = "synthetic", PromotedUtc = DateTimeOffset.UnixEpoch, ReviewedBy = "synthetic", ReviewedUtc = DateTimeOffset.UnixEpoch,
                    IntelligenceOutput = "review " + state, CapabilityId = "capability", ProviderId = "local", CorrelationId = "correlation", EngineName = "local",
                    StartedUtc = DateTimeOffset.UnixEpoch, CompletedUtc = DateTimeOffset.UnixEpoch, RequiresReview = false, Warnings = [], SourceExcerpts = []
                }] }],
            CurrentMedications = [entry],
            MedicationProgressions = [new VeteransReviewerMedicationProgression { MedicationName = entry.MedicationName, Entries = [entry], EntrySources = new Dictionary<MedicationLedgerEntryId, string> { [entry.Id] = "record page 2" },
                IndicationReconciliation = new MedicationIndicationReconciliation { Id = "reconciliation", VeteranId = new("veteran"), MedicationName = entry.MedicationName, ReconciliationDate = new(2026, 1, 1), Indication = state + " indication", Source = "Synthetic report" } }],
            MedicationClinicalContexts = [new VeteransReviewerMedicationClinicalContext { MedicationName = entry.MedicationName, PrescriptionNumber = entry.PrescriptionNumber!, ContextType = "Clinical", SourceLocator = "page 2", Summary = "context " + state }],
            SourceClarifications = [new VeteransReviewerSourceClarification { ReviewerArtifactId = artifact.Id, SourceLocator = "page 2", OriginalText = "original", Clarification = "clarification " + state, ReviewerMatchText = "Factual", ReviewerReplacementText = "replace" }],
            ClinicalProgressionEvents = [new VeteransReviewerClinicalProgressionEvent { ReviewerArtifactId = artifact.Id, EventDate = new(2026, 1, 1), EventType = "DiagnosticFinding", SourceLocator = "page 2", Summary = "progression " + state }],
            MedicalOpinionRequested = new VeteransReviewerMedicalOpinionRequest { OpinionText = "Request an independent nexus opinion regarding Bilateral pes planus: " + state, ApplicableRegulatoryCitations = ["38 C.F.R. § 3.310(a)"] }
        };
    }

    internal static IReadOnlyList<VeteransReviewerApplicableRegulation> Regulations() =>
        [new() { Citation = "38 C.F.R. § 3.310(a)", Text = "Synthetic regulatory text", SourceUri = "https://example.invalid/regulation", UpToDateAsOf = new(2026, 1, 1), RetrievedUtc = DateTimeOffset.UnixEpoch, SourceSha256 = new string('A', 64) }];

    private sealed class RegulatoryProvider : IVeteransReviewerRegulatoryTextProvider
    {
        public bool Throw;
        public int Calls;
        public Task<IReadOnlyList<VeteransReviewerApplicableRegulation>> GetCurrentAsync(IReadOnlyList<string> citations, CancellationToken cancellationToken = default)
        { Calls++; if (Throw) throw new InvalidOperationException("Must not fetch current regulations"); return Task.FromResult(Regulations()); }
    }
    private sealed class InvalidConverter : IVeteransReviewerPackageDocumentConverter,
        IVeteransReviewerPackageDocumentConverterInfoProvider
    {
        public Task<byte[]> ConvertDocxToPdfAsync(ReadOnlyMemory<byte> docx, CancellationToken cancellationToken = default) => Task.FromResult(new byte[] { 0 });
        public Task<VeteransReviewerPackageDocumentConverterInfo> GetDocumentConverterInfoAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(new VeteransReviewerPackageDocumentConverterInfo("Synthetic invalid-output converter", "1"));
    }
    private sealed class Database(string path) : IAsyncDisposable
    {
        public SqliteEvidencePackageRepository Repository { get; } = new(path);
        public static async Task<Database> Create()
        {
            var db = new Database(Path.GetTempFileName());
            await new VeteransClaimsSqliteSchema(db.PathValue).InitializeAsync();
            await db.Seed();
            return db;
        }
        public string PathValue => path;
        public Task Seed() => Sql("INSERT INTO VeteransClaims_Veterans VALUES ('veteran'); INSERT INTO VeteransClaims_Claims VALUES ('claim','veteran'); INSERT INTO VeteransClaims_ClaimIssues VALUES ('issue','claim','ServiceConnection');");
        public async Task Sql(string sql)
        {
            await using var c = new SqliteConnection($"Data Source={path}"); await c.OpenAsync();
            await using var command = c.CreateCommand(); command.CommandText = sql; await command.ExecuteNonQueryAsync();
        }
        public async Task<object?> Scalar(string sql)
        {
            await using var c = new SqliteConnection($"Data Source={path}"); await c.OpenAsync();
            await using var command = c.CreateCommand(); command.CommandText = sql; return await command.ExecuteScalarAsync();
        }
        public ValueTask DisposeAsync() { SqliteConnection.ClearAllPools(); File.Delete(path); return ValueTask.CompletedTask; }
    }
}

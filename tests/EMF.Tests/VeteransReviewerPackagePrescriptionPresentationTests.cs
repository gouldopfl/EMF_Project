using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.ConsoleApplication;

namespace EMF.Tests;

public sealed class VeteransReviewerPackagePrescriptionPresentationTests
{
    private static VeteransReviewerArtifactContent Derive(VeteransReviewerPackageDetails details, DateOnly cutoff,
        IReadOnlyList<MedicationLedger> ledgers, IReadOnlyList<MedicationLedgerEntry> entries,
        IReadOnlyList<MedicationCurrentUseReconciliation> reconciliations, IReadOnlyList<string> priorities) =>
        VeteransReviewerPackagePrescriptionPresentation.Derive(details, cutoff, ledgers, entries, reconciliations, priorities,
            new DateTimeOffset(cutoff.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));

    internal static MedicationLedger Ledger(string id = "ledger", int month = 9, int day = 9) => new()
    { Id = new(id), VeteranId = new("veteran"), SourceArtifactId = new("source"), ReportDate = new(2026, month, day),
        SourceStartPage = 10, SourceEndPage = 20, IsComplete = true, ParsedEntryCount = 9, ReportedEntryCount = 9 };

    internal static MedicationLedgerEntry[] Entries(string ledger = "ledger", string directions = "TAKE THREE TABLETS ORALLY AT BEDTIME FOR INSOMNIA.") =>
        new[] { ("Allopurinol 300 mg", "active"), ("Trazodone 100 mg", "active"), ("Refill medication", "refillinprocess"),
            ("Reported non-use medication", "active"), ("Historical medication", "expired"), ("Lamotrigine 200 mg", "active"),
            ("Sertraline 50 mg", "active"), ("Bupropion 300 mg", "active"), ("Missing documented fields", "active") }
        .Select((item, i) => new MedicationLedgerEntry { Id = new(ledger + "-" + i), MedicationLedgerId = new(ledger),
            EntryOrdinal = i + 1, SourceStartPage = 10, SourceEndPage = 11, MedicationName = item.Item1, Status = item.Item2,
            Strength = i == 8 ? null : "source strength", Directions = i == 8 ? null : directions }).ToArray();

    internal static MedicationCurrentUseReconciliation[] Reconciliations(string ledger = "ledger") =>
        [new() { Id = new("reconciliation"), VeteranId = new("veteran"), MedicationLedgerEntryId = new(ledger + "-3"),
            ReconciliationDate = new(2026, 9, 15), CurrentUseStatus = "NotCurrentlyUsed", Source = "VeteranReported" }];

    internal static VeteransReviewerArtifactContent DeriveFor(VeteransReviewerPackageDetails details)
    {
        var original = Ledger();
        var ledger = new MedicationLedger { Id = original.Id, VeteranId = original.VeteranId,
            SourceArtifactId = details.Artifacts[0].Id, ReportDate = original.ReportDate, SourceStartPage = 10, SourceEndPage = 20,
            IsComplete = true, ParsedEntryCount = 9, ReportedEntryCount = 9 };
        return Derive(details, new(2026, 9, 26), [ledger], Entries(), Reconciliations(),
            ["Bupropion", "Lamotrigine", "Sertraline", "Trazodone"]);
    }

    private static VeteransReviewerPackageDetails Details(string id = "package-one")
    {
        var artifact = new Artifact { Id = new("source"), Name = "Verified VA medication report", ArtifactType = "medical-record" };
        return new() { PackageDetails = new() { Package = new() { Id = new(id), ClaimIssueId = new("claim-" + id), Purpose = "review", ReviewerRole = "MedicalProfessional" },
            Artifacts = [new() { EvidencePackageId = new(id), ArtifactId = artifact.Id, ContentRole = EvidencePackageContentRoles.UnderlyingEvidence }] },
            Artifacts = [artifact], ArtifactContents = [new() { Artifact = artifact, Text = "Source evidence.",
                PrintablePages = [new() { PageNumber = 10, ContentType = "text/plain", Content = Encoding.UTF8.GetBytes("Source evidence.") }] }] };
    }

    [Fact]
    public void CutoffSelectsLatestApplicableEvidenceAndIgnoresLaterLedger()
    {
        var details = Details();
        var rows = Entries("old").Concat(Entries()).Concat(Entries("future")).ToArray();
        var result = Derive(details, new(2026, 9, 26),
            [Ledger("old", 8, 1), Ledger(), Ledger("future", 10, 1)], rows, Reconciliations(), []);
        Assert.Equal("VA Prescription List — September 9, 2026", result.Artifact.Name);
        Assert.Contains("ledger-", result.Artifact.Metadata["prescriptionEvidence"].ToString());
        Assert.DoesNotContain("future-", result.Artifact.Metadata["prescriptionEvidence"].ToString());
    }

    [Fact]
    public void LaterClaimAndMedicationStateDoNotChangeEarlierSnapshotOrRendering()
    {
        var earlier = Details();
        var frozen = VeteransReviewerPackagePrescriptionPresentation.Attach(earlier, DeriveFor(earlier));
        var snapshot = VeteransReviewerPackageSnapshot.Capture(frozen, []);
        var before = DocumentText(VeteransReviewerPackageDocxRenderer.Render(VeteransReviewerPackageSnapshot.Restore(snapshot).Details));
        var later = Details("package-two");
        var laterArtifact = Derive(later, new(2026, 10, 2),
            [Ledger(), Ledger("later", 10, 1)], Entries().Concat(Entries("later", "DIFFERENT LATER DIRECTIONS")).ToArray(), [], []);
        var laterSnapshot = VeteransReviewerPackageSnapshot.Capture(VeteransReviewerPackagePrescriptionPresentation.Attach(later, laterArtifact), []);
        Assert.Contains("DIFFERENT LATER DIRECTIONS", DocumentText(VeteransReviewerPackageDocxRenderer.Render(VeteransReviewerPackageSnapshot.Restore(laterSnapshot).Details)));
        var restored = VeteransReviewerPackageSnapshot.Restore(snapshot);
        Assert.Equal(snapshot, VeteransReviewerPackageSnapshot.Capture(restored.Details, restored.Regulations));
        Assert.Equal(before, DocumentText(VeteransReviewerPackageDocxRenderer.Render(restored.Details)));
        Assert.DoesNotContain("DIFFERENT LATER DIRECTIONS", before);
    }

    [Fact]
    public void DerivationIsDeterministicAndPreservesOrderMissingFieldsAndUseQualification()
    {
        var details = Details();
        var a = DeriveFor(details);
        var b = DeriveFor(details);
        Assert.Equal(a.Text, b.Text);
        Assert.Equal(a.Artifact.Id, b.Artifact.Id);
        Assert.Equal(VeteransReviewerPackageSnapshot.Capture(VeteransReviewerPackagePrescriptionPresentation.Attach(details, a), []),
            VeteransReviewerPackageSnapshot.Capture(VeteransReviewerPackagePrescriptionPresentation.Attach(details, b), []));
        var text = a.Text;
        var positions = new[] { "Bupropion 300 mg", "Lamotrigine 200 mg", "Sertraline 50 mg", "Trazodone 100 mg", "Allopurinol 300 mg" }
            .Select(x => text.IndexOf(x, StringComparison.Ordinal)).ToArray();
        Assert.Equal(positions.Order().ToArray(), positions);
        Assert.All(positions, p => Assert.True(p >= 0));
        Assert.DoesNotContain("current-use reconciliation", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Current Medication List", text);
        Assert.Contains("Reported non-use medication", text);
        Assert.DoesNotContain("Historical medication", text);
        Assert.Contains("Missing documented fields\nVA status: active", text);
        Assert.Contains("TAKE THREE TABLETS ORALLY AT BEDTIME FOR INSOMNIA.", text);
    }

    [Fact]
    public void LaterReconciliationDoesNotAffectEarlierCutoff()
    {
        var result = Derive(Details(), new(2026, 9, 10), [Ledger()], Entries(), Reconciliations(), []);
        Assert.Contains("Reported non-use medication", result.Text);
    }

    [Fact]
    public void OutOfPackageAndFutureEvidenceCannotSupplyTheList()
    {
        Assert.Throws<InvalidDataException>(() => Derive(Details(), new(2026, 8, 1), [Ledger()], Entries(), [], []));
        var empty = new VeteransReviewerPackageDetails { PackageDetails = new() { Package = Details().PackageDetails.Package, Artifacts = [] }, Artifacts = [], ArtifactContents = [] };
        Assert.Throws<InvalidDataException>(() => Derive(empty, new(2026, 9, 26), [Ledger()], Entries(), [], []));
    }

    [Fact]
    public void AmbiguousLedgerAndReconciliationFailClosed()
    {
        Assert.Throws<InvalidDataException>(() => Derive(Details(), new(2026, 9, 26), [Ledger(), Ledger("duplicate")], Entries(), [], []));
        Assert.Throws<InvalidDataException>(() => Derive(Details(), new(2026, 9, 26), [Ledger()], Entries(), [..Reconciliations(), ..Reconciliations()], []));
    }

    [Fact]
    public void HistoricalStatusesAreExcludedWithoutInferringFromMissingDirections()
    {
        var rows = Entries().Select((e, i) => i is 2 or 3 ? new MedicationLedgerEntry {
            Id = e.Id, MedicationLedgerId = e.MedicationLedgerId, EntryOrdinal = e.EntryOrdinal,
            SourceStartPage = e.SourceStartPage, SourceEndPage = e.SourceEndPage, MedicationName = e.MedicationName,
            Status = i == 2 ? "transferred" : "discontinued" } : e).ToArray();
        var result = Derive(Details(), new(2026, 9, 26), [Ledger()], rows, [], []);
        Assert.DoesNotContain("Refill medication", result.Text);
        Assert.DoesNotContain("Reported non-use medication", result.Text);
        Assert.Contains("Missing documented fields\nVA status: active", result.Text);
    }

    [Fact]
    public void FrozenPresentationCannotBeAttachedToAnotherPackageOrAlteredAfterCapture()
    {
        var presentation = DeriveFor(Details());
        Assert.Throws<InvalidDataException>(() => VeteransReviewerPackagePrescriptionPresentation.Attach(Details("other"), presentation));
        var altered = new VeteransReviewerArtifactContent { Artifact = presentation.Artifact, Text = presentation.Text + "tampered" };
        Assert.Throws<InvalidDataException>(() => VeteransReviewerPackagePrescriptionPresentation.Attach(Details(), altered));
        var once = VeteransReviewerPackagePrescriptionPresentation.Attach(Details(), presentation);
        Assert.Throws<InvalidDataException>(() => VeteransReviewerPackagePrescriptionPresentation.Attach(once, presentation));
    }

    [Fact]
    public void PreparationDateAndSelectedSourcePagesBoundTheDerivation()
    {
        Assert.Throws<InvalidDataException>(() => VeteransReviewerPackagePrescriptionPresentation.Derive(Details(),
            new(2026, 9, 26), [Ledger()], Entries(), [], [], new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero)));
        var details = Details();
        var selected = new VeteransReviewerPackageDetails { PackageDetails = new() {
            Package = details.PackageDetails.Package,
            Artifacts = [new() { EvidencePackageId = details.PackageDetails.Package.Id, ArtifactId = new("source"),
                ContentRole = EvidencePackageContentRoles.UnderlyingEvidence, ReviewerPageSelection = "10" }] },
            Artifacts = details.Artifacts, ArtifactContents = details.ArtifactContents };
        Assert.Throws<InvalidDataException>(() => Derive(selected, new(2026, 9, 26), [Ledger()], Entries(), [], []));
    }

    [Fact]
    public void FullSourceNameKeepsStrengthAndPackageSizeWithoutRedundantStrengthLabel()
    {
        var rows = Entries();
        var old = rows[0];
        rows[0] = new() { Id = old.Id, MedicationLedgerId = old.MedicationLedgerId, EntryOrdinal = old.EntryOrdinal,
            SourceStartPage = old.SourceStartPage, SourceEndPage = old.SourceEndPage, Status = old.Status,
            MedicationName = "ketoconazole topical (ketoconazole 2% cream [60g])", Strength = "60g", Directions = old.Directions };
        var result = Derive(Details(), new(2026, 9, 26), [Ledger()], rows, [], []);
        Assert.Contains("ketoconazole topical (ketoconazole 2% cream [60g])", result.Text);
        Assert.DoesNotContain("Documented strength: 60g", result.Text);
        Assert.Contains("\"Strength\":\"60g\"", result.Artifact.Metadata["prescriptionEvidence"].ToString());
    }

    [Fact]
    public void CurrentUseReconciliationRemainsInternalToPrescriptionPresentation()
    {
        var reconciliations =
            new[]
            {
                new MedicationCurrentUseReconciliation
                {
                    Id = new("current-use"),
                    VeteranId = new("veteran"),
                    MedicationLedgerEntryId = new("ledger-5"),
                    ReconciliationDate = new(2026, 9, 26),
                    CurrentUseStatus = MedicationCurrentUseStatuses.CurrentlyUsed,
                    Source = "VeteranReported"
                }
            };

        var result = Derive(
            Details(),
            new(2026, 9, 26),
            [Ledger()],
            Entries(),
            reconciliations,
            ["Lamotrigine"]);

        Assert.Contains("Claim-relevant prescriptions", result.Text);
        Assert.DoesNotContain("Claim-relevant psychiatric prescriptions", result.Text);
        Assert.DoesNotContain("Current-use reconciliation", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Veteran report", result.Text, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"Reconciliations\"", result.Artifact.Metadata["prescriptionEvidence"].ToString());
    }

    [Fact]
    public async Task SeptemberSourcePagesAndDerivedRowsAreFrozenMembersAndRerenderWithoutLiveEvidenceAccess()
    {
        var original = Details("osa-v4");
        var pageBytes = Enumerable.Range(3911, 13).Select(p => Encoding.UTF8.GetBytes($"September 9, 2026 VA source page {p}. Original source wording.")).ToArray();
        var pages = pageBytes.Select((bytes, i) => new PrintableArtifactPage { PageNumber = 3911 + i, ContentType = "text/plain", Content = bytes }).ToArray();
        var source = original.Artifacts.Single();
        var details = new VeteransReviewerPackageDetails {
            PackageDetails = new() { Package = original.PackageDetails.Package, Artifacts = [new() {
                EvidencePackageId = original.PackageDetails.Package.Id, ArtifactId = source.Id,
                ContentRole = EvidencePackageContentRoles.UnderlyingEvidence, ReviewerPageSelection = "3911-3923" }] },
            Artifacts = [source], ArtifactContents = [new() { Artifact = source, Text = "", PrintablePages = pages, ReviewerPageSelection = "3911-3923" }]
        };
        var ledger = new MedicationLedger { Id = new("september"), VeteranId = new("veteran"), SourceArtifactId = source.Id,
            ReportDate = new(2026, 9, 9), SourceStartPage = 3911, SourceEndPage = 3923, ParsedEntryCount = 1, ReportedEntryCount = 1, IsComplete = true };
        MedicationLedgerEntry[] rows = [new() { Id = new("bupropion"), MedicationLedgerId = ledger.Id, EntryOrdinal = 1,
            SourceStartPage = 3911, SourceEndPage = 3912, MedicationName = "buPROPion XL 300 mg/24 hour tablet", Strength = "300 mg/24 hour",
            Directions = "TAKE ONE TABLET ORALLY EVERY MORNING FOR MOOD.", Status = "active" }];
        var presentation = Derive(details, new(2026, 9, 26), [ledger], rows, [], ["Bupropion"]);
        var frozen = VeteransReviewerPackageSnapshot.Capture(VeteransReviewerPackagePrescriptionPresentation.Attach(details, presentation), []);
        var restored = VeteransReviewerPackageSnapshot.Restore(frozen);
        var member = restored.Details.PackageDetails.Artifacts.Single(a => a.ArtifactId == source.Id);
        Assert.Equal(EvidencePackageContentRoles.UnderlyingEvidence, member.ContentRole);
        Assert.Equal("3911-3923", member.ReviewerPageSelection);
        var storedSource = restored.Details.ArtifactContents.Single(a => a.Artifact.Id == source.Id);
        Assert.Equal(Enumerable.Range(3911, 13), storedSource.PrintablePages.Select(p => p.PageNumber));
        for (var i = 0; i < 13; i++) Assert.Equal(pageBytes[i], storedSource.PrintablePages[i].Content.ToArray());
        var storedPresentation = restored.Details.ArtifactContents.Single(a => a.Artifact.ArtifactType == VeteransReviewerPackagePrescriptionPresentation.ArtifactType);
        Assert.Equal(source.Id.Value, storedPresentation.Artifact.Metadata["sourceArtifactId"].ToString());
        Assert.Contains("2026-09-09", storedPresentation.Artifact.Metadata["prescriptionEvidence"].ToString());
        var repository = new SnapshotOnlyRepository(frozen);
        var output = new VeteransReviewerPackageDocumentOutputService(snapshotRepository: repository);
        var before = await output.RenderAsync(details, VeteransReviewerPackageOutputFormat.Docx);
        foreach (var bytes in pageBytes) Array.Fill(bytes, (byte)'X');
        rows[0] = new() { Id = rows[0].Id, MedicationLedgerId = ledger.Id, EntryOrdinal = 1, SourceStartPage = 3911, SourceEndPage = 3912,
            MedicationName = "LATER LIVE MEDICATION", Directions = "LATER LIVE DIRECTIONS", Status = "discontinued" };
        var poisoned = new VeteransReviewerPackageDetails { PackageDetails = details.PackageDetails, Artifacts = [], ArtifactContents = [] };
        var after = await output.RenderAsync(poisoned, VeteransReviewerPackageOutputFormat.Docx);
        var text = DocumentText(after.Docx!);
        Assert.Equal(DocumentText(before.Docx!), text);
        Assert.DoesNotContain("Additional Evidence", text);
        Assert.True(
            text.IndexOf("Appendix A — Medical Evidence", StringComparison.Ordinal) <
            text.IndexOf("VA source page 3911. Original source wording.", StringComparison.Ordinal));
        Assert.Equal(2, repository.Reads);
        Assert.Contains("VA source page 3911. Original source wording.", text);
        Assert.Contains("VA source page 3923. Original source wording.", text);
        Assert.Contains("buPROPion XL 300 mg/24 hour tablet", text);
        Assert.Contains("TAKE ONE TABLET ORALLY EVERY MORNING FOR MOOD.", text);
        Assert.DoesNotContain("LATER LIVE", text);
        Assert.DoesNotContain("Documented strength: 300 mg/24 hour", text);
    }

    [ReviewerLibreOfficeFact]
    public async Task LibreOffice_PrescriptionEntriesStayTogetherAndOversizedEntryPreservesOrder()
    {
        var details = Details();
        var ledger = new MedicationLedger
        {
            Id = new("pagination"), VeteranId = new("veteran"), SourceArtifactId = new("source"),
            ReportDate = new(2026, 9, 9), SourceStartPage = 10, SourceEndPage = 35,
            IsComplete = true, ParsedEntryCount = 26, ReportedEntryCount = 26
        };
        var oversizedMarkers = Enumerable.Range(0, 180).Select(i => $"LONGROW{i:D3}").ToArray();
        var entries = Enumerable.Range(0, 26).Select(i => new MedicationLedgerEntry
        {
            Id = new($"entry-{i}"), MedicationLedgerId = ledger.Id, EntryOrdinal = i + 1,
            SourceStartPage = 10 + i, SourceEndPage = 10 + i, Status = "active",
            MedicationName = $"MEDICATION{i:D2}", Strength = $"STRENGTH{i:D2}",
            Directions = i == 24
                ? string.Join(" ", oversizedMarkers.Select(m => m + " original source directions retained in order."))
                : $"DIRECTIONS{i:D2}: " + string.Join(" ", Enumerable.Repeat("Original prescription instructions.", 3 + i % 3))
        }).ToArray();
        var presentation = Derive(details, new(2026, 9, 26), [ledger], entries, [], []);
        var output = await new VeteransReviewerPackageDocumentOutputService(
            new LibreOfficeVeteransReviewerPackageDocumentConverter()).RenderAsync(
                VeteransReviewerPackagePrescriptionPresentation.Attach(details, presentation),
                VeteransReviewerPackageOutputFormat.Both);
        if (Environment.GetEnvironmentVariable("EMF_REVIEWER_LAYOUT_ARTIFACTS") is { Length: > 0 } artifactDirectory)
        {
            Directory.CreateDirectory(artifactDirectory);
            await File.WriteAllBytesAsync(Path.Combine(artifactDirectory, "Prescription_Pagination.docx"), output.Docx!);
            await File.WriteAllBytesAsync(Path.Combine(artifactDirectory, "Prescription_Pagination.pdf"), output.Pdf!);
        }
        using var docx = WordprocessingDocument.Open(new MemoryStream(output.Docx!), false);
        Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(docx));
        using var pdf = UglyToad.PdfPig.PdfDocument.Open(output.Pdf!);
        var pages = pdf.GetPages().ToArray();
        Assert.True(pages.Count(p => p.Text.Contains("MEDICATION")) > 2);
        foreach (var i in Enumerable.Range(0, 26).Where(i => i != 24))
        {
            var page = Assert.Single(pages.Where(p => p.Text.Contains($"MEDICATION{i:D2}")));
            Assert.Contains($"STRENGTH{i:D2}", page.Text);
            Assert.Contains($"DIRECTIONS{i:D2}", page.Text);
            Assert.Contains($"pages {10 + i}–{10 + i}", page.Text);
        }
        var firstOversizedPage = Assert.Single(pages.Where(p => p.Text.Contains("MEDICATION24")));
        var lastOversizedPage = Assert.Single(pages.Where(p => p.Text.Contains("pages 34–34")));
        Assert.True(lastOversizedPage.Number > firstOversizedPage.Number);
        var text = string.Join(" ", pages.Select(p => p.Text));
        var previous = -1;
        foreach (var marker in entries.Take(24).Select(e => e.MedicationName)
                     .Concat(new[] { "MEDICATION24" }).Concat(oversizedMarkers)
                     .Concat(new[] { "pages 34–34", "MEDICATION25" }))
        {
            var offset = text.IndexOf(marker, StringComparison.Ordinal);
            Assert.True(offset > previous, $"Missing or reordered marker: {marker}");
            Assert.Equal(offset, text.LastIndexOf(marker, StringComparison.Ordinal));
            previous = offset;
        }
    }

    private sealed class SnapshotOnlyRepository(ReviewerPackageSnapshot snapshot) : IEvidencePackageRepository
    {
        public int Reads { get; private set; }
        public Task<ReviewerPackageSnapshot?> GetReviewerSnapshotAsync(EvidencePackageId id, CancellationToken cancellationToken = default)
        { Assert.Equal(snapshot.PackageId, id); Reads++; return Task.FromResult<ReviewerPackageSnapshot?>(snapshot); }
        public Task AddEvidencePackageAsync(EvidencePackage package, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected live package write.");
        public Task<EvidencePackage?> GetEvidencePackageAsync(EvidencePackageId id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected live package query.");
        public Task<IReadOnlyList<EvidencePackage>> GetEvidencePackagesAsync(ClaimIssueId id, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected live package query.");
    }

    internal static string DocumentText(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        using var document = WordprocessingDocument.Open(stream, false);
        return document.MainDocumentPart!.Document!.Body!.InnerText;
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// A generated organizational artifact, frozen by the existing V1 package
// snapshot. No veteran-wide state and no live medication lookup during rendering.
public static class VeteransReviewerPackagePrescriptionPresentation
{
    public const string ArtifactType = "reviewer-package-va-prescriptions";

    public static VeteransReviewerArtifactContent Derive(
        VeteransReviewerPackageDetails package, DateOnly evidenceCutoff,
        IReadOnlyList<MedicationLedger> ledgers, IReadOnlyList<MedicationLedgerEntry> entries,
        IReadOnlyList<MedicationCurrentUseReconciliation> reconciliations,
        IReadOnlyList<string> priorityMedicationNames, DateTimeOffset preparedUtc)
    {
        if (evidenceCutoff > DateOnly.FromDateTime(preparedUtc.UtcDateTime))
            throw new InvalidDataException("Prescription evidence cutoff cannot follow package preparation.");
        var scope = new VeteransReviewerPackageEvidenceScope(package);
        var eligible = ledgers.Where(l => l.IsComplete && l.ReportDate <= evidenceCutoff &&
            scope.ArtifactIds.Contains(l.SourceArtifactId)).ToArray();
        if (eligible.Length == 0) throw new InvalidDataException("No complete prescription evidence is available within this package cutoff.");
        var newest = eligible.Where(l => l.ReportDate == eligible.Max(x => x.ReportDate)).ToArray();
        if (newest.Length != 1) throw new InvalidDataException("Ambiguous prescription evidence at the package cutoff.");
        var ledger = newest[0];
        if (eligible.Any(l => l.VeteranId != ledger.VeteranId))
            throw new InvalidDataException("Prescription evidence has conflicting veteran identities.");
        var rows = entries.Where(e => e.MedicationLedgerId == ledger.Id).OrderBy(e => e.EntryOrdinal).ToArray();
        if (rows.Length != ledger.ParsedEntryCount || (ledger.ReportedEntryCount is { } count && count != rows.Length) ||
            rows.Select(e => e.Id).Distinct().Count() != rows.Length ||
            rows.Select(e => e.EntryOrdinal).Distinct().Count() != rows.Length ||
            rows.Any(e => !MedicationLedgerStatuses.IsKnown(e.Status) || e.SourceStartPage < ledger.SourceStartPage ||
                e.SourceEndPage > ledger.SourceEndPage || e.SourceEndPage < e.SourceStartPage))
            throw new InvalidDataException("Prescription evidence is incomplete or has invalid source lineage.");
        var applied = reconciliations.Where(r => r.ReconciliationDate <= evidenceCutoff && rows.Any(e => e.Id == r.MedicationLedgerEntryId))
            .GroupBy(r => r.MedicationLedgerEntryId).Select(g =>
            {
                var latest = g.Where(r => r.ReconciliationDate == g.Max(x => x.ReconciliationDate)).ToArray();
                if (latest.Length != 1 || latest[0].VeteranId != ledger.VeteranId ||
                    !MedicationCurrentUseStatuses.IsSupported(latest[0].CurrentUseStatus))
                    throw new InvalidDataException("Prescription reconciliation is ambiguous or has invalid identity/status.");
                return latest[0];
            }).OrderBy(r => r.MedicationLedgerEntryId.Value, StringComparer.Ordinal).ToArray();
        var current = rows.Where(e => MedicationLedgerStatuses.IsCurrent(e.Status)).ToArray();
        if (current.Any(e => !scope.Contains(ledger.SourceArtifactId, e.SourceStartPage, e.SourceEndPage)))
            throw new InvalidDataException("The package does not include the source pages supporting its prescription list.");
        var selected = current.Where(e => !applied.Any(r => r.MedicationLedgerEntryId == e.Id &&
            !MedicationCurrentUseStatuses.IsCurrentlyUsed(r.CurrentUseStatus))).ToArray();
        int Priority(MedicationLedgerEntry entry)
        {
            for (var i = 0; i < priorityMedicationNames.Count; i++)
                if (Regex.IsMatch(entry.MedicationName, @"^" + Regex.Escape(priorityMedicationNames[i]) + @"\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) return i;
            return int.MaxValue;
        }
        selected = selected.OrderBy(Priority).ThenBy(e => e.MedicationName, StringComparer.OrdinalIgnoreCase).ThenBy(e => e.EntryOrdinal).ToArray();
        var source = package.Artifacts.Single(a => a.Id == ledger.SourceArtifactId);
        var date = ledger.ReportDate.ToString("MMMM d, yyyy", CultureInfo.InvariantCulture);
        var title = $"VA Prescription List — {date}";
        var lines = new List<string> { title,
            $"Source: {source.Name}; original pages {ledger.SourceStartPage}–{ledger.SourceEndPage}. Package evidence cutoff: {evidenceCutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}.",
            $"This section reflects prescriptions VA listed as active or refill-in-process as of {date}. " +
            "Prescription status does not establish actual use on the package preparation date." };
        if (!selected.Any(e => applied.Any(r => r.MedicationLedgerEntryId == e.Id && MedicationCurrentUseStatuses.IsCurrentlyUsed(r.CurrentUseStatus))))
            lines.Add("The available evidence contains no positive current-use reconciliation confirming use of the listed medications on the package preparation date.");
        if (current.Length != selected.Length)
            lines.Add($"{current.Length - selected.Length} prescription(s) excluded based on recorded non-use reconciliations available by the package cutoff; the source evidence is unchanged.");
        foreach (var group in selected.GroupBy(e => Priority(e) != int.MaxValue))
        {
            lines.Add(group.Key ? "Claim-relevant psychiatric prescriptions" : "Other VA prescriptions");
            foreach (var entry in group)
            {
                lines.Add(entry.MedicationName);
                // Preserve the full source name, which already carries strengths and
                // package sizes. Do not relabel a repeated package size as a dose.
                if (!string.IsNullOrWhiteSpace(entry.Strength) &&
                    !entry.MedicationName.Contains(entry.Strength, StringComparison.OrdinalIgnoreCase))
                    lines.Add($"Documented strength: {entry.Strength}");
                if (!string.IsNullOrWhiteSpace(entry.Directions)) lines.Add($"Directions: {entry.Directions}");
                lines.Add($"VA status: {entry.Status}. Source date: {date}; pages {entry.SourceStartPage}–{entry.SourceEndPage}.");
            }
        }
        var text = string.Join("\n", lines);
        var metadata = new Dictionary<string, object> {
            ["packageId"] = package.PackageDetails.Package.Id.Value,
            ["evidenceCutoff"] = evidenceCutoff.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["packagePreparedUtc"] = preparedUtc.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
            ["sourceArtifactId"] = ledger.SourceArtifactId.Value,
            ["sourceFingerprint"] = source.Fingerprint?.Value ?? "",
            ["prescriptionEvidence"] = JsonSerializer.Serialize(new { Ledger = ledger, Entries = selected, Reconciliations = applied,
                PriorityMedicationNames = priorityMedicationNames.ToArray() })
        };
        // Metadata is part of the derived identity: two packages/cutoffs must
        // not collide even when they happen to display the same prescriptions.
        var identity = text + "\n" + JsonSerializer.Serialize(metadata);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
        return new() { Artifact = new() { Id = new("sha256:" + hash), Name = title, ArtifactType = ArtifactType,
            CreatedUtc = preparedUtc.ToUniversalTime(), Metadata = metadata,
            Fingerprint = new() { Algorithm = "SHA-256", Value = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))) } }, Text = text };
    }

    internal static void ValidateFrozen(VeteransReviewerPackageDetails details, VeteransReviewerArtifactContent presentation)
    {
        var metadata = presentation.Artifact.Metadata;
        if (!metadata.TryGetValue("packageId", out var packageId) || packageId.ToString() != details.PackageDetails.Package.Id.Value ||
            !metadata.TryGetValue("sourceArtifactId", out var sourceId) ||
            !new VeteransReviewerPackageEvidenceScope(details).ArtifactIds.Contains(new ArtifactId(sourceId.ToString()!)) ||
            presentation.Artifact.Fingerprint is not { Algorithm: "SHA-256" } fingerprint ||
            fingerprint.Value != Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(presentation.Text))))
            throw new InvalidDataException("Frozen prescription presentation binding or content integrity is invalid.");
    }

    public static VeteransReviewerPackageDetails Attach(VeteransReviewerPackageDetails details, VeteransReviewerArtifactContent presentation)
    {
        ValidateFrozen(details, presentation);
        if (presentation.Artifact.ArtifactType != ArtifactType ||
            presentation.Artifact.Metadata["packageId"].ToString() != details.PackageDetails.Package.Id.Value ||
            details.ArtifactContents.Any(c => c.Artifact.ArtifactType == ArtifactType))
            throw new InvalidDataException("Prescription presentation must be bound once to its own package.");
        return new() {
            PackageDetails = new() { Package = details.PackageDetails.Package, Artifacts = [..details.PackageDetails.Artifacts,
                new() { EvidencePackageId = details.PackageDetails.Package.Id, ArtifactId = presentation.Artifact.Id,
                    ContentRole = EvidencePackageContentRoles.GeneratedOrganizationalMaterial }] },
            Artifacts = [..details.Artifacts, presentation.Artifact], ArtifactContents = [..details.ArtifactContents, presentation],
            CurrentPrescribedMedications = details.CurrentPrescribedMedications, CurrentMedications = details.CurrentMedications,
            MedicationProgressions = details.MedicationProgressions, MedicationClinicalContexts = details.MedicationClinicalContexts,
            SourceClarifications = details.SourceClarifications, ClinicalProgressionEvents = details.ClinicalProgressionEvents,
            MedicalOpinionRequested = details.MedicalOpinionRequested, VeteranDisplayName = details.VeteranDisplayName, PackagePreparedBy = details.PackagePreparedBy
        };
    }
}

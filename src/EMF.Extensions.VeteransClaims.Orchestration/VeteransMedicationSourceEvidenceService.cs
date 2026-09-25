using System.Security.Cryptography;
using System.Text.RegularExpressions;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Services;
using EMF.Orchestration.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

/// <summary>Reads linked, fingerprint-verified source pages locally; never infers provenance from a filename or Rx format.</summary>
public sealed class VeteransMedicationSourceEvidenceService(
    IEvidenceRepository evidence,
    IArtifactContentStore content,
    PdfArtifactTextExtractionProvider pdfTextExtractor)
{
    public async Task<IReadOnlyDictionary<MedicationLedgerEntryId, MedicationSourceEvidence>> GetAsync(
        MedicationLedger ledger, IReadOnlyList<MedicationLedgerEntry> entries, CancellationToken cancellationToken = default)
    {
        var unresolved = entries.Where(e => !MedicationLedgerSource.IsExplicitNonVa(e) &&
            !MedicationLedgerSource.IsVaPrescription(e)).ToArray();
        if (unresolved.Length == 0) return new Dictionary<MedicationLedgerEntryId, MedicationSourceEvidence>();

        var artifact = await evidence.GetArtifactAsync(ledger.SourceArtifactId, cancellationToken);
        var bytes = await content.ReadAsync(ledger.SourceArtifactId, cancellationToken);
        if (artifact is null || bytes is null) return new Dictionary<MedicationLedgerEntryId, MedicationSourceEvidence>();
        if (!VerifySourceArtifact(artifact, bytes, ledger))
            return new Dictionary<MedicationLedgerEntryId, MedicationSourceEvidence>();

        var result = new Dictionary<MedicationLedgerEntryId, MedicationSourceEvidence>();
        foreach (var entry in unresolved)
        {
            if (entry.MedicationLedgerId != ledger.Id)
                throw new InvalidDataException("Medication source ledger identity mismatch.");
            if (string.IsNullOrWhiteSpace(entry.PrescriptionNumber) ||
                entry.SourceStartPage < ledger.SourceStartPage ||
                entry.SourceEndPage > ledger.SourceEndPage ||
                entry.SourceStartPage < 1 ||
                entry.SourceEndPage < entry.SourceStartPage)
                continue;

            var text = await pdfTextExtractor.ExtractPageRangeTextAsync(
                ledger.SourceArtifactId,
                entry.SourceStartPage,
                entry.SourceEndPage,
                cancellationToken);

            if (string.IsNullOrWhiteSpace(text)) continue;

            var sourceEvidence = ReadVerifiedRecord(artifact, ledger, entry, text);
            if (sourceEvidence is not null)
                result[entry.Id] = sourceEvidence;
        }

        return result;
    }

    internal static bool VerifySourceArtifact(
        Artifact artifact, byte[] bytes, MedicationLedger ledger)
    {
        if (artifact.Id != ledger.SourceArtifactId)
            throw new InvalidDataException("Medication source artifact identity mismatch.");
        if (artifact.Fingerprint is not { } fingerprint ||
            !string.Equals(fingerprint.Algorithm.Replace("-", ""), "SHA256", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!string.Equals(Convert.ToHexString(SHA256.HashData(bytes)), fingerprint.Value, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Medication source artifact fingerprint mismatch.");
        return bytes.AsSpan().StartsWith("%PDF-"u8);
    }

    internal static MedicationSourceEvidence? ReadVerifiedRecord(
        Artifact artifact, MedicationLedger ledger, MedicationLedgerEntry entry, string text)
    {
        if (entry.MedicationLedgerId != ledger.Id)
            throw new InvalidDataException("Medication source ledger identity mismatch.");
        if (string.IsNullOrWhiteSpace(entry.PrescriptionNumber)) return null;
        if (!Regex.IsMatch(text, @"Report\s+generated\s+by\s+My\s+HealtheVet\s+on\s+VA\.gov\b", RegexOptions.IgnoreCase))
            return null;

        var rx = Regex.Matches(text,
            @"Prescription\s+number:\s*" + Regex.Escape(entry.PrescriptionNumber.Trim()) + @"(?=\s|$)",
            RegexOptions.IgnoreCase);
        if (rx.Count != 1) return null;

        var start = text.LastIndexOf("About your prescription", rx[0].Index, StringComparison.OrdinalIgnoreCase);
        if (start < 0) return null;
        var end = text.IndexOf("About your prescription", rx[0].Index + rx[0].Length, StringComparison.OrdinalIgnoreCase);
        var record = text[start..(end < 0 ? text.Length : end)];

        if (!Regex.IsMatch(record, @"Status:\s*" + Regex.Escape(entry.Status.Trim()) + @"(?=\s|$)", RegexOptions.IgnoreCase) ||
            !Regex.IsMatch(record, @"Prescribed[ \t]+(?:on|by):[ \t]*[^\s]", RegexOptions.IgnoreCase))
            return null;

        var nonVa = Regex.IsMatch(record,
            @"(?:Facility|Prescribed by|Source):[^\r\n]*\bnon[\s-]*VA\b",
            RegexOptions.IgnoreCase);

        return new MedicationSourceEvidence(
            !nonVa,
            nonVa,
            $"Verified VA prescription report {artifact.Id.Value}, source pages {entry.SourceStartPage}–{entry.SourceEndPage}, prescription {entry.PrescriptionNumber}.");
    }
}

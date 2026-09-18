using System.Text.RegularExpressions;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackageMedicationProgressionService
{
    private static readonly HashSet<string> NonIdentityTokens =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "hcl",
            "hydrochloride",
            "er",
            "xr",
            "xl",
            "sr",
            "dr",
            "tab",
            "tabs",
            "tablet",
            "tablets",
            "cap",
            "caps",
            "capsule",
            "capsules"
        };

    private static readonly Regex TokenSeparator =
        new("[^a-z0-9]+", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static readonly Regex Whitespace =
        new("\\s+", RegexOptions.Compiled);

    private static readonly Regex TrailingRefills =
        new(
            @"(?:\.\s*)?Refills:\s*\d+\.\s*$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly IClaimIssueRepository _issues;
    private readonly IClaimRepository _claims;
    private readonly IServiceConnectionRepository _connections;
    private readonly IMedicationRepository _medications;

    public VeteransReviewerPackageMedicationProgressionService(
        IClaimIssueRepository issues,
        IClaimRepository claims,
        IServiceConnectionRepository connections,
        IMedicationRepository medications)
    {
        ArgumentNullException.ThrowIfNull(issues);
        ArgumentNullException.ThrowIfNull(claims);
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(medications);

        _issues = issues;
        _claims = claims;
        _connections = connections;
        _medications = medications;
    }

    public async Task<IReadOnlyList<VeteransReviewerMedicationProgression>>
        GetAsync(
            EvidencePackage package,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (package.ServiceConnectionBasisId is null)
            return [];

        var issue =
            await _issues.GetClaimIssueAsync(
                package.ClaimIssueId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Reviewer package claim issue was not found.");

        if (issue.Id != package.ClaimIssueId)
            throw new InvalidOperationException(
                "Reviewer package claim issue identity mismatch.");

        var basis =
            await _connections.GetServiceConnectionBasisAsync(
                package.ServiceConnectionBasisId.Value,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Reviewer package service-connection basis was not found.");

        if (basis.Id != package.ServiceConnectionBasisId.Value ||
            basis.ClaimIssueId != package.ClaimIssueId)
        {
            throw new InvalidOperationException(
                "Reviewer package service-connection basis lineage mismatch.");
        }

        var relevantNames =
            (await _connections.GetPrescribedMedicationNamesAsync(
                basis.Id,
                cancellationToken))
            .Select(name => name.Trim())
            .Where(name => name.Length > 0)
            .GroupBy(
                MedicationIdentityKey,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        if (relevantNames.Length == 0)
            return [];

        var claim =
            await _claims.GetClaimAsync(
                issue.ClaimId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Reviewer package claim was not found.");

        if (claim.Id != issue.ClaimId)
            throw new InvalidOperationException(
                "Reviewer package claim identity mismatch.");

        var completeLedgers =
            (await _medications.GetMedicationLedgersAsync(
                claim.VeteranId,
                cancellationToken))
            .Where(ledger => ledger.IsComplete)
            .ToArray();

        if (completeLedgers.Length == 0)
            return [];

        var snapshots =
            new List<(MedicationLedger Ledger, MedicationLedgerEntry Entry)>();

        foreach (var ledger in completeLedgers)
        {
            if (ledger.VeteranId != claim.VeteranId)
                throw new InvalidDataException(
                    "Medication ledger veteran identity mismatch.");

            var entries =
                await _medications.GetMedicationLedgerEntriesAsync(
                    ledger.Id,
                    cancellationToken);

            ValidateLedgerEntries(ledger, entries);

            snapshots.AddRange(
                entries.Select(entry => (ledger, entry)));
        }

        var distinctSnapshots =
            snapshots
                .GroupBy(
                    snapshot => MedicationSnapshotKey(snapshot.Entry),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                    group
                        .OrderByDescending(snapshot => snapshot.Ledger.ReportDate)
                        .ThenByDescending(snapshot => snapshot.Entry.EntryOrdinal)
                        .First())
                .ToArray();

        var result =
            new List<VeteransReviewerMedicationProgression>();

        foreach (var relevantName in relevantNames)
        {
            var matching =
                distinctSnapshots
                    .Where(snapshot =>
                        MedicationNamesMatch(
                            relevantName,
                            snapshot.Entry.MedicationName))
                    .OrderBy(snapshot =>
                        snapshot.Entry.PrescribedDate ??
                        snapshot.Entry.LastFilledDate ??
                        DateOnly.MinValue)
                    .ThenBy(snapshot => snapshot.Ledger.ReportDate)
                    .ThenBy(snapshot => snapshot.Entry.EntryOrdinal)
                    .Select(snapshot => snapshot.Entry)
                    .ToArray();

            if (matching.Length == 0)
                continue;

            result.Add(
                new VeteransReviewerMedicationProgression
                {
                    MedicationName = relevantName,
                    Entries = SelectMeaningfulEntries(matching)
                });
        }

        return result
            .OrderBy(
                item => item.MedicationName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static void ValidateLedgerEntries(
        MedicationLedger ledger,
        IReadOnlyList<MedicationLedgerEntry> entries)
    {
        if (entries.Count != ledger.ParsedEntryCount)
            throw new InvalidDataException(
                "Medication ledger changed while reviewer progression was being prepared.");

        if (ledger.ReportedEntryCount is not null &&
            ledger.ReportedEntryCount.Value != entries.Count)
        {
            throw new InvalidDataException(
                "Complete medication ledger reported-entry count does not match persisted entries.");
        }

        if (entries.Any(entry => entry.MedicationLedgerId != ledger.Id))
            throw new InvalidDataException(
                "Medication ledger entry identity mismatch.");
    }

    private static IReadOnlyList<MedicationLedgerEntry>
        SelectMeaningfulEntries(
            IReadOnlyList<MedicationLedgerEntry> ordered)
    {
        if (ordered.Count == 0)
            return [];

        var selected = new List<MedicationLedgerEntry>();
        MedicationLedgerEntry? previousTherapy = null;

        foreach (var entry in ordered)
        {
            var therapyChanged =
                previousTherapy is null ||
                !SameTherapy(previousTherapy, entry);

            var preserveStatusTransition =
                string.Equals(
                    entry.Status.Trim(),
                    MedicationLedgerStatuses.Transferred,
                    StringComparison.OrdinalIgnoreCase) ||
                MedicationLedgerStatuses.IsCurrent(entry.Status);

            if (selected.Count == 0 ||
                therapyChanged ||
                preserveStatusTransition)
            {
                selected.Add(entry);
            }

            if (therapyChanged)
                previousTherapy = entry;
        }

        var terminal = ordered[^1];
        if (!selected.Contains(terminal))
            selected.Add(terminal);

        return selected;
    }

    private static bool SameTherapy(
        MedicationLedgerEntry first,
        MedicationLedgerEntry second) =>
        string.Equals(
            NormalizeValue(first.Strength),
            NormalizeValue(second.Strength),
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            NormalizeDirections(first.Directions),
            NormalizeDirections(second.Directions),
            StringComparison.OrdinalIgnoreCase);

    private static string MedicationSnapshotKey(
        MedicationLedgerEntry entry)
    {
        var medication =
            MedicationIdentityKey(entry.MedicationName);
        var prescription =
            NormalizeValue(entry.PrescriptionNumber);

        if (prescription.Length > 0)
        {
            return string.Join(
                "|",
                "rx",
                medication,
                prescription,
                NormalizeValue(entry.Status),
                NormalizeValue(entry.Strength),
                NormalizeDirections(entry.Directions));
        }

        return string.Join(
            "|",
            "entry",
            medication,
            entry.PrescribedDate?.ToString("yyyy-MM-dd") ?? string.Empty,
            NormalizeValue(entry.Strength),
            NormalizeDirections(entry.Directions),
            NormalizeValue(entry.Indication),
            NormalizeValue(entry.Prescriber));
    }

    private static bool MedicationNamesMatch(
        string relevantName,
        string ledgerName)
    {
        var relevantTokens = IdentityTokens(relevantName);
        if (relevantTokens.Length == 0)
            return false;

        var ledgerTokens =
            IdentityTokens(ledgerName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return relevantTokens.All(ledgerTokens.Contains);
    }

    private static string MedicationIdentityKey(string value) =>
        string.Join(" ", IdentityTokens(value));

    private static string[] IdentityTokens(string value) =>
        TokenSeparator
            .Split(value.Trim().ToLowerInvariant())
            .Where(token => token.Length > 0)
            .Where(token => !token.Any(char.IsDigit))
            .Where(token => !NonIdentityTokens.Contains(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string NormalizeDirections(string? value)
    {
        var normalized = NormalizeValue(value);

        if (normalized.StartsWith(
                "See Instructions.",
                StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized["See Instructions.".Length..].Trim();
        }

        normalized = TrailingRefills.Replace(normalized, string.Empty).Trim();
        return Whitespace.Replace(normalized, " ");
    }

    private static string NormalizeValue(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : Whitespace.Replace(value.Trim(), " ");
}

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

        var authoritative =
            await new CurrentMedicationLedgerService(_medications)
                .GetAsync(
                    claim.VeteranId,
                    cancellationToken);

        if (authoritative is null)
            return [];

        var allEntries =
            await _medications.GetMedicationLedgerEntriesAsync(
                authoritative.Ledger.Id,
                cancellationToken);

        if (allEntries.Count != authoritative.Ledger.ParsedEntryCount)
            throw new InvalidDataException(
                "Medication ledger changed while reviewer progression was being prepared.");

        var result =
            new List<VeteransReviewerMedicationProgression>();

        foreach (var relevantName in relevantNames)
        {
            var matching =
                allEntries
                    .Where(entry =>
                        MedicationNamesMatch(
                            relevantName,
                            entry.MedicationName))
                    .OrderBy(entry =>
                        entry.PrescribedDate ??
                        entry.LastFilledDate ??
                        DateOnly.MinValue)
                    .ThenBy(entry => entry.EntryOrdinal)
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

using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class CurrentMedicationLedgerService
{
    private readonly IMedicationRepository _repository;

    public CurrentMedicationLedgerService(
        IMedicationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<CurrentMedicationLedgerSnapshot?> GetAsync(
        VeteranId veteranId,
        CancellationToken cancellationToken = default)
    {
        var ledgers =
            await _repository.GetMedicationLedgersAsync(
                veteranId,
                cancellationToken);

        foreach (var ledger in ledgers)
        {
            if (ledger.VeteranId != veteranId)
                throw new InvalidDataException(
                    "Medication ledger veteran identity mismatch.");
        }

        var complete =
            ledgers
                .Where(ledger => ledger.IsComplete)
                .ToArray();

        if (complete.Length == 0)
            return null;

        var latestDate =
            complete.Max(ledger => ledger.ReportDate);

        var latest =
            complete
                .Where(ledger => ledger.ReportDate == latestDate)
                .ToArray();

        if (latest.Length != 1)
            throw new InvalidDataException(
                $"Multiple complete medication ledgers exist for " +
                $"{latestDate:yyyy-MM-dd}.");

        var selected = latest[0];

        var entries =
            await _repository.GetMedicationLedgerEntriesAsync(
                selected.Id,
                cancellationToken);

        ValidateEntries(selected, entries);

        var current =
            entries
                .Where(entry => MedicationLedgerStatuses.IsCurrent(entry.Status))
                .OrderBy(
                    entry => entry.MedicationName,
                    StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.EntryOrdinal)
                .ToArray();

        return new CurrentMedicationLedgerSnapshot
        {
            Ledger = selected,
            Entries = current
        };
    }

    private static void ValidateEntries(
        MedicationLedger ledger,
        IReadOnlyList<MedicationLedgerEntry> entries)
    {
        if (entries.Count != ledger.ParsedEntryCount)
            throw new InvalidDataException(
                "Medication ledger parsed-entry count does not match " +
                "persisted entries.");

        if (ledger.ReportedEntryCount is not null &&
            ledger.ReportedEntryCount.Value != entries.Count)
        {
            throw new InvalidDataException(
                "Complete medication ledger reported-entry count does not " +
                "match persisted entries.");
        }

        var ordinals = new HashSet<int>();

        foreach (var entry in entries)
        {
            if (entry.MedicationLedgerId != ledger.Id)
                throw new InvalidDataException(
                    "Medication ledger entry identity mismatch.");

            if (!ordinals.Add(entry.EntryOrdinal))
                throw new InvalidDataException(
                    "Medication ledger contains duplicate entry ordinals.");

            if (string.IsNullOrWhiteSpace(entry.Status) ||
                !MedicationLedgerStatuses.IsKnown(entry.Status))
            {
                throw new InvalidDataException(
                    $"Medication ledger contains unrecognized status " +
                    $"'{entry.Status}'.");
            }
        }
    }
}

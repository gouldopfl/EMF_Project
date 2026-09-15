using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class ReconciledCurrentMedicationLedgerService
{
    private readonly CurrentMedicationLedgerService _currentMedications;
    private readonly IMedicationRepository _repository;

    public ReconciledCurrentMedicationLedgerService(
        CurrentMedicationLedgerService currentMedications,
        IMedicationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(currentMedications);
        ArgumentNullException.ThrowIfNull(repository);

        _currentMedications = currentMedications;
        _repository = repository;
    }

    public async Task<CurrentMedicationLedgerSnapshot?> GetAsync(
        VeteranId veteranId,
        CancellationToken cancellationToken = default)
    {
        var snapshot =
            await _currentMedications.GetAsync(
                veteranId,
                cancellationToken);

        if (snapshot is null)
            return null;

        var reconciliations =
            await _repository.GetMedicationCurrentUseReconciliationsAsync(
                veteranId,
                cancellationToken);

        var latestByEntry =
            new Dictionary<MedicationLedgerEntryId, MedicationCurrentUseReconciliation>();

        foreach (var reconciliation in reconciliations)
        {
            ValidateReconciliation(veteranId, reconciliation);

            if (!latestByEntry.TryGetValue(
                    reconciliation.MedicationLedgerEntryId,
                    out var existing) ||
                reconciliation.ReconciliationDate > existing.ReconciliationDate)
            {
                latestByEntry[reconciliation.MedicationLedgerEntryId] =
                    reconciliation;
                continue;
            }

            if (reconciliation.ReconciliationDate ==
                existing.ReconciliationDate)
            {
                throw new InvalidDataException(
                    "Medication current-use reconciliation contains " +
                    "multiple records for the same ledger entry and date.");
            }
        }

        var entries =
            snapshot.Entries
                .Where(
                    entry =>
                        !latestByEntry.TryGetValue(entry.Id, out var reconciliation) ||
                        MedicationCurrentUseStatuses.IsCurrentlyUsed(
                            reconciliation.CurrentUseStatus))
                .ToArray();

        return new CurrentMedicationLedgerSnapshot
        {
            Ledger = snapshot.Ledger,
            Entries = entries
        };
    }

    private static void ValidateReconciliation(
        VeteranId veteranId,
        MedicationCurrentUseReconciliation reconciliation)
    {
        ArgumentNullException.ThrowIfNull(reconciliation);

        if (reconciliation.VeteranId != veteranId)
            throw new InvalidDataException(
                "Medication current-use reconciliation veteran identity mismatch.");

        if (!MedicationCurrentUseStatuses.IsSupported(
                reconciliation.CurrentUseStatus))
        {
            throw new InvalidDataException(
                $"Unsupported medication current-use status " +
                $"'{reconciliation.CurrentUseStatus}'.");
        }

        if (string.IsNullOrWhiteSpace(reconciliation.Source))
            throw new InvalidDataException(
                "Medication current-use reconciliation source is required.");
    }
}

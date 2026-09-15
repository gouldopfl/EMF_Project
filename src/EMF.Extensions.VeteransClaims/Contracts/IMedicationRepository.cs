using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Contracts;

public interface IMedicationRepository
{
    Task AddMedicationRecordAsync(
        MedicationRecord medicationRecord,
        CancellationToken cancellationToken = default);

    Task<MedicationRecord?> GetMedicationRecordAsync(
        MedicationRecordId medicationRecordId,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MedicationRecord>>
        GetMedicationRecordsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default);

    Task<IReadOnlyList<MedicationRecord>>
        GetMedicationRecordsAsync(
            VeteranId veteranId,
            string medicationName,
            CancellationToken cancellationToken = default);

    Task AddMedicationLedgerAsync(
        MedicationLedger medicationLedger,
        IReadOnlyCollection<MedicationLedgerEntry> entries,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<MedicationLedger?> GetMedicationLedgerAsync(
        MedicationLedgerId medicationLedgerId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<MedicationLedger>>
        GetMedicationLedgersAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<MedicationLedgerEntry>>
        GetMedicationLedgerEntriesAsync(
            MedicationLedgerId medicationLedgerId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task AddMedicationCurrentUseReconciliationAsync(
        MedicationCurrentUseReconciliation reconciliation,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<MedicationCurrentUseReconciliation>>
        GetMedicationCurrentUseReconciliationsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task AddMedicationClinicalContextAsync(
        MedicationClinicalContext clinicalContext,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task UpdateMedicationClinicalContextRecordTitleAsync(
        MedicationClinicalContextId medicationClinicalContextId,
        string recordTitle,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<MedicationClinicalContext>>
        GetMedicationClinicalContextsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
    Task AddMedicationHistoryEventAsync(
        MedicationHistoryEvent historyEvent,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<MedicationHistoryEvent>>
        GetMedicationHistoryEventsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    Task<IReadOnlyList<MedicationHistoryEvent>>
        GetMedicationHistoryEventsAsync(
            VeteranId veteranId,
            string medicationName,
            CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();
}

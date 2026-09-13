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

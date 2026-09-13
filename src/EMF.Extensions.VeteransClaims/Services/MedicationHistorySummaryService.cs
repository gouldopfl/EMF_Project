using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class MedicationHistorySummaryService
{
    private readonly IMedicationRepository _repository;

    public MedicationHistorySummaryService(
        IMedicationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<MedicationHistoryEvent?>
        GetEarliestDocumentedReleaseAsync(
            VeteranId veteranId,
            string medicationName,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(medicationName);

        var name = medicationName.Trim();

        var events =
            await _repository.GetMedicationHistoryEventsAsync(
                veteranId,
                name,
                cancellationToken);

        foreach (var historyEvent in events)
        {
            if (historyEvent.VeteranId != veteranId)
            {
                throw new InvalidDataException(
                    "Medication history veteran identity mismatch.");
            }

            if (!string.Equals(
                    historyEvent.MedicationName.Trim(),
                    name,
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidDataException(
                    "Medication history medication identity mismatch.");
            }
        }

        return events
            .Where(
                historyEvent =>
                    string.Equals(
                        historyEvent.EventType,
                        MedicationHistoryEventTypes.LastReleased,
                        StringComparison.Ordinal))
            .OrderBy(historyEvent => historyEvent.EventDate)
            .ThenBy(historyEvent => historyEvent.Id.Value, StringComparer.Ordinal)
            .FirstOrDefault();
    }
}

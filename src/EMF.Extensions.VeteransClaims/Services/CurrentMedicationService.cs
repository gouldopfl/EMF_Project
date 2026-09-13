using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class CurrentMedicationService
{
    private readonly IMedicationRepository _repository;

    public CurrentMedicationService(
        IMedicationRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);
        _repository = repository;
    }

    public async Task<IReadOnlyList<MedicationRecord>>
        GetCurrentMedicationsAsync(
            VeteranId veteranId,
            CancellationToken cancellationToken = default)
    {
        var records =
            await _repository.GetMedicationRecordsAsync(
                veteranId,
                cancellationToken);

        foreach (var record in records)
        {
            if (record.VeteranId != veteranId)
                throw new InvalidDataException(
                    "Medication record veteran identity mismatch.");
        }

        var current = new List<MedicationRecord>();

        foreach (var group in records.GroupBy(
            record => record.MedicationName.Trim(),
            StringComparer.OrdinalIgnoreCase))
        {
            var latestDate =
                group.Max(record => record.RecordDate);

            var latest =
                group.Where(
                        record => record.RecordDate == latestDate)
                    .ToArray();

            var distinct =
                latest
                    .GroupBy(BuildObservationKey)
                    .Select(item => item.First())
                    .ToArray();

            if (distinct.Length > 1)
                throw new InvalidDataException(
                    $"Medication '{group.Key}' has conflicting " +
                    $"observations on {latestDate:yyyy-MM-dd}.");

            var resolved = distinct[0];

            if (MedicationStatuses.IsCurrent(resolved.Status))
                current.Add(resolved);
        }

        return current
            .OrderBy(
                record => record.MedicationName,
                StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string BuildObservationKey(
        MedicationRecord record)
    {
        return string.Join(
            "\u001f",
            record.MedicationName.Trim().ToUpperInvariant(),
            record.Strength?.Trim().ToUpperInvariant() ?? "",
            record.Directions?.Trim().ToUpperInvariant() ?? "",
            record.Indication?.Trim().ToUpperInvariant() ?? "",
            record.Status.Trim().ToUpperInvariant());
    }
}

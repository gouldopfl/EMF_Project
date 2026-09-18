using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class MedicationClinicalContextLinkService
{
    private readonly IMedicationRepository _medications;

    public MedicationClinicalContextLinkService(
        IMedicationRepository medications)
    {
        ArgumentNullException.ThrowIfNull(medications);
        _medications = medications;
    }

    public async Task<IReadOnlyList<MedicationClinicalContextLink>>
        GetAsync(
            VeteranId veteranId,
            IReadOnlyCollection<MedicationLedgerEntry> medicationEntries,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(medicationEntries);

        var byPrescription =
            new Dictionary<string, MedicationLedgerEntry>(
                StringComparer.OrdinalIgnoreCase);

        foreach (var entry in medicationEntries)
        {
            ArgumentNullException.ThrowIfNull(entry);

            if (string.IsNullOrWhiteSpace(entry.PrescriptionNumber))
                continue;

            var prescriptionNumber = entry.PrescriptionNumber.Trim();

            if (byPrescription.TryGetValue(
                    prescriptionNumber,
                    out var existing))
            {
                if (!SameMedicationIdentity(existing, entry))
                {
                    throw new InvalidDataException(
                        "Medication progression contains conflicting " +
                        "medication identities for the same prescription " +
                        "number; clinical context linkage would be ambiguous.");
                }

                // Progression entries are supplied in chronological order.
                // Keep legitimate status/history snapshots and use the latest
                // equivalent snapshot for clinical-context projection.
                byPrescription[prescriptionNumber] = entry;
                continue;
            }

            byPrescription.Add(prescriptionNumber, entry);
        }

        if (byPrescription.Count == 0)
            return [];

        var contexts =
            await _medications.GetMedicationClinicalContextsAsync(
                veteranId,
                cancellationToken);

        return contexts
            .Where(context =>
                byPrescription.ContainsKey(
                    context.PrescriptionNumber.Trim()))
            .Select(context =>
                new MedicationClinicalContextLink
                {
                    Medication =
                        byPrescription[
                            context.PrescriptionNumber.Trim()],
                    Context = context
                })
            .OrderBy(link => link.Context.EventDate)
            .ThenBy(link => link.Context.SourceStartPage)
            .ThenBy(link => link.Context.Id.Value, StringComparer.Ordinal)
            .ToArray();
    }

    private static bool SameMedicationIdentity(
        MedicationLedgerEntry first,
        MedicationLedgerEntry second) =>
        string.Equals(
            Normalize(first.MedicationName),
            Normalize(second.MedicationName),
            StringComparison.OrdinalIgnoreCase) &&
        string.Equals(
            Normalize(first.Strength),
            Normalize(second.Strength),
            StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string? value) =>
        string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim();
}

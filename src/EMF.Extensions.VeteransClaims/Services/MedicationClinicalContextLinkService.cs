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

            if (!byPrescription.TryAdd(prescriptionNumber, entry))
            {
                throw new InvalidDataException(
                    "Medication progression contains duplicate " +
                    "prescription numbers; clinical context linkage " +
                    "would be ambiguous.");
            }
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
}

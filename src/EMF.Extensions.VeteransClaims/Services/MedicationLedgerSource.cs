using System.Text.RegularExpressions;
using EMF.Extensions.VeteransClaims.Models.Medications;

namespace EMF.Extensions.VeteransClaims.Services;

public static class MedicationLedgerSource
{
    public static bool IsExplicitNonVa(MedicationLedgerEntry entry) =>
        Regex.IsMatch(entry.MedicationName + " " + entry.Facility + " " + entry.Prescriber,
            @"\bnon[\s-]*VA\b", RegexOptions.IgnoreCase);

    /// <summary>Consider positive source evidence; an absent optional facility is not negative evidence.</summary>
    public static bool IsVaPrescription(MedicationLedgerEntry entry, MedicationSourceEvidence? source = null)
    {
        if (IsExplicitNonVa(entry) || source?.IsExplicitNonVa == true)
            return false;
        return !string.IsNullOrWhiteSpace(entry.PrescriptionNumber) &&
            (HasVaDesignation(entry.Facility) || source?.IsVaPrescriptionRecord == true);
    }

    private static bool HasVaDesignation(string? value) => Regex.IsMatch(value ?? string.Empty,
        @"\bVA\b|\bVAMC\b|\bVeterans[’']?\s+(Administration|Affairs)\b", RegexOptions.IgnoreCase);
}

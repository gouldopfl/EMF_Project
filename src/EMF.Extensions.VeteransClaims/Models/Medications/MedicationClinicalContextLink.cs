namespace EMF.Extensions.VeteransClaims.Models.Medications;

public sealed class MedicationClinicalContextLink
{
    public required MedicationLedgerEntry Medication { get; init; }

    public required MedicationClinicalContext Context { get; init; }
}

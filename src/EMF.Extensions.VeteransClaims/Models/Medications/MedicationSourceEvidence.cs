namespace EMF.Extensions.VeteransClaims.Models.Medications;

/// <summary>Corroboration from the linked source; never substitutes for original ledger wording.</summary>
public sealed record MedicationSourceEvidence(
    bool IsVaPrescriptionRecord,
    bool IsExplicitNonVa,
    string SourceLocator);

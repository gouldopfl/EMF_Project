namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed record VeteransReviewerPackageSection(
    string Id,
    string Heading);

public static class VeteransReviewerPackageSectionCatalog
{
    public const string ExecutiveSummary = "executive-summary";
    public const string PackageGuide = "package-guide";
    public const string MedicalReviewScope = "medical-review-scope";
    public const string PapAdherence = "pap-adherence";
    public const string PapTitration = "pap-titration";
    public const string ClinicalProgression = "clinical-progression";
    public const string MedicationProgression = "medication-progression";
    public const string CurrentMedications = "current-medications";
    public const string KeyEvidenceChronology = "key-evidence-chronology";
    public const string MedicalLiterature = "medical-literature";
    public const string ReviewerQuestions = "reviewer-questions";
    public const string EvidenceAppendices = "evidence-appendices";

    public static IReadOnlyList<VeteransReviewerPackageSection> All { get; } =
        new[]
        {
            new VeteransReviewerPackageSection(
                ExecutiveSummary,
                "Executive Summary"),
            new VeteransReviewerPackageSection(
                PackageGuide,
                "Package Guide"),
            new VeteransReviewerPackageSection(
                MedicalReviewScope,
                "Issues Presented for Medical Review"),
            new VeteransReviewerPackageSection(
                PapAdherence,
                "PAP Adherence / Compliance Summary"),
            new VeteransReviewerPackageSection(
                PapTitration,
                "Sleep Study / PAP Titration Results"),
            new VeteransReviewerPackageSection(
                ClinicalProgression,
                "Clinical Progression"),
            new VeteransReviewerPackageSection(
                MedicationProgression,
                "Relevant Medication Progression / History"),
            new VeteransReviewerPackageSection(
                CurrentMedications,
                "Current Medication Use — Reconciled"),
            new VeteransReviewerPackageSection(
                KeyEvidenceChronology,
                "Key Evidence and Chronology"),
            new VeteransReviewerPackageSection(
                MedicalLiterature,
                "Medical / Scientific Literature Considered"),
            new VeteransReviewerPackageSection(
                ReviewerQuestions,
                "Questions for the Reviewing Physician"),
            new VeteransReviewerPackageSection(
                EvidenceAppendices,
                "Evidence Appendices")
        };
}

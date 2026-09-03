using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public static class VeteransReviewerPackageAppendix
{
    public const string MedicalEvidence = "MedicalEvidence";
    public const string ServiceRecords = "ServiceRecords";
    public const string LayEvidence = "LayEvidence";
    public const string AdjudicativeRecords = "AdjudicativeRecords";

    public static string GetAppendix(
        string classification) =>
        classification switch
        {
            EvidenceClassifications.MedicalEvidence or
            EvidenceClassifications.Examination or
            EvidenceClassifications.MedicalOpinion =>
                MedicalEvidence,

            EvidenceClassifications.ServiceTreatmentRecord or
            EvidenceClassifications.ServiceRecord =>
                ServiceRecords,

            EvidenceClassifications.LayEvidence =>
                LayEvidence,

            EvidenceClassifications.AdjudicativeRecord =>
                AdjudicativeRecords,

            _ =>
                throw new ArgumentException(
                    $"Unsupported evidence classification '{classification}'.",
                    nameof(classification))
        };
}

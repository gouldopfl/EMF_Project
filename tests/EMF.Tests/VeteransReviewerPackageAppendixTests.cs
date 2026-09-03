using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerPackageAppendixTests
{
    [Theory]
    [InlineData(EvidenceClassifications.MedicalEvidence, "MedicalEvidence")]
    [InlineData(EvidenceClassifications.Examination, "MedicalEvidence")]
    [InlineData(EvidenceClassifications.MedicalOpinion, "MedicalEvidence")]
    [InlineData(EvidenceClassifications.ServiceTreatmentRecord, "ServiceRecords")]
    [InlineData(EvidenceClassifications.ServiceRecord, "ServiceRecords")]
    [InlineData(EvidenceClassifications.LayEvidence, "LayEvidence")]
    [InlineData(EvidenceClassifications.AdjudicativeRecord, "AdjudicativeRecords")]
    public void GetAppendix_MapsEvidenceClassification(
        string classification,
        string expected)
    {
        Assert.Equal(
            expected,
            VeteransReviewerPackageAppendix.GetAppendix(
                classification));
    }
}

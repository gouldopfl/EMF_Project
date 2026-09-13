using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class EvidenceRecognitionTermProposalAuditServiceTests
{
    [Fact]
    public void Audit_UsesCaseInsensitiveBoundaryAwareLiteralMatching()
    {
        var result = new EvidenceRecognitionTermProposalAuditService().Audit(
            [Proposal("sleep apnea")],
            [
                Record("Sleep note", "OBSTRUCTIVE SLEEP APNEA documented.", 10),
                Record("Other note", "No matching phrase.", 11)
            ]);

        var audit = Assert.Single(result.Audits);
        Assert.Equal(2, result.RecordCount);
        Assert.Equal(1, result.UniqueMatchingRecordCount);
        Assert.Equal(1, audit.MatchingRecordCount);
        Assert.Equal("Sleep note", Assert.Single(audit.Samples).Title);
    }


    [Fact]
    public void Audit_DoesNotMatchAcronymInsideLargerWord()
    {
        var result = new EvidenceRecognitionTermProposalAuditService().Audit(
            [Proposal("OSA")],
            [
                Record("Medication", "Medication dosage adjusted.", 12),
                Record("Sleep", "OSA treated with CPAP.", 13)
            ]);

        var audit = Assert.Single(result.Audits);
        Assert.Equal(1, audit.MatchingRecordCount);
        Assert.Equal(1, result.UniqueMatchingRecordCount);
        Assert.Equal("Sleep", Assert.Single(audit.Samples).Title);
    }

    [Fact]
    public void Audit_ReportsZeroHitProposal()
    {
        var result = new EvidenceRecognitionTermProposalAuditService().Audit(
            [Proposal("proximately due to coronary artery disease")],
            [Record("Sleep note", "OSA follow-up.", 20)]);

        var audit = Assert.Single(result.Audits);
        Assert.Equal(0, audit.MatchingRecordCount);
        Assert.Empty(audit.Samples);
        Assert.Equal(0, result.UniqueMatchingRecordCount);
    }

    [Fact]
    public void Audit_LimitsSamplesWithoutLimitingHitCount()
    {
        var records = Enumerable.Range(1, 5)
            .Select(index =>
                Record($"Note {index}", "CPAP compliance reviewed.", index))
            .ToArray();

        var result = new EvidenceRecognitionTermProposalAuditService().Audit(
            [Proposal("CPAP")],
            records,
            maximumSamples: 2);

        var audit = Assert.Single(result.Audits);
        Assert.Equal(5, audit.MatchingRecordCount);
        Assert.Equal(2, audit.Samples.Count);
        Assert.Equal(5, result.UniqueMatchingRecordCount);
    }

    [Fact]
    public void Audit_UnionCountsEachRecordOnceAcrossTerms()
    {
        var result = new EvidenceRecognitionTermProposalAuditService().Audit(
            [Proposal("OSA"), Proposal("CPAP")],
            [
                Record("Both", "OSA treated with CPAP.", 30),
                Record("OSA", "OSA diagnosis.", 31),
                Record("Neither", "Routine primary care.", 32)
            ]);

        Assert.Equal(2, result.UniqueMatchingRecordCount);
        Assert.Equal(2, result.Audits[0].MatchingRecordCount);
        Assert.Equal(1, result.Audits[1].MatchingRecordCount);
    }


    [Fact]
    public void Audit_ReportsDiagnosisAndRequirementSignalCooccurrence()
    {
        var result = new EvidenceRecognitionTermProposalAuditService().Audit(
            [
                Proposal(
                    "OSA",
                    EvidenceRecognitionRoles.Diagnosis),
                Proposal(
                    "proximately due to",
                    EvidenceRecognitionRoles.MedicalNexus)
            ],
            [
                Record(
                    "Qualified",
                    "OSA is proximately due to another condition.",
                    40),
                Record(
                    "Diagnosis only",
                    "OSA treated with CPAP.",
                    41),
                Record(
                    "Signal only",
                    "Condition is proximately due to another disease.",
                    42)
            ]);

        Assert.Equal(2, result.DiagnosisAnchorRecordCount);
        Assert.Equal(2, result.RequirementSignalRecordCount);
        Assert.Equal(1, result.QualifiedRecordCount);
        Assert.Equal(new[] { 0 }, result.QualifiedRecordIndexes);
        Assert.Equal(
            "Qualified",
            Assert.Single(result.QualifiedSamples).Title);
    }


    [Fact]
    public void Audit_DoesNotQualifyDistantSignalInSameRecord()
    {
        var lines =
            new List<string>
            {
                "Current severity is greater than baseline."
            };

        lines.AddRange(
            Enumerable.Repeat(
                "Unrelated questionnaire content.",
                40));

        lines.Add("OSA diagnosed.");

        var result = new EvidenceRecognitionTermProposalAuditService().Audit(
            [
                Proposal(
                    "OSA",
                    EvidenceRecognitionRoles.Diagnosis),
                Proposal(
                    "current severity",
                    EvidenceRecognitionRoles.Aggravation)
            ],
            [
                Record(
                    "Long C&P record",
                    string.Join(Environment.NewLine, lines),
                    60)
            ]);

        Assert.Equal(1, result.DiagnosisAnchorRecordCount);
        Assert.Equal(1, result.RequirementSignalRecordCount);
        Assert.Equal(0, result.QualifiedRecordCount);
        Assert.Empty(result.QualifiedRecordIndexes);
    }

    [Fact]
    public void Audit_QualifiesNearbySignalWithinDiagnosisWindow()
    {
        var result = new EvidenceRecognitionTermProposalAuditService().Audit(
            [
                Proposal(
                    "obstructive sleep apnea",
                    EvidenceRecognitionRoles.Diagnosis),
                Proposal(
                    "proximately due to",
                    EvidenceRecognitionRoles.MedicalNexus)
            ],
            [
                Record(
                    "OSA opinion",
                    string.Join(
                        Environment.NewLine,
                        "The claimed condition is less likely than not proximately due to the service connected condition.",
                        "Rationale follows.",
                        "Obstructive sleep apnea was diagnosed after testing."),
                    61)
            ]);

        Assert.Equal(1, result.DiagnosisAnchorRecordCount);
        Assert.Equal(1, result.RequirementSignalRecordCount);
        Assert.Equal(1, result.QualifiedRecordCount);
        Assert.Equal(new[] { 0 }, result.QualifiedRecordIndexes);
    }


    [Fact]
    public void Audit_ExcludesEvidenceTypeFromRequirementSignals()
    {
        var result = new EvidenceRecognitionTermProposalAuditService().Audit(
            [
                Proposal(
                    "OSA",
                    EvidenceRecognitionRoles.Diagnosis),
                Proposal(
                    "medical opinion",
                    EvidenceRecognitionRoles.EvidenceType)
            ],
            [
                Record(
                    "Document label only",
                    "OSA medical opinion reviewed.",
                    50)
            ]);

        Assert.Equal(1, result.DiagnosisAnchorRecordCount);
        Assert.Equal(0, result.RequirementSignalRecordCount);
        Assert.Equal(0, result.QualifiedRecordCount);
        Assert.Empty(result.QualifiedRecordIndexes);
        Assert.Empty(result.QualifiedSamples);
    }

    [Fact]
    public void Audit_RejectsNegativeSampleLimit()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new EvidenceRecognitionTermProposalAuditService().Audit(
                [],
                [],
                -1));
    }

    private static EvidenceRecognitionTermProposal Proposal(
        string term,
        string recognitionRole = EvidenceRecognitionRoles.MedicalNexus) =>
        new()
        {
            RequirementId = new RequirementId("requirement-1"),
            Term = term,
            TermType = "Phrase",
            RecognitionRole = recognitionRole,
            EvidenceClassification = null,
            AuthoritySource = "provision-1",
            Rationale = "Test proposal."
        };

    private static VeteransBlueButtonCareSummaryRecord Record(
        string title,
        string text,
        int page) =>
        new()
        {
            Title = title,
            DateEntered = "2026-01-01",
            SourceStartPage = page,
            SourceEndPage = page,
            NoteTitles = [],
            Text = text
        };
}

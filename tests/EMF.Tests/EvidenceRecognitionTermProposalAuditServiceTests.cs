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
        Assert.Empty(result.QualifiedWindows);
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

        var window = Assert.Single(result.QualifiedWindows);
        Assert.Equal(0, window.RecordIndex);
        Assert.Equal("OSA opinion", window.Title);
        Assert.Contains(
            "proximately due to",
            window.Text,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "Obstructive sleep apnea",
            window.Text,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Audit_ReturnsOnlyBoundedTextForQualifiedWindow()
    {
        var lines =
            new List<string>
            {
                "Distant material that must not be exported."
            };

        lines.AddRange(
            Enumerable.Repeat(
                "Unrelated content.",
                20));

        lines.Add("OSA diagnosed.");
        lines.Add("Condition is proximately due to another condition.");

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
                    "Bounded opinion",
                    string.Join(Environment.NewLine, lines),
                    62)
            ]);

        var window = Assert.Single(result.QualifiedWindows);
        Assert.DoesNotContain(
            "Distant material",
            window.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "OSA diagnosed",
            window.Text,
            StringComparison.Ordinal);
        Assert.Contains(
            "proximately due to",
            window.Text,
            StringComparison.Ordinal);
    }


    [Fact]
    public void Audit_UsesStructuredOpinionBoundaryToExcludePriorOpinion()
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
                    "Multiple opinions",
                    string.Join(
                        Environment.NewLine,
                        "RESTATEMENT OF REQUESTED OPINION:",
                        "Diabetic peripheral neuropathy is proximately due to diabetes.",
                        "***************",
                        "RESTATEMENT OF REQUESTED OPINION:",
                        "Is obstructive sleep apnea proximately due to coronary artery disease?",
                        "Rationale: obstructive sleep apnea is less likely than not proximately due to coronary artery disease.",
                        "***************"),
                    63)
            ]);

        var window = Assert.Single(result.QualifiedWindows);
        Assert.DoesNotContain(
            "peripheral neuropathy",
            window.Text,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "obstructive sleep apnea",
            window.Text,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "coronary artery disease",
            window.Text,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Audit_ExtendsStructuredOpinionThroughDistantConclusion()
    {
        var lines = new List<string>
        {
            "2A. TYPE OF MEDICAL OPINION REQUESTED: Secondary Service connection.",
            "The claimed condition is less likely than not proximately due to the service connected condition."
        };

        lines.AddRange(Enumerable.Repeat("Evidence review detail.", 20));
        lines.Add("Obstructive sleep apnea diagnosed by sleep study.");
        lines.AddRange(Enumerable.Repeat("Medical literature discussion.", 24));
        lines.Add("Secondary nexus not established after thorough review of evidence.");
        lines.Add("11A. Examiner's signature: /es/ Examiner");

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
                    "Long OSA addendum",
                    string.Join(Environment.NewLine, lines),
                    64)
            ]);

        var window = Assert.Single(result.QualifiedWindows);
        Assert.Contains(
            "Secondary nexus not established",
            window.Text,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "Examiner's signature",
            window.Text,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Audit_DoesNotUseTemplateSignalOutsideStructuredOpinionSection()
    {
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
                    "OSA addendum",
                    string.Join(
                        Environment.NewLine,
                        "III. Is the current severity greater than the baseline? [No Response Provided]",
                        "2A. TYPE OF MEDICAL OPINION REQUESTED: Secondary Service connection.",
                        "OSA diagnosed after sleep testing.",
                        "Secondary nexus not established.",
                        "11A. Examiner's signature: /es/ Examiner"),
                    65)
            ]);

        Assert.Equal(1, result.DiagnosisAnchorRecordCount);
        Assert.Equal(1, result.RequirementSignalRecordCount);
        Assert.Equal(0, result.QualifiedRecordCount);
        Assert.Empty(result.QualifiedWindows);
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
        Assert.Empty(result.QualifiedWindows);
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

using EMF.Extensions.VeteransClaims.Services;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class EvidenceRecognitionTermProposalAuditSample
{
    public required string Title { get; init; }

    public required string DateEntered { get; init; }

    public required int SourceStartPage { get; init; }

    public required int SourceEndPage { get; init; }
}

public sealed class EvidenceRecognitionTermProposalAudit
{
    public required EvidenceRecognitionTermProposal Proposal { get; init; }

    public required int MatchingRecordCount { get; init; }

    public required IReadOnlyList<EvidenceRecognitionTermProposalAuditSample>
        Samples { get; init; }
}

public sealed class EvidenceRecognitionTermProposalAuditResult
{
    public required int RecordCount { get; init; }

    public required int UniqueMatchingRecordCount { get; init; }

    public required int DiagnosisAnchorRecordCount { get; init; }

    public required int RequirementSignalRecordCount { get; init; }

    public required int QualifiedRecordCount { get; init; }

    public required IReadOnlyList<int> QualifiedRecordIndexes { get; init; }

    public required IReadOnlyList<EvidenceRecognitionTermProposalAuditSample>
        QualifiedSamples { get; init; }

    public required IReadOnlyList<EvidenceRecognitionTermProposalAudit>
        Audits { get; init; }
}

public sealed class EvidenceRecognitionTermProposalAuditService
{
    public const int DefaultMaximumSamples = 3;

    private const int QualifiedWindowRadiusLines = 16;

    public EvidenceRecognitionTermProposalAuditResult Audit(
        IReadOnlyList<EvidenceRecognitionTermProposal> proposals,
        IReadOnlyList<VeteransBlueButtonCareSummaryRecord> records,
        int maximumSamples = DefaultMaximumSamples)
    {
        ArgumentNullException.ThrowIfNull(proposals);
        ArgumentNullException.ThrowIfNull(records);

        if (maximumSamples < 0)
            throw new ArgumentOutOfRangeException(nameof(maximumSamples));

        var matchingRecordIndexes = new HashSet<int>();
        var audits =
            new List<EvidenceRecognitionTermProposalAudit>(proposals.Count);

        foreach (var proposal in proposals)
        {
            ArgumentNullException.ThrowIfNull(proposal);

            if (string.IsNullOrWhiteSpace(proposal.Term))
                throw new InvalidOperationException(
                    "Recognition-term proposal must not contain a blank term.");

            var samples =
                new List<EvidenceRecognitionTermProposalAuditSample>(
                    Math.Min(maximumSamples, records.Count));

            var matchingRecordCount = 0;

            for (var index = 0; index < records.Count; index++)
            {
                var record = records[index]
                    ?? throw new InvalidOperationException(
                        "Blue Button audit records must not contain null entries.");

                if (string.IsNullOrEmpty(record.Text) ||
                    !EvidenceRecognitionTextMatcher.ContainsTerm(
                        record.Text,
                        proposal.Term))
                {
                    continue;
                }

                matchingRecordCount++;
                matchingRecordIndexes.Add(index);

                if (samples.Count < maximumSamples)
                {
                    samples.Add(
                        new EvidenceRecognitionTermProposalAuditSample
                        {
                            Title = record.Title,
                            DateEntered = record.DateEntered,
                            SourceStartPage = record.SourceStartPage,
                            SourceEndPage = record.SourceEndPage
                        });
                }
            }

            audits.Add(
                new EvidenceRecognitionTermProposalAudit
                {
                    Proposal = proposal,
                    MatchingRecordCount = matchingRecordCount,
                    Samples = samples
                });
        }

        var diagnosisAnchors =
            proposals
                .Where(IsDiagnosisAnchor)
                .ToArray();

        var requirementSignals =
            proposals
                .Where(IsRequirementSignal)
                .ToArray();

        var diagnosisAnchorRecordCount = 0;
        var requirementSignalRecordCount = 0;
        var qualifiedRecordCount = 0;
        var qualifiedRecordIndexes = new List<int>();
        var qualifiedSamples =
            new List<EvidenceRecognitionTermProposalAuditSample>(
                Math.Min(maximumSamples, records.Count));

        for (var index = 0; index < records.Count; index++)
        {
            var record = records[index]
                ?? throw new InvalidOperationException(
                    "Blue Button audit records must not contain null entries.");

            if (string.IsNullOrEmpty(record.Text))
                continue;

            var hasDiagnosisAnchor =
                diagnosisAnchors.Any(
                    proposal =>
                        EvidenceRecognitionTextMatcher.ContainsTerm(
                            record.Text,
                            proposal.Term));

            var hasRequirementSignal =
                requirementSignals.Any(
                    proposal =>
                        EvidenceRecognitionTextMatcher.ContainsTerm(
                            record.Text,
                            proposal.Term));

            if (hasDiagnosisAnchor)
                diagnosisAnchorRecordCount++;

            if (hasRequirementSignal)
                requirementSignalRecordCount++;

            if (!hasDiagnosisAnchor ||
                !hasRequirementSignal ||
                !HasBoundedQualifiedWindow(
                    record.Text,
                    diagnosisAnchors,
                    requirementSignals))
            {
                continue;
            }

            qualifiedRecordCount++;
            qualifiedRecordIndexes.Add(index);

            if (qualifiedSamples.Count < maximumSamples)
            {
                qualifiedSamples.Add(
                    new EvidenceRecognitionTermProposalAuditSample
                    {
                        Title = record.Title,
                        DateEntered = record.DateEntered,
                        SourceStartPage = record.SourceStartPage,
                        SourceEndPage = record.SourceEndPage
                    });
            }
        }

        return new EvidenceRecognitionTermProposalAuditResult
        {
            RecordCount = records.Count,
            UniqueMatchingRecordCount = matchingRecordIndexes.Count,
            DiagnosisAnchorRecordCount = diagnosisAnchorRecordCount,
            RequirementSignalRecordCount = requirementSignalRecordCount,
            QualifiedRecordCount = qualifiedRecordCount,
            QualifiedRecordIndexes = qualifiedRecordIndexes,
            QualifiedSamples = qualifiedSamples,
            Audits = audits
        };
    }


    private static bool HasBoundedQualifiedWindow(
        string text,
        IReadOnlyList<EvidenceRecognitionTermProposal> diagnosisAnchors,
        IReadOnlyList<EvidenceRecognitionTermProposal> requirementSignals)
    {
        var lines =
            text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

        for (var lineIndex = 0;
             lineIndex < lines.Length;
             lineIndex++)
        {
            var anchorProbeEnd =
                Math.Min(
                    lines.Length - 1,
                    lineIndex + 1);

            var anchorProbe =
                JoinLines(
                    lines,
                    lineIndex,
                    anchorProbeEnd);

            if (!ContainsAnyTerm(
                    anchorProbe,
                    diagnosisAnchors))
            {
                continue;
            }

            var windowStart =
                Math.Max(
                    0,
                    lineIndex - QualifiedWindowRadiusLines);

            var windowEnd =
                Math.Min(
                    lines.Length - 1,
                    lineIndex + QualifiedWindowRadiusLines);

            var windowText =
                JoinLines(
                    lines,
                    windowStart,
                    windowEnd);

            if (ContainsAnyTerm(
                    windowText,
                    requirementSignals))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAnyTerm(
        string text,
        IReadOnlyList<EvidenceRecognitionTermProposal> proposals) =>
        proposals.Any(
            proposal =>
                EvidenceRecognitionTextMatcher.ContainsTerm(
                    text,
                    proposal.Term));

    private static string JoinLines(
        string[] lines,
        int start,
        int end) =>
        string.Join(
            " ",
            lines,
            start,
            end - start + 1);

    private static bool IsDiagnosisAnchor(
        EvidenceRecognitionTermProposal proposal) =>
        string.Equals(
            proposal.RecognitionRole,
            EvidenceRecognitionRoles.Diagnosis,
            StringComparison.Ordinal);

    private static bool IsRequirementSignal(
        EvidenceRecognitionTermProposal proposal) =>
        proposal.RecognitionRole is
            EvidenceRecognitionRoles.SeverityCriterion or
            EvidenceRecognitionRoles.FunctionalImpact or
            EvidenceRecognitionRoles.ServiceConnection or
            EvidenceRecognitionRoles.MedicalNexus or
            EvidenceRecognitionRoles.Aggravation or
            EvidenceRecognitionRoles.Presumptive;
}

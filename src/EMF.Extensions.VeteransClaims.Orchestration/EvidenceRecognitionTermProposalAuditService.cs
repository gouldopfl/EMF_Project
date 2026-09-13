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

public sealed class EvidenceRecognitionTermProposalAuditWindow
{
    public required int RecordIndex { get; init; }

    public required string Title { get; init; }

    public required string DateEntered { get; init; }

    public required int SourceStartPage { get; init; }

    public required int SourceEndPage { get; init; }

    public required IReadOnlyList<string> NoteTitles { get; init; }

    public required int StartLineNumber { get; init; }

    public required int EndLineNumber { get; init; }

    public required string Text { get; init; }
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

    public required IReadOnlyList<EvidenceRecognitionTermProposalAuditWindow>
        QualifiedWindows { get; init; }

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
        var qualifiedWindows =
            new List<EvidenceRecognitionTermProposalAuditWindow>();
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

            if (!hasDiagnosisAnchor || !hasRequirementSignal)
                continue;

            var recordQualifiedWindows =
                FindQualifiedWindows(
                    index,
                    record,
                    diagnosisAnchors,
                    requirementSignals);

            if (recordQualifiedWindows.Count == 0)
                continue;

            qualifiedRecordCount++;
            qualifiedRecordIndexes.Add(index);
            qualifiedWindows.AddRange(recordQualifiedWindows);

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
            QualifiedWindows = qualifiedWindows,
            QualifiedSamples = qualifiedSamples,
            Audits = audits
        };
    }


    private static IReadOnlyList<EvidenceRecognitionTermProposalAuditWindow>
        FindQualifiedWindows(
            int recordIndex,
            VeteransBlueButtonCareSummaryRecord record,
            IReadOnlyList<EvidenceRecognitionTermProposal> diagnosisAnchors,
            IReadOnlyList<EvidenceRecognitionTermProposal> requirementSignals)
    {
        var lines =
            record.Text
                .Replace("\r\n", "\n", StringComparison.Ordinal)
                .Replace('\r', '\n')
                .Split('\n');

        var structuredSections =
            FindStructuredMedicalOpinionSections(lines);

        var intervals =
            structuredSections.Count > 0
                ? FindQualifiedStructuredSections(
                    lines,
                    structuredSections,
                    diagnosisAnchors,
                    requirementSignals)
                : FindFallbackQualifiedWindows(
                    lines,
                    diagnosisAnchors,
                    requirementSignals);

        if (intervals.Count == 0)
            return [];

        return intervals
            .Select(interval =>
                new EvidenceRecognitionTermProposalAuditWindow
                {
                    RecordIndex = recordIndex,
                    Title = record.Title,
                    DateEntered = record.DateEntered,
                    SourceStartPage = record.SourceStartPage,
                    SourceEndPage = record.SourceEndPage,
                    NoteTitles = record.NoteTitles,
                    StartLineNumber = interval.Start + 1,
                    EndLineNumber = interval.End + 1,
                    Text = JoinLines(
                        lines,
                        interval.Start,
                        interval.End,
                        Environment.NewLine)
                })
            .ToArray();
    }

    private static IReadOnlyList<(int Start, int End)>
        FindStructuredMedicalOpinionSections(string[] lines)
    {
        var starts =
            Enumerable.Range(0, lines.Length)
                .Where(index => IsMedicalOpinionSectionStart(lines[index]))
                .ToArray();

        if (starts.Length == 0)
            return [];

        var sections = new List<(int Start, int End)>(starts.Length);

        for (var sectionIndex = 0;
             sectionIndex < starts.Length;
             sectionIndex++)
        {
            var start = starts[sectionIndex];
            var nextStart =
                sectionIndex + 1 < starts.Length
                    ? starts[sectionIndex + 1]
                    : lines.Length;

            var end = nextStart - 1;

            for (var index = start + 1;
                 index < nextStart;
                 index++)
            {
                if (IsMedicalOpinionSeparator(lines[index]) ||
                    IsMedicalOpinionSignature(lines[index]))
                {
                    end = index - 1;
                    break;
                }
            }

            while (end >= start &&
                   string.IsNullOrWhiteSpace(lines[end]))
            {
                end--;
            }

            if (end >= start)
                sections.Add((start, end));
        }

        return sections;
    }

    private static IReadOnlyList<(int Start, int End)>
        FindQualifiedStructuredSections(
            string[] lines,
            IReadOnlyList<(int Start, int End)> sections,
            IReadOnlyList<EvidenceRecognitionTermProposal> diagnosisAnchors,
            IReadOnlyList<EvidenceRecognitionTermProposal> requirementSignals)
    {
        var qualified = new List<(int Start, int End)>();

        foreach (var section in sections)
        {
            var text =
                JoinLines(
                    lines,
                    section.Start,
                    section.End,
                    " ");

            if (ContainsAnyTerm(text, diagnosisAnchors) &&
                ContainsAnyTerm(text, requirementSignals))
            {
                qualified.Add(section);
            }
        }

        return qualified;
    }

    private static IReadOnlyList<(int Start, int End)>
        FindFallbackQualifiedWindows(
            string[] lines,
            IReadOnlyList<EvidenceRecognitionTermProposal> diagnosisAnchors,
            IReadOnlyList<EvidenceRecognitionTermProposal> requirementSignals)
    {
        var intervals = new List<(int Start, int End)>();

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
                    anchorProbeEnd,
                    " ");

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
                    windowEnd,
                    " ");

            if (ContainsAnyTerm(
                    windowText,
                    requirementSignals))
            {
                intervals.Add((windowStart, windowEnd));
            }
        }

        if (intervals.Count == 0)
            return [];

        var merged = new List<(int Start, int End)>();

        foreach (var interval in intervals
                     .OrderBy(item => item.Start)
                     .ThenBy(item => item.End))
        {
            if (merged.Count == 0 ||
                interval.Start > merged[^1].End + 1)
            {
                merged.Add(interval);
                continue;
            }

            var previous = merged[^1];
            merged[^1] =
                (previous.Start, Math.Max(previous.End, interval.End));
        }

        return merged;
    }

    private static bool IsMedicalOpinionSectionStart(string line)
    {
        var text = line.Trim();

        return
            text.StartsWith(
                "RESTATEMENT OF REQUESTED OPINION:",
                StringComparison.OrdinalIgnoreCase) ||
            text.Contains(
                "TYPE OF MEDICAL OPINION REQUESTED:",
                StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsMedicalOpinionSeparator(string line)
    {
        var text = line.Trim();

        return text.Length >= 10 &&
            text.All(character => character == '*');
    }

    private static bool IsMedicalOpinionSignature(string line) =>
        line.Contains(
            "Examiner's signature:",
            StringComparison.OrdinalIgnoreCase);

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
        int end,
        string separator) =>
        string.Join(
            separator,
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

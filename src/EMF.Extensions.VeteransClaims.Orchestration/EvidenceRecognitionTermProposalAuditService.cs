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

    public required IReadOnlyList<EvidenceRecognitionTermProposalAudit>
        Audits { get; init; }
}

public sealed class EvidenceRecognitionTermProposalAuditService
{
    public const int DefaultMaximumSamples = 3;

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
                    !record.Text.Contains(
                        proposal.Term,
                        StringComparison.OrdinalIgnoreCase))
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

        return new EvidenceRecognitionTermProposalAuditResult
        {
            RecordCount = records.Count,
            UniqueMatchingRecordCount = matchingRecordIndexes.Count,
            Audits = audits
        };
    }
}

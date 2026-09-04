using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class VaDecisionDocumentProcessingAttemptService
{
    private readonly IVaDecisionDocumentProcessingAttemptRepository
        _repository;

    public VaDecisionDocumentProcessingAttemptService(
        IVaDecisionDocumentProcessingAttemptRepository repository)
    {
        ArgumentNullException.ThrowIfNull(repository);

        _repository = repository;
    }

    public async Task RecordAsync(
        ClaimId claimId,
        VaDecisionDocumentInterpretation interpretation,
        VaDecisionDocumentProcessingResult result,
        DateTimeOffset processedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(interpretation);
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(
            interpretation.IssueDecisions);
        ArgumentNullException.ThrowIfNull(result.Matches);

        if (processedAt == default)
        {
            throw new ArgumentOutOfRangeException(
                nameof(processedAt));
        }

        if (interpretation.IssueDecisions.Count == 0)
        {
            throw new InvalidOperationException(
                "A VA decision processing attempt must contain " +
                "at least one interpreted issue.");
        }

        if (result.Matches.Count !=
            interpretation.IssueDecisions.Count)
        {
            throw new InvalidOperationException(
                "Every interpreted VA decision issue must have " +
                "exactly one recorded match.");
        }

        foreach (var match in result.Matches)
        {
            ArgumentNullException.ThrowIfNull(match);
            ArgumentNullException.ThrowIfNull(
                match.Interpretation);
        }

        foreach (var issue in interpretation.IssueDecisions)
        {
            var count =
                result.Matches.Count(
                    match =>
                        ReferenceEquals(
                            match.Interpretation,
                            issue));

            if (count != 1)
            {
                throw new InvalidOperationException(
                    "Every interpreted VA decision issue must be " +
                    "represented exactly once in the result.");
            }
        }

        var hasUnresolved =
            result.Matches.Any(
                match =>
                    match.Status !=
                        VaDecisionDocumentIssueMatchStatuses.Matched ||
                    match.ClaimIssueId is null);

        if (result.Decision is not null && hasUnresolved)
        {
            throw new InvalidOperationException(
                "A persisted VA decision result cannot contain " +
                "unresolved issue matches.");
        }

        if (result.Decision is null && !hasUnresolved)
        {
            throw new InvalidOperationException(
                "An unpersisted VA decision result must contain " +
                "an unresolved issue match.");
        }

        await _repository.AddAsync(
            new VaDecisionDocumentProcessingAttempt
            {
                ClaimId = claimId,
                ArtifactId = interpretation.ArtifactId,
                ProcessedAt = processedAt,
                VaDecisionId = result.Decision?.Id,
                Matches = result.Matches
            },
            cancellationToken);
    }
}

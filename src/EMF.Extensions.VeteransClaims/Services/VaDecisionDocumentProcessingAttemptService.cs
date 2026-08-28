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

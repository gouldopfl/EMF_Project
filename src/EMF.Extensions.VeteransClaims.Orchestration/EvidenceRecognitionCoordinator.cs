using EMF.Core.Contracts;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class EvidenceRecognitionCoordinator :
    IEvidenceRecognitionCoordinator
{
    private readonly IEvidenceGapRepository _gapRepository;
    private readonly IArtifactTextExtractor _textExtractor;
    private readonly EvidenceRecognitionMatcher _matcher;

    public EvidenceRecognitionCoordinator(
        IEvidenceGapRepository gapRepository,
        IArtifactTextExtractor textExtractor,
        IEvidenceRecognitionTermRepository termRepository)
    {
        ArgumentNullException.ThrowIfNull(gapRepository);
        ArgumentNullException.ThrowIfNull(textExtractor);
        ArgumentNullException.ThrowIfNull(termRepository);

        _gapRepository = gapRepository;
        _textExtractor = textExtractor;
        _matcher = new EvidenceRecognitionMatcher(termRepository);
    }

    public async Task<IReadOnlyList<EvidenceRecognitionMatch>>
        RecognizeAsync(
            Models.Identities.EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default)
    {
        var gap =
            await _gapRepository.GetEvidenceGapAsync(
                evidenceGapId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Evidence gap was not found.");

        var artifacts =
            await _gapRepository.GetEvidenceGapArtifactsAsync(
                evidenceGapId,
                cancellationToken);

        var matches =
            new Dictionary<
                Models.Identities.EvidenceRecognitionTermId,
                EvidenceRecognitionMatch>();

        foreach (var artifact in artifacts)
        {
            var text =
                await _textExtractor.ExtractTextAsync(
                    artifact.ArtifactId,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(text))
                continue;

            var artifactMatches =
                await _matcher.FindMatchesAsync(
                    gap.RequirementId,
                    text,
                    cancellationToken);

            foreach (var match in artifactMatches)
                matches.TryAdd(match.TermId, match);
        }

        return matches.Values
            .OrderBy(match => match.TermId.Value)
            .ToArray();
    }
}

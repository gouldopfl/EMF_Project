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

    public async Task<EvidenceRecognitionResult>
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

        if (gap.Id != evidenceGapId)
            throw new InvalidOperationException(
                "Evidence gap identity mismatch.");

        var artifacts =
            await _gapRepository.GetEvidenceGapArtifactsAsync(
                evidenceGapId,
                cancellationToken);

        var matches =
            new Dictionary<
                Models.Identities.EvidenceRecognitionTermId,
                EvidenceRecognitionMatch>();

        var matchArtifacts =
            new List<EvidenceRecognitionMatchArtifact>();

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
            {
                matches.TryAdd(match.TermId, match);

                matchArtifacts.Add(
                    new EvidenceRecognitionMatchArtifact
                    {
                        RecognitionTermId = match.TermId,
                        ArtifactId = artifact.ArtifactId,
                        Role = artifact.Role
                    });
            }
        }

        return new EvidenceRecognitionResult
        {
            Matches =
                matches.Values
                    .OrderBy(match => match.TermId.Value)
                    .ToArray(),

            MatchArtifacts =
                matchArtifacts
                    .OrderBy(link => link.RecognitionTermId.Value)
                    .ThenBy(link => link.ArtifactId.Value)
                    .ToArray()
        };
    }
}

using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal sealed class DevelopEvidenceGapWorkflowActivity :
    IWorkflowActivity
{
    private readonly IEvidenceGapRepository _repository;
    private readonly IEvidenceRequirementGuidanceRepository _guidanceRepository;
    private readonly IEvidenceDevelopmentPlanRepository _developmentRepository;
    private readonly IEvidenceRecognitionCoordinator _recognitionCoordinator;
    private readonly IEvidenceClassificationService? _classificationService;
    private readonly IRequirementEvidenceService? _requirementEvidenceService;
    private readonly EvidenceGapId _evidenceGapId;

    public DevelopEvidenceGapWorkflowActivity(
        IEvidenceGapRepository repository,
        IEvidenceRequirementGuidanceRepository guidanceRepository,
        IEvidenceDevelopmentPlanRepository developmentRepository,
        EvidenceGapId evidenceGapId)
        : this(
            repository,
            guidanceRepository,
            developmentRepository,
            new EmptyEvidenceRecognitionCoordinator(),
            evidenceGapId)
    {
    }

    public DevelopEvidenceGapWorkflowActivity(
        IEvidenceGapRepository repository,
        IEvidenceRequirementGuidanceRepository guidanceRepository,
        IEvidenceDevelopmentPlanRepository developmentRepository,
        IEvidenceRecognitionCoordinator recognitionCoordinator,
        EvidenceGapId evidenceGapId)
        : this(
            repository,
            guidanceRepository,
            developmentRepository,
            recognitionCoordinator,
            null,
            null,
            evidenceGapId)
    {
    }

    public DevelopEvidenceGapWorkflowActivity(
        IEvidenceGapRepository repository,
        IEvidenceRequirementGuidanceRepository guidanceRepository,
        IEvidenceDevelopmentPlanRepository developmentRepository,
        IEvidenceRecognitionCoordinator recognitionCoordinator,
        IEvidenceClassificationService? classificationService,
        EvidenceGapId evidenceGapId)
        : this(
            repository,
            guidanceRepository,
            developmentRepository,
            recognitionCoordinator,
            classificationService,
            null,
            evidenceGapId)
    {
    }

    public DevelopEvidenceGapWorkflowActivity(
        IEvidenceGapRepository repository,
        IEvidenceRequirementGuidanceRepository guidanceRepository,
        IEvidenceDevelopmentPlanRepository developmentRepository,
        IEvidenceRecognitionCoordinator recognitionCoordinator,
        IEvidenceClassificationService? classificationService,
        IRequirementEvidenceService? requirementEvidenceService,
        EvidenceGapId evidenceGapId)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(guidanceRepository);
        ArgumentNullException.ThrowIfNull(developmentRepository);
        ArgumentNullException.ThrowIfNull(recognitionCoordinator);

        _repository = repository;
        _guidanceRepository = guidanceRepository;
        _developmentRepository = developmentRepository;
        _recognitionCoordinator = recognitionCoordinator;
        _classificationService = classificationService;
        _requirementEvidenceService = requirementEvidenceService;
        _evidenceGapId = evidenceGapId;
    }

    public string Id => "develop-evidence-gap";

    public string Name => "Develop Evidence Gap";

    public async Task<WorkflowActivityResult> ExecuteAsync(
        WorkflowExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        var gap =
            await _repository.GetEvidenceGapAsync(
                _evidenceGapId,
                cancellationToken);

        if (gap is null)
        {
            return new WorkflowActivityResult
            {
                Succeeded = false,
                Message = "Evidence gap was not found.",
                CompletedUtc = DateTimeOffset.UtcNow
            };
        }

        var guidance =
            await _guidanceRepository
                .GetEvidenceRequirementGuidanceAsync(
                    gap.RequirementId,
                    cancellationToken);

        var recognition =
            await _recognitionCoordinator.RecognizeAsync(
                gap.Id,
                cancellationToken);

        if (_classificationService is not null)
        {
            foreach (var match in recognition.Matches)
            {
                if (string.IsNullOrWhiteSpace(
                        match.EvidenceClassification))
                    continue;

                foreach (var link in recognition.MatchArtifacts
                    .Where(x => x.RecognitionTermId == match.TermId))
                {
                    var classification =
                        await _classificationService.ClassifyAsync(
                            link.ArtifactId,
                            match.EvidenceClassification,
                            gap.ClaimIssueId,
                            cancellationToken);

                    await _classificationService
                        .AssociateRequirementAsync(
                            classification.Id,
                            gap.RequirementId,
                            cancellationToken);
                }
            }
        }

        var developmentResult =
            new EMF.Extensions.VeteransClaims.Models.Adjudication.EvidenceDevelopmentResult
            {
                EvidenceGapId = gap.Id,
                RequirementId = gap.RequirementId,
                EvidenceGuidance = guidance,
                RecognitionMatches = recognition.Matches,
                RecognitionMatchArtifacts =
                    recognition.MatchArtifacts
            };

        await _developmentRepository
            .AddEvidenceDevelopmentResultAsync(
                developmentResult,
                cancellationToken);

        RequirementEvidenceAssessment? assessment = null;
        RequirementEvidenceResponsivenessAssessment? responsiveness = null;

        if (_requirementEvidenceService is not null)
        {
            assessment =
                await _requirementEvidenceService.AssessAsync(
                    gap.RequirementId,
                    cancellationToken);

            responsiveness =
                await _requirementEvidenceService
                    .AssessResponsivenessAsync(
                        gap.RequirementId,
                        cancellationToken);


            if (responsiveness.MissingItemCount == 0)
            {
                await _repository.UpdateEvidenceGapStatusAsync(
                    gap.Id,
                    EvidenceGapStatuses.Resolved,
                    cancellationToken);
            }
        }

        return new WorkflowActivityResult
        {
            Succeeded = true,
            Message =
                $"Evidence gap: {gap.Description}; " +
                $"guidance items: {guidance.Count}" +
                (assessment is null
                    ? "."
                    : $"; evidence present: {assessment.HasEvidence}; " +
                      $"matching guidance items: " +
                      $"{responsiveness!.MatchingItemCount}; " +
                      $"missing guidance items: " +
                      $"{responsiveness.MissingItemCount}."),
            CompletedUtc = DateTimeOffset.UtcNow
        };
    }
}

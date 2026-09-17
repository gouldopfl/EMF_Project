namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerMedicalOpinionRequest
{
    public required string OpinionText { get; init; }

    public IReadOnlyList<string> ApplicableRegulatoryCitations { get; init; } =
        Array.Empty<string>();
}

using EMF.Core.Models;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerArtifactContent
{
    public required Artifact Artifact
    { get; init; }

    public required string Text
    { get; init; }

    public IReadOnlyList<PrintableArtifactPage> PrintablePages
    { get; init; } = [];

    public string? Appendix
    { get; init; }

    public IReadOnlyList<Provenance> Provenance
    { get; init; } = [];

    public IReadOnlyList<Relationship> Relationships
    { get; init; } = [];

    public IReadOnlyList<ReviewedMedicalLiteratureClassification>
        ReviewedMedicalLiteratureClassifications
    { get; init; } = [];
}

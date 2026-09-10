using EMF.Common;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class RegulatoryEvidenceGuidanceService :
    IRegulatoryEvidenceGuidanceService
{
    private readonly IRegulatoryRepository _regulatory;
    private readonly IEvidenceRequirementGuidanceRepository _guidance;
    private readonly IIdGenerator _idGenerator;

    public RegulatoryEvidenceGuidanceService(
        IRegulatoryRepository regulatory,
        IEvidenceRequirementGuidanceRepository guidance,
        IIdGenerator? idGenerator = null)
    {
        ArgumentNullException.ThrowIfNull(regulatory);
        ArgumentNullException.ThrowIfNull(guidance);

        _regulatory = regulatory;
        _guidance = guidance;
        _idGenerator = idGenerator ?? new GuidIdGenerator();
    }

    public async Task<EvidenceRequirementGuidance> AddEvidenceGuidanceAsync(
        RequirementId requirementId, string evidenceClassification,
        string guidanceRole, string description,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(evidenceClassification);
        ArgumentException.ThrowIfNullOrWhiteSpace(guidanceRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (evidenceClassification is not (
            EvidenceClassifications.MedicalEvidence or
            EvidenceClassifications.ServiceTreatmentRecord or
            EvidenceClassifications.ServiceRecord or
            EvidenceClassifications.LayEvidence or
            EvidenceClassifications.Examination or
            EvidenceClassifications.MedicalOpinion or
            EvidenceClassifications.AdjudicativeRecord))
            throw new ArgumentException(
                $"Unsupported evidence classification '{evidenceClassification}'.",
                nameof(evidenceClassification));

        if (guidanceRole is not (
            EvidenceGuidanceRoles.SupportsRequirement or
            EvidenceGuidanceRoles.EstablishesElement or
            EvidenceGuidanceRoles.Corroborates or
            EvidenceGuidanceRoles.Clarifies))
            throw new ArgumentException(
                $"Unsupported evidence guidance role '{guidanceRole}'.",
                nameof(guidanceRole));

        var requirement=await _regulatory.GetRequirementAsync(requirementId,cancellationToken);
        if (requirement is null)
            throw new InvalidOperationException($"Requirement not found: {requirementId.Value}");

        var existing=await _guidance.GetEvidenceRequirementGuidanceAsync(requirementId,cancellationToken);
        if (existing.Any(x=>x.RequirementId!=requirementId))
            throw new InvalidOperationException(
                $"Requirement '{requirementId.Value}' guidance lookup returned guidance for a different requirement.");

        var match=existing.FirstOrDefault(x=>
            x.EvidenceClassification==evidenceClassification &&
            x.GuidanceRole==guidanceRole &&
            x.Description==description);
        if (match is not null) return match;

        var result=new EvidenceRequirementGuidance {
            Id=new EvidenceRequirementGuidanceId(_idGenerator.Generate()),
            RequirementId=requirementId,
            EvidenceClassification=evidenceClassification,
            GuidanceRole=guidanceRole,
            Description=description
        };
        await _guidance.AddEvidenceRequirementGuidanceAsync(result,cancellationToken);
        return result;
    }

    public async Task<IReadOnlyList<RequirementEvidenceGuidance>>
        GetEvidenceGuidanceAsync(
            RegulatoryProvisionId provisionId,
            CancellationToken cancellationToken = default)
    {
        var requirements =
            await _regulatory.GetRequirementsAsync(
                provisionId,
                cancellationToken);

        var badRequirement =
            requirements.FirstOrDefault(
                x => x.RegulatoryProvisionId != provisionId);

        if (badRequirement is not null)
            throw new InvalidOperationException(
                $"Provision '{provisionId.Value}' requirement lookup returned " +
                $"requirement '{badRequirement.Id.Value}' for provision " +
                $"'{badRequirement.RegulatoryProvisionId.Value}'.");

        var results = new List<RequirementEvidenceGuidance>();

        foreach (var requirement in requirements)
        {
            var guidance =
                await _guidance.GetEvidenceRequirementGuidanceAsync(
                    requirement.Id,
                    cancellationToken);

            if (guidance.Any(x => x.RequirementId != requirement.Id))
                throw new InvalidOperationException(
                    $"Requirement '{requirement.Id.Value}' guidance lookup " +
                    "returned guidance for a different requirement.");

            results.Add(
                new RequirementEvidenceGuidance
                {
                    Requirement = requirement,
                    EvidenceGuidance = guidance
                });
        }

        return results;
    }
}

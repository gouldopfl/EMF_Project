using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class RegulatoryEvidenceGuidanceService :
    IRegulatoryEvidenceGuidanceService
{
    private readonly IRegulatoryRepository _regulatory;
    private readonly IEvidenceRequirementGuidanceRepository _guidance;

    public RegulatoryEvidenceGuidanceService(
        IRegulatoryRepository regulatory,
        IEvidenceRequirementGuidanceRepository guidance)
    {
        ArgumentNullException.ThrowIfNull(regulatory);
        ArgumentNullException.ThrowIfNull(guidance);

        _regulatory = regulatory;
        _guidance = guidance;
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

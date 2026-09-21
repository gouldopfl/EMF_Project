using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerMedicalOpinionRequestService
{
    private readonly IServiceConnectionRepository _connections;
    private readonly IConditionRepository _conditions;
    private readonly IRegulatoryRepository _regulatory;

    public VeteransReviewerMedicalOpinionRequestService(
        IServiceConnectionRepository connections,
        IConditionRepository conditions,
        IRegulatoryRepository regulatory)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(conditions);
        ArgumentNullException.ThrowIfNull(regulatory);

        _connections = connections;
        _conditions = conditions;
        _regulatory = regulatory;
    }

    public async Task<VeteransReviewerMedicalOpinionRequest?> GetAsync(
        EvidencePackage package,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(package);

        if (package.ServiceConnectionBasisId is null)
            return null;

        var basis =
            await _connections.GetServiceConnectionBasisAsync(
                package.ServiceConnectionBasisId.Value,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Reviewer package service-connection basis was not found.");

        if (basis.Id != package.ServiceConnectionBasisId.Value ||
            basis.ClaimIssueId != package.ClaimIssueId)
        {
            throw new InvalidOperationException(
                "Reviewer package service-connection basis lineage mismatch.");
        }

        var theory =
            await _connections.GetServiceConnectionTheoryAsync(
                basis.ServiceConnectionTheoryId,
                cancellationToken)
            ?? throw new InvalidOperationException(
                "Reviewer package service-connection theory was not found.");

        if (theory.Id != basis.ServiceConnectionTheoryId ||
            theory.ClaimIssueId != package.ClaimIssueId)
        {
            throw new InvalidOperationException(
                "Reviewer package service-connection theory lineage mismatch.");
        }

        if (!string.Equals(
                theory.TheoryType,
                ServiceConnectionTheoryTypes.Secondary,
                StringComparison.Ordinal))
        {
            return null;
        }

        var claimedConditionIds =
            await _connections.GetClaimedConditionIdsAsync(
                basis.Id,
                cancellationToken);

        if (claimedConditionIds.Count == 0)
            throw new InvalidOperationException(
                "Secondary reviewer basis has no claimed condition.");

        var claimedConditions = new List<ClaimedCondition>();

        foreach (var conditionId in claimedConditionIds)
        {
            var condition =
                await _conditions.GetClaimedConditionAsync(
                    conditionId,
                    cancellationToken)
                ?? throw new InvalidOperationException(
                    "Secondary reviewer claimed condition was not found.");

            if (condition.Id != conditionId ||
                condition.ClaimIssueId != package.ClaimIssueId)
            {
                throw new InvalidOperationException(
                    "Secondary reviewer claimed condition lineage mismatch.");
            }

            claimedConditions.Add(condition);
        }

        var selectedMedicationNames =
            await _connections.GetPrescribedMedicationNamesAsync(
                basis.Id,
                cancellationToken);

        var opinionBases =
            new List<(
                ServiceConnectionBasis Basis,
                IReadOnlyList<string> MedicationNames)>
            {
                (basis, selectedMedicationNames)
            };

        if (selectedMedicationNames.Count > 0)
        {
            var siblingBases =
                await _connections.GetServiceConnectionBasesAsync(
                    basis.ServiceConnectionTheoryId,
                    cancellationToken);

            foreach (var siblingBasis in siblingBases)
            {
                if (siblingBasis.Id == basis.Id ||
                    siblingBasis.ClaimIssueId != package.ClaimIssueId ||
                    siblingBasis.ServiceConnectionTheoryId !=
                        basis.ServiceConnectionTheoryId)
                {
                    continue;
                }

                var siblingMedicationNames =
                    await _connections.GetPrescribedMedicationNamesAsync(
                        siblingBasis.Id,
                        cancellationToken);

                if (siblingMedicationNames.Count > 0)
                    opinionBases.Add(
                        (siblingBasis, siblingMedicationNames));
            }
        }

        var targets = new List<string>();

        foreach (var opinionBasis in opinionBases)
        {
            var serviceConnectedConditionIds =
                await _connections.GetServiceConnectedConditionIdsAsync(
                    opinionBasis.Basis.Id,
                    cancellationToken);

            if (serviceConnectedConditionIds.Count == 0)
                throw new InvalidOperationException(
                    "Secondary reviewer basis has no service-connected condition.");

            var serviceConnectedConditions = new List<MedicalCondition>();

            foreach (var conditionId in serviceConnectedConditionIds)
            {
                var condition =
                    await _conditions.GetMedicalConditionAsync(
                        conditionId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Secondary reviewer service-connected condition was not found.");

                if (condition.Id != conditionId)
                {
                    throw new InvalidOperationException(
                        "Secondary reviewer service-connected condition identity mismatch.");
                }

                serviceConnectedConditions.Add(condition);
            }

            var serviceConnectedNames =
                string.IsNullOrWhiteSpace(opinionBasis.Basis.ReviewerLabel)
                    ? FormatConditionNames(
                        serviceConnectedConditions.Select(x => x.Name))
                    : opinionBasis.Basis.ReviewerLabel.Trim();

            if (!VeteransReviewerDisplayNameResolver
                    .IsReviewerFacingLabel(serviceConnectedNames))
            {
                throw new InvalidOperationException(
                    "Reviewer medical opinion basis label is invalid.");
            }

            targets.Add(
                opinionBasis.MedicationNames.Count > 0
                    ? MedicationOpinionTarget(serviceConnectedNames)
                    : $"the Veteran's service-connected {serviceConnectedNames}");
        }

        var claimedNames =
            FormatConditionNames(
                claimedConditions.Select(x => x.Name));

        var verb = claimedConditions.Count == 1 ? "is" : "are";
        var targetText = FormatOpinionTargets(targets);
        var medicationOpinion = selectedMedicationNames.Count > 0;

        var citations =
            await GetApplicableRegulatoryCitationsAsync(
                opinionBases.Select(item => item.Basis.Id),
                cancellationToken);

        return new VeteransReviewerMedicalOpinionRequest
        {
            OpinionText =
                $"Determine whether the Veteran's {claimedNames} {verb} at least as " +
                "likely as not (50 percent or greater probability) proximately due to " +
                $"or the result of {targetText}. " +
                "If causation is not established, determine whether the Veteran's " +
                $"{claimedNames} {verb} at least as likely as not aggravated by " +
                (medicationOpinion
                    ? "one or more of those medications"
                    : AggravationOpinionTargets(targets)) +
                ", with supporting medical rationale.",
            ApplicableRegulatoryCitations = citations
        };
    }

    private async Task<IReadOnlyList<string>>
        GetApplicableRegulatoryCitationsAsync(
            IEnumerable<ServiceConnectionBasisId> basisIds,
            CancellationToken cancellationToken)
    {
        var citations = new List<string>();

        foreach (var basisId in basisIds.Distinct())
        {
            var requirementIds =
                await _connections.GetRequirementIdsAsync(
                    basisId,
                    cancellationToken);

            foreach (var requirementId in requirementIds)
            {
                var requirement =
                    await _regulatory.GetRequirementAsync(
                        requirementId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Reviewer medical opinion requirement was not found.");

                if (requirement.Id != requirementId)
                {
                    throw new InvalidOperationException(
                        "Reviewer medical opinion requirement identity mismatch.");
                }

                var provision =
                    await _regulatory.GetRegulatoryProvisionAsync(
                        requirement.RegulatoryProvisionId,
                        cancellationToken)
                    ?? throw new InvalidOperationException(
                        "Reviewer medical opinion regulatory provision was not found.");

                if (provision.Id != requirement.RegulatoryProvisionId)
                {
                    throw new InvalidOperationException(
                        "Reviewer medical opinion regulatory provision identity mismatch.");
                }

                if (string.IsNullOrWhiteSpace(provision.Citation))
                {
                    throw new InvalidOperationException(
                        "Reviewer medical opinion regulatory citation is empty.");
                }

                citations.Add(provision.Citation.Trim());
            }
        }

        return citations
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string MedicationOpinionTarget(string reviewerLabel)
    {
        const string prefix =
            "Secondary to medications used for service-connected ";

        var label = reviewerLabel.Trim().TrimEnd('.');

        if (label.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            label = label[prefix.Length..].Trim();

        return
            $"one or more medications prescribed for the Veteran's service-connected {label}";
    }

    private static string FormatOpinionTargets(IReadOnlyList<string> targets)
    {
        var values =
            targets
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (values.Length == 0)
            throw new InvalidOperationException(
                "Reviewer medical opinion target is empty.");

        return values.Length switch
        {
            1 => values[0],
            2 => $"{values[0]} and/or {values[1]}",
            _ =>
                string.Join(", ", values[..^1]) +
                $", and/or {values[^1]}"
        };
    }

    private static string AggravationOpinionTargets(
        IReadOnlyList<string> targets)
    {
        var values =
            targets
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(value =>
                    value.StartsWith(
                        "the Veteran's ",
                        StringComparison.Ordinal)
                        ? "the " + value["the Veteran's ".Length..]
                        : value)
                .ToArray();

        return FormatOpinionTargets(values);
    }

    private static string FormatConditionNames(IEnumerable<string> names)
    {
        var values =
            names
                .Select(name => name.Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();

        if (values.Length == 0)
            throw new InvalidOperationException(
                "Reviewer medical opinion condition name is empty.");

        return values.Length switch
        {
            1 => values[0],
            2 => $"{values[0]} and {values[1]}",
            _ => string.Join(", ", values[..^1]) + $", and {values[^1]}"
        };
    }
}

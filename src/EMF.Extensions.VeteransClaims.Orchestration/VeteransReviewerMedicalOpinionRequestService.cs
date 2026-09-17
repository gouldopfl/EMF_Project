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

        var serviceConnectedConditionIds =
            await _connections.GetServiceConnectedConditionIdsAsync(
                basis.Id,
                cancellationToken);

        if (serviceConnectedConditionIds.Count == 0)
            throw new InvalidOperationException(
                "Secondary reviewer basis has no service-connected condition.");

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

        var claimedNames =
            FormatConditionNames(
                claimedConditions.Select(x => x.Name));

        var serviceConnectedNames =
            string.IsNullOrWhiteSpace(basis.ReviewerLabel)
                ? FormatConditionNames(
                    serviceConnectedConditions.Select(x => x.Name))
                : basis.ReviewerLabel.Trim();

        if (!VeteransReviewerDisplayNameResolver
                .IsReviewerFacingLabel(serviceConnectedNames))
            throw new InvalidOperationException(
                "Reviewer medical opinion basis label is invalid.");

        var verb = claimedConditions.Count == 1 ? "is" : "are";

        var citations =
            await GetApplicableRegulatoryCitationsAsync(
                basis.Id,
                cancellationToken);

        return new VeteransReviewerMedicalOpinionRequest
        {
            OpinionText =
                $"Determine whether the Veteran's {claimedNames} {verb} at least as " +
                "likely as not (50 percent or greater probability) proximately due to " +
                $"or the result of the Veteran's service-connected {serviceConnectedNames}. " +
                "If causation is not established, determine whether the Veteran's " +
                $"{claimedNames} {verb} at least as likely as not aggravated by the " +
                $"service-connected {serviceConnectedNames}, with supporting medical rationale.",
            ApplicableRegulatoryCitations = citations
        };
    }

    private async Task<IReadOnlyList<string>>
        GetApplicableRegulatoryCitationsAsync(
            ServiceConnectionBasisId basisId,
            CancellationToken cancellationToken)
    {
        var requirementIds =
            await _connections.GetRequirementIdsAsync(
                basisId,
                cancellationToken);

        if (requirementIds.Count == 0)
            return Array.Empty<string>();

        var citations = new List<string>();

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

        return citations
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
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

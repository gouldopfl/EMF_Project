using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Service;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerMedicalOpinionRequestService
{
    private readonly IServiceConnectionRepository _connections;
    private readonly IConditionRepository _conditions;

    public VeteransReviewerMedicalOpinionRequestService(
        IServiceConnectionRepository connections,
        IConditionRepository conditions)
    {
        ArgumentNullException.ThrowIfNull(connections);
        ArgumentNullException.ThrowIfNull(conditions);

        _connections = connections;
        _conditions = conditions;
    }

    public async Task<string?> GetAsync(
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
            FormatConditionNames(
                serviceConnectedConditions.Select(x => x.Name));

        var verb = claimedConditions.Count == 1 ? "is" : "are";

        return
            $"Determine whether the Veteran's {claimedNames} {verb} at least as " +
            "likely as not (50 percent or greater probability) proximately due to " +
            $"or the result of the Veteran's service-connected {serviceConnectedNames}. " +
            "If causation is not established, determine whether the Veteran's " +
            $"{claimedNames} {verb} at least as likely as not aggravated by the " +
            $"service-connected {serviceConnectedNames}, with supporting medical rationale.";
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

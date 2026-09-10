using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class MedicalLiteratureService :
    IMedicalLiteratureService
{
    private readonly IRegulatoryRepository _regulatory;
    private readonly IMedicalLiteratureRepository _literature;

    public MedicalLiteratureService(
        IRegulatoryRepository regulatory,
        IMedicalLiteratureRepository literature)
    {
        ArgumentNullException.ThrowIfNull(regulatory);
        ArgumentNullException.ThrowIfNull(literature);

        _regulatory = regulatory;
        _literature = literature;
    }

    public async Task<MedicalLiteratureSource> AddSourceAsync(
        MedicalLiteratureSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Authors);
        ArgumentException.ThrowIfNullOrWhiteSpace(source.Publication);

        var existing =
            await _literature.GetMedicalLiteratureSourceAsync(
                source.Id,
                cancellationToken);

        if (existing is not null)
        {
            if (!SameSource(existing, source))
                throw new InvalidOperationException(
                    $"Medical literature source '{source.Id.Value}' " +
                    "already exists with different metadata.");

            return existing;
        }

        await _literature.AddMedicalLiteratureSourceAsync(
            source,
            cancellationToken);

        return source;
    }

    public async Task<RequirementMedicalLiteratureDetails>
        AddRequirementLiteratureAsync(
            RequirementId requirementId,
            MedicalLiteratureSourceId sourceId,
            string guidanceRole,
            string description,
            CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(guidanceRole);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);

        if (guidanceRole is not (
            EvidenceGuidanceRoles.SupportsRequirement or
            EvidenceGuidanceRoles.EstablishesElement or
            EvidenceGuidanceRoles.Corroborates or
            EvidenceGuidanceRoles.Clarifies))
        {
            throw new ArgumentException(
                $"Unsupported evidence guidance role '{guidanceRole}'.",
                nameof(guidanceRole));
        }

        var requirement =
            await _regulatory.GetRequirementAsync(
                requirementId,
                cancellationToken);

        if (requirement is null)
            throw new InvalidOperationException(
                $"Requirement not found: {requirementId.Value}");

        var source =
            await _literature.GetMedicalLiteratureSourceAsync(
                sourceId,
                cancellationToken);

        if (source is null)
            throw new InvalidOperationException(
                $"Medical literature source not found: {sourceId.Value}");

        var existing =
            await _literature.GetRequirementMedicalLiteratureAsync(
                requirementId,
                cancellationToken);

        if (existing.Any(x => x.RequirementId != requirementId))
            throw new InvalidOperationException(
                $"Requirement '{requirementId.Value}' literature lookup " +
                "returned an association for a different requirement.");

        var sameKey =
            existing.FirstOrDefault(
                x =>
                    x.MedicalLiteratureSourceId == sourceId &&
                    x.GuidanceRole == guidanceRole);

        if (sameKey is not null)
        {
            if (sameKey.Description != description)
                throw new InvalidOperationException(
                    $"Medical literature source '{sourceId.Value}' is " +
                    $"already linked to requirement '{requirementId.Value}' " +
                    $"with role '{guidanceRole}' and a different description.");

            return new RequirementMedicalLiteratureDetails
            {
                Association = sameKey,
                Source = source
            };
        }

        var association =
            new RequirementMedicalLiterature
            {
                RequirementId = requirementId,
                MedicalLiteratureSourceId = sourceId,
                GuidanceRole = guidanceRole,
                Description = description
            };

        await _literature.AddRequirementMedicalLiteratureAsync(
            association,
            cancellationToken);

        return new RequirementMedicalLiteratureDetails
        {
            Association = association,
            Source = source
        };
    }

    private static bool SameSource(
        MedicalLiteratureSource left,
        MedicalLiteratureSource right) =>
        left.Id == right.Id &&
        left.Title == right.Title &&
        left.Authors == right.Authors &&
        left.Publication == right.Publication &&
        left.PublicationYear == right.PublicationYear &&
        left.VaAffiliated == right.VaAffiliated &&
        left.VaFunded == right.VaFunded &&
        left.PeerReviewed == right.PeerReviewed &&
        left.FundingSource == right.FundingSource &&
        left.ResearchOrganization == right.ResearchOrganization &&
        left.Doi == right.Doi &&
        left.Pmid == right.Pmid &&
        left.SourceUri == right.SourceUri &&
        left.SourceHash == right.SourceHash;
}

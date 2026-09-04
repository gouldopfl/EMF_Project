using EMF.Common;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;

namespace EMF.Extensions.VeteransClaims.Services;

public sealed class EvidencePackageService :
    IEvidencePackageService
{
    private readonly IEvidencePackageRepository _repository;
    private readonly IIdGenerator _idGenerator;

    public EvidencePackageService(
        IEvidencePackageRepository repository,
        IIdGenerator idGenerator)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(idGenerator);

        _repository = repository;
        _idGenerator = idGenerator;
    }

    public Task<EvidencePackage> CreateAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        CancellationToken cancellationToken = default) =>
        CreateAsync(
            claimIssueId,
            purpose,
            reviewerRole,
            [],
            [],
            cancellationToken);

    public async Task<EvidencePackage> CreateAsync(
        ClaimIssueId claimIssueId,
        string purpose,
        string reviewerRole,
        IReadOnlyCollection<ArtifactId>
            underlyingEvidenceArtifactIds,
        IReadOnlyCollection<ArtifactId>
            generatedOrganizationalMaterialArtifactIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(purpose);
        ArgumentException.ThrowIfNullOrWhiteSpace(reviewerRole);
        ArgumentNullException.ThrowIfNull(
            underlyingEvidenceArtifactIds);
        ArgumentNullException.ThrowIfNull(
            generatedOrganizationalMaterialArtifactIds);

        var conflictingArtifactIds =
            underlyingEvidenceArtifactIds
                .Intersect(
                    generatedOrganizationalMaterialArtifactIds)
                .ToArray();

        if (conflictingArtifactIds.Length != 0)
        {
            throw new InvalidOperationException(
                $"Artifact '{conflictingArtifactIds[0].Value}' cannot be " +
                "both underlying evidence and generated organizational material.");
        }

        var package =
            new EvidencePackage
            {
                Id =
                    new EvidencePackageId(
                        _idGenerator.Generate()),
                ClaimIssueId = claimIssueId,
                Purpose = purpose,
                ReviewerRole = reviewerRole
            };

        var artifacts =
            underlyingEvidenceArtifactIds
                .Distinct()
                .Select(
                    artifactId =>
                        new EvidencePackageArtifact
                        {
                            EvidencePackageId = package.Id,
                            ArtifactId = artifactId,
                            ContentRole =
                                EvidencePackageContentRoles
                                    .UnderlyingEvidence
                        })
                .Concat(
                    generatedOrganizationalMaterialArtifactIds
                        .Distinct()
                        .Select(
                            artifactId =>
                                new EvidencePackageArtifact
                                {
                                    EvidencePackageId = package.Id,
                                    ArtifactId = artifactId,
                                    ContentRole =
                                        EvidencePackageContentRoles
                                            .GeneratedOrganizationalMaterial
                                }))
                .ToArray();

        await _repository.AddEvidencePackageAsync(
            package,
            artifacts,
            cancellationToken);

        return package;
    }

    public async Task<EvidencePackageArtifact> AddArtifactAsync(
        EvidencePackageId evidencePackageId,
        ArtifactId artifactId,
        string contentRole,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(contentRole);

        if (contentRole is not (
            EvidencePackageContentRoles.UnderlyingEvidence or
            EvidencePackageContentRoles.GeneratedOrganizationalMaterial))
        {
            throw new ArgumentException(
                $"Unsupported evidence package content role '{contentRole}'.",
                nameof(contentRole));
        }

        var existingArtifacts =
            await _repository.GetEvidencePackageArtifactsAsync(
                evidencePackageId,
                cancellationToken);

        if (existingArtifacts.Any(
            existing =>
                existing.ArtifactId == artifactId &&
                !string.Equals(
                    existing.ContentRole,
                    contentRole,
                    StringComparison.Ordinal)))
        {
            throw new InvalidOperationException(
                $"Artifact '{artifactId.Value}' already has a different " +
                $"content role in evidence package '{evidencePackageId.Value}'.");
        }

        var artifact =
            new EvidencePackageArtifact
            {
                EvidencePackageId = evidencePackageId,
                ArtifactId = artifactId,
                ContentRole = contentRole
            };

        await _repository.AddEvidencePackageArtifactAsync(
            artifact,
            cancellationToken);

        return artifact;
    }

    public async Task<IReadOnlyList<EvidencePackageDetails>> GetAsync(
        ClaimIssueId claimIssueId,
        CancellationToken cancellationToken = default)
    {
        var packages =
            await _repository.GetEvidencePackagesAsync(
                claimIssueId,
                cancellationToken);

        var details =
            new List<EvidencePackageDetails>();

        foreach (var package in packages)
        {
            var artifacts =
                await _repository.GetEvidencePackageArtifactsAsync(
                    package.Id,
                    cancellationToken);

            ValidateContentRoles(artifacts);

            details.Add(
                new EvidencePackageDetails
                {
                    Package = package,
                    Artifacts = artifacts
                });
        }

        return details;
    }

    public async Task<EvidencePackageDetails?> GetAsync(
        EvidencePackageId evidencePackageId,
        CancellationToken cancellationToken = default)
    {
        var package =
            await _repository.GetEvidencePackageAsync(
                evidencePackageId,
                cancellationToken);

        if (package is null)
            return null;

        var artifacts =
            await _repository.GetEvidencePackageArtifactsAsync(
                evidencePackageId,
                cancellationToken);

        ValidateContentRoles(artifacts);

        return new EvidencePackageDetails
        {
            Package = package,
            Artifacts = artifacts
        };
    }

    private static void ValidateContentRoles(
        IReadOnlyList<EvidencePackageArtifact> artifacts)
    {
        foreach (var artifact in artifacts)
        {
            if (artifact.ContentRole is
                EvidencePackageContentRoles.UnderlyingEvidence or
                EvidencePackageContentRoles.GeneratedOrganizationalMaterial)
            {
                continue;
            }

            throw new InvalidOperationException(
                $"Evidence package '{artifact.EvidencePackageId.Value}' " +
                $"contains unsupported content role '{artifact.ContentRole}'.");
        }
    }
}

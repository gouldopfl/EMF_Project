using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class RegulatoryEvidenceGuidanceServiceTests
{
    [Fact]
    public async Task GetEvidenceGuidanceAsync_ComposesRequirementsAndGuidance()
    {
        var provisionId = new RegulatoryProvisionId("provision-001");

        var requirement =
            new Requirement
            {
                Id = new RequirementId("requirement-001"),
                RegulatoryProvisionId = provisionId,
                Description = "Required element."
            };

        var guidance =
            new EvidenceRequirementGuidance
            {
                Id = new EvidenceRequirementGuidanceId("guidance-001"),
                RequirementId = requirement.Id,
                EvidenceClassification =
                    EvidenceClassifications.MedicalOpinion,
                GuidanceRole =
                    EvidenceGuidanceRoles.SupportsRequirement,
                Description = "Supporting evidence."
            };

        var service =
            new RegulatoryEvidenceGuidanceService(
                new StubRegulatoryRepository(requirement),
                new StubGuidanceRepository(guidance));

        var results =
            await service.GetEvidenceGuidanceAsync(provisionId);

        Assert.Single(results);
        Assert.Equal(requirement.Id, results[0].Requirement.Id);
        Assert.Single(results[0].EvidenceGuidance);
        Assert.Equal(
            guidance.Id,
            results[0].EvidenceGuidance[0].Id);
    }


    [Fact]
    public async Task GetEvidenceGuidanceAsync_RetainsRequirementWithoutGuidance()
    {
        var provisionId =
            new RegulatoryProvisionId("provision-001");

        var requirement =
            new Requirement
            {
                Id = new RequirementId("requirement-001"),
                RegulatoryProvisionId = provisionId,
                Description = "Required element."
            };

        var service =
            new RegulatoryEvidenceGuidanceService(
                new StubRegulatoryRepository(requirement),
                new EmptyGuidanceRepository());

        var results =
            await service.GetEvidenceGuidanceAsync(provisionId);

        Assert.Single(results);
        Assert.Equal(
            requirement.Id,
            results[0].Requirement.Id);
        Assert.Empty(results[0].EvidenceGuidance);
    }



    [Fact]
    public async Task GetEvidenceGuidanceAsync_ReturnsEmptyWhenProvisionHasNoRequirements()
    {
        var service =
            new RegulatoryEvidenceGuidanceService(
                new EmptyRegulatoryRepository(),
                new EmptyGuidanceRepository());

        var results =
            await service.GetEvidenceGuidanceAsync(
                new RegulatoryProvisionId("provision-001"));

        Assert.Empty(results);
    }



    private sealed class EmptyRegulatoryRepository :
        IRegulatoryRepository
    {
        public Task<IReadOnlyList<Requirement>> GetRequirementsAsync(
            RegulatoryProvisionId provisionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Requirement>>(
                Array.Empty<Requirement>());

        public Task AddRegulatoryAuthorityAsync(RegulatoryAuthority a, CancellationToken c = default) => throw new NotSupportedException();
        public Task<RegulatoryAuthority?> GetRegulatoryAuthorityAsync(RegulatoryAuthorityId id, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RegulatoryAuthority>> GetRegulatoryAuthoritiesAsync(CancellationToken c = default) => throw new NotSupportedException();
        public Task AddRegulatoryProvisionAsync(RegulatoryProvision p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<RegulatoryProvision?> GetRegulatoryProvisionAsync(RegulatoryProvisionId id, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RegulatoryProvision>> GetRegulatoryProvisionsAsync(RegulatoryAuthorityId id, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddRequirementAsync(Requirement r, CancellationToken c = default) => throw new NotSupportedException();
        public Task<Requirement?> GetRequirementAsync(RequirementId id, CancellationToken c = default) => throw new NotSupportedException();
    }


    private sealed class StubRegulatoryRepository :
        IRegulatoryRepository
    {
        private readonly Requirement _requirement;

        public StubRegulatoryRepository(Requirement requirement) =>
            _requirement = requirement;

        public Task<IReadOnlyList<Requirement>> GetRequirementsAsync(
            RegulatoryProvisionId provisionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Requirement>>(
                new[] { _requirement });

        public Task AddRegulatoryAuthorityAsync(RegulatoryAuthority a, CancellationToken c = default) => throw new NotSupportedException();
        public Task<RegulatoryAuthority?> GetRegulatoryAuthorityAsync(RegulatoryAuthorityId id, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RegulatoryAuthority>> GetRegulatoryAuthoritiesAsync(CancellationToken c = default) => throw new NotSupportedException();
        public Task AddRegulatoryProvisionAsync(RegulatoryProvision p, CancellationToken c = default) => throw new NotSupportedException();
        public Task<RegulatoryProvision?> GetRegulatoryProvisionAsync(RegulatoryProvisionId id, CancellationToken c = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<RegulatoryProvision>> GetRegulatoryProvisionsAsync(RegulatoryAuthorityId id, CancellationToken c = default) => throw new NotSupportedException();
        public Task AddRequirementAsync(Requirement r, CancellationToken c = default) => throw new NotSupportedException();
        public Task<Requirement?> GetRequirementAsync(RequirementId id, CancellationToken c = default) => throw new NotSupportedException();
    }

    private sealed class StubGuidanceRepository :
        IEvidenceRequirementGuidanceRepository
    {
        private readonly EvidenceRequirementGuidance _guidance;

        public StubGuidanceRepository(
            EvidenceRequirementGuidance guidance) =>
            _guidance = guidance;

        public Task<IReadOnlyList<EvidenceRequirementGuidance>>
            GetEvidenceRequirementGuidanceAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceRequirementGuidance>>(
                new[] { _guidance });

        public Task AddEvidenceRequirementGuidanceAsync(EvidenceRequirementGuidance g, CancellationToken c = default) => throw new NotSupportedException();
        public Task<EvidenceRequirementGuidance?> GetEvidenceRequirementGuidanceAsync(EvidenceRequirementGuidanceId id, CancellationToken c = default) => throw new NotSupportedException();
    }

    private sealed class EmptyGuidanceRepository :
        IEvidenceRequirementGuidanceRepository
    {
        public Task<IReadOnlyList<EvidenceRequirementGuidance>>
            GetEvidenceRequirementGuidanceAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceRequirementGuidance>>(
                Array.Empty<EvidenceRequirementGuidance>());

        public Task AddEvidenceRequirementGuidanceAsync(
            EvidenceRequirementGuidance guidance,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<EvidenceRequirementGuidance?>
            GetEvidenceRequirementGuidanceAsync(
                EvidenceRequirementGuidanceId guidanceId,
                CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

}

using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class EvidenceRecognitionTermServiceTests
{
    [Fact]
    public async Task AddAsync_AddsValidatedRecognitionTerm()
    {
        var requirement = CreateRequirement("requirement-add");
        var terms = new RecordingTermRepository();
        var service =
            new EvidenceRecognitionTermService(
                new StubRegulatoryRepository(requirement),
                terms);

        var result =
            await service.AddAsync(
                requirement.Id,
                "  sleep apnea  ",
                EvidenceRecognitionTermTypes.Phrase,
                EvidenceRecognitionRoles.Diagnosis,
                EvidenceClassifications.MedicalEvidence,
                "  requirement bootstrap  ");

        Assert.Equal(requirement.Id, result.RequirementId);
        Assert.Equal("sleep apnea", result.Term);
        Assert.Equal(
            EvidenceRecognitionTermTypes.Phrase,
            result.TermType);
        Assert.Equal(
            EvidenceRecognitionRoles.Diagnosis,
            result.RecognitionRole);
        Assert.Equal(
            EvidenceClassifications.MedicalEvidence,
            result.EvidenceClassification);
        Assert.Equal(
            "requirement bootstrap",
            result.AuthoritySource);
        Assert.Single(terms.Items);
    }

    [Fact]
    public async Task AddAsync_ReturnsExistingEquivalentTerm()
    {
        var requirement = CreateRequirement("requirement-existing");
        var existing =
            CreateTerm(
                "term-existing",
                requirement.Id,
                "CPAP");
        var terms = new RecordingTermRepository(existing);
        var service =
            new EvidenceRecognitionTermService(
                new StubRegulatoryRepository(requirement),
                terms);

        var result =
            await service.AddAsync(
                requirement.Id,
                "cpap",
                existing.TermType,
                existing.RecognitionRole,
                existing.EvidenceClassification,
                existing.AuthoritySource);

        Assert.Equal(existing.Id, result.Id);
        Assert.Single(terms.Items);
    }

    [Fact]
    public async Task AddAsync_RejectsMissingRequirement()
    {
        var service =
            new EvidenceRecognitionTermService(
                new StubRegulatoryRepository(),
                new RecordingTermRepository());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddAsync(
                new RequirementId("requirement-missing"),
                "sleep apnea",
                EvidenceRecognitionTermTypes.Phrase,
                EvidenceRecognitionRoles.Diagnosis,
                EvidenceClassifications.MedicalEvidence,
                "bootstrap"));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public async Task AddAsync_RejectsBlankTerm(string term)
    {
        var requirement = CreateRequirement("requirement-term");
        var service = CreateService(requirement);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(
                requirement.Id,
                term,
                EvidenceRecognitionTermTypes.Keyword,
                EvidenceRecognitionRoles.Diagnosis,
                null,
                "bootstrap"));
    }

    [Fact]
    public async Task AddAsync_RejectsUnsupportedTermType()
    {
        var requirement = CreateRequirement("requirement-type");
        var service = CreateService(requirement);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(
                requirement.Id,
                "sleep apnea",
                "Unsupported",
                EvidenceRecognitionRoles.Diagnosis,
                null,
                "bootstrap"));
    }

    [Fact]
    public async Task AddAsync_RejectsUnsupportedRecognitionRole()
    {
        var requirement = CreateRequirement("requirement-role");
        var service = CreateService(requirement);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(
                requirement.Id,
                "sleep apnea",
                EvidenceRecognitionTermTypes.Phrase,
                "Unsupported",
                null,
                "bootstrap"));
    }

    [Fact]
    public async Task AddAsync_RejectsUnsupportedClassification()
    {
        var requirement = CreateRequirement("requirement-classification");
        var service = CreateService(requirement);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(
                requirement.Id,
                "sleep apnea",
                EvidenceRecognitionTermTypes.Phrase,
                EvidenceRecognitionRoles.Diagnosis,
                "Unsupported",
                "bootstrap"));
    }

    [Fact]
    public async Task AddAsync_RejectsBlankAuthoritySource()
    {
        var requirement = CreateRequirement("requirement-authority");
        var service = CreateService(requirement);

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.AddAsync(
                requirement.Id,
                "sleep apnea",
                EvidenceRecognitionTermTypes.Phrase,
                EvidenceRecognitionRoles.Diagnosis,
                null,
                " "));
    }

    [Fact]
    public async Task AddAsync_RejectsMismatchedRepositoryRequirement()
    {
        var requirement = CreateRequirement("requirement-owner");
        var wrongRequirement =
            new RequirementId("requirement-other");
        var repository =
            new RecordingTermRepository(
                CreateTerm(
                    "term-wrong",
                    wrongRequirement,
                    "sleep apnea"));
        var service =
            new EvidenceRecognitionTermService(
                new StubRegulatoryRepository(requirement),
                repository);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.AddAsync(
                requirement.Id,
                "CPAP",
                EvidenceRecognitionTermTypes.Acronym,
                EvidenceRecognitionRoles.Diagnosis,
                EvidenceClassifications.MedicalEvidence,
                "bootstrap"));
    }

    private static EvidenceRecognitionTermService CreateService(
        Requirement requirement) =>
        new(
            new StubRegulatoryRepository(requirement),
            new RecordingTermRepository());

    private static Requirement CreateRequirement(string id) =>
        new()
        {
            Id = new RequirementId(id),
            RegulatoryProvisionId =
                new RegulatoryProvisionId("provision-test"),
            Description = "Required element."
        };

    private static EvidenceRecognitionTerm CreateTerm(
        string id,
        RequirementId requirementId,
        string term) =>
        new()
        {
            Id = new EvidenceRecognitionTermId(id),
            RequirementId = requirementId,
            Term = term,
            TermType = EvidenceRecognitionTermTypes.Acronym,
            RecognitionRole = EvidenceRecognitionRoles.Diagnosis,
            EvidenceClassification =
                EvidenceClassifications.MedicalEvidence,
            AuthoritySource = "bootstrap"
        };

    private sealed class RecordingTermRepository :
        IEvidenceRecognitionTermRepository
    {
        public RecordingTermRepository(
            params EvidenceRecognitionTerm[] terms) =>
            Items.AddRange(terms);

        public List<EvidenceRecognitionTerm> Items { get; } = [];

        public Task AddEvidenceRecognitionTermAsync(
            EvidenceRecognitionTerm term,
            CancellationToken cancellationToken = default)
        {
            Items.Add(term);
            return Task.CompletedTask;
        }

        public Task<EvidenceRecognitionTerm?>
            GetEvidenceRecognitionTermAsync(
                EvidenceRecognitionTermId termId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult(
                Items.FirstOrDefault(x => x.Id == termId));

        public Task<IReadOnlyList<EvidenceRecognitionTerm>>
            GetEvidenceRecognitionTermsAsync(
                RequirementId requirementId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvidenceRecognitionTerm>>(
                Items.ToArray());
    }

    private sealed class StubRegulatoryRepository :
        IRegulatoryRepository
    {
        private readonly Requirement[] _requirements;

        public StubRegulatoryRepository(
            params Requirement[] requirements) =>
            _requirements = requirements;

        public Task AddRegulatoryAuthorityAsync(
            RegulatoryAuthority authority,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RegulatoryAuthority?> GetRegulatoryAuthorityAsync(
            RegulatoryAuthorityId authorityId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RegulatoryAuthority?>(null);

        public Task<IReadOnlyList<RegulatoryAuthority>>
            GetRegulatoryAuthoritiesAsync(
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RegulatoryAuthority>>([]);

        public Task AddRegulatoryProvisionAsync(
            RegulatoryProvision provision,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<RegulatoryProvision?> GetRegulatoryProvisionAsync(
            RegulatoryProvisionId provisionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<RegulatoryProvision?>(null);

        public Task<IReadOnlyList<RegulatoryProvision>>
            GetRegulatoryProvisionsAsync(
                RegulatoryAuthorityId authorityId,
                CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RegulatoryProvision>>([]);

        public Task AddRequirementAsync(
            Requirement requirement,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Requirement?> GetRequirementAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                _requirements.FirstOrDefault(
                    x => x.Id == requirementId));

        public Task<IReadOnlyList<Requirement>> GetRequirementsAsync(
            RegulatoryProvisionId provisionId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<Requirement>>(
                _requirements
                    .Where(
                        x => x.RegulatoryProvisionId == provisionId)
                    .ToArray());
    }
}

using System.Reflection;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Regulatory;

namespace EMF.Tests;

public sealed class VeteransReviewerEvidenceSourceServiceTests
{
    [Fact]
    public void Constructor_RequiresDependencies()
    {
        Assert.Throws<ArgumentNullException>(
            () => new VeteransReviewerEvidenceSourceService(
                null!,
                Proxy<IMedicalLiteratureRepository>(),
                Proxy<IArtifactTextExtractor>()));

        Assert.Throws<ArgumentNullException>(
            () => new VeteransReviewerEvidenceSourceService(
                Proxy<IEvidenceRepository>(),
                null!,
                Proxy<IArtifactTextExtractor>()));

        Assert.Throws<ArgumentNullException>(
            () => new VeteransReviewerEvidenceSourceService(
                Proxy<IEvidenceRepository>(),
                Proxy<IMedicalLiteratureRepository>(),
                null!));
    }

    [Fact]
    public async Task GetAsync_IncludesClassifiedAndLiteratureArtifacts()
    {
        var classifiedId = new ArtifactId("classified-1");
        var literatureId = new ArtifactId("literature-1");
        var details = CreateDetails(CreateLiterature("requirement-1", "study-1"));

        var classifications =
            new[]
            {
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-1"),
                    ArtifactId = classifiedId,
                    ClaimIssueId = details.ClaimIssue.Id,
                    Classification = EvidenceClassifications.MedicalEvidence
                }
            };

        var service =
            CreateService(
                id => CreateArtifact(id),
                [classifiedId, literatureId, classifiedId],
                id => $"text:{id.Value}");

        var result =
            await service.GetAsync(details, classifications);

        Assert.Equal(2, result.Count);
        Assert.Equal(classifiedId, result[0].ArtifactId);
        Assert.Equal(
            EvidenceClassifications.MedicalEvidence,
            Assert.Single(result[0].Classifications));
        Assert.Equal(literatureId, result[1].ArtifactId);
        Assert.Empty(result[1].Classifications);
        Assert.Equal("text:literature-1", result[1].Text);
    }

    [Fact]
    public async Task GetAsync_RejectsRequirementMismatch()
    {
        var details =
            CreateDetails(
                CreateLiterature(
                    "requirement-1",
                    "study-1",
                    associationRequirementId: "requirement-other"));

        var service =
            CreateService(
                CreateArtifact,
                [],
                id => $"text:{id.Value}");

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(details, []));

        Assert.Contains("requirement mismatch", ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsLiteratureSourceIdentityMismatch()
    {
        var details =
            CreateDetails(
                CreateLiterature(
                    "requirement-1",
                    "study-1",
                    associationSourceId: "study-other"));

        var service =
            CreateService(
                CreateArtifact,
                [],
                id => $"text:{id.Value}");

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(details, []));

        Assert.Contains("source identity mismatch", ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsMissingArtifact()
    {
        var artifactId = new ArtifactId("missing-1");
        var details = CreateDetails();

        var classifications =
            new[]
            {
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-1"),
                    ArtifactId = artifactId,
                    ClaimIssueId = details.ClaimIssue.Id,
                    Classification = EvidenceClassifications.MedicalEvidence
                }
            };

        var service =
            CreateService(
                _ => null,
                [],
                _ => "unused");

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(details, classifications));

        Assert.Contains("artifact not found", ex.Message);
        Assert.Contains(artifactId.Value, ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsWrongReturnedArtifactIdentity()
    {
        var requestedId = new ArtifactId("requested-1");
        var details = CreateDetails();

        var classifications =
            new[]
            {
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-1"),
                    ArtifactId = requestedId,
                    ClaimIssueId = details.ClaimIssue.Id,
                    Classification = EvidenceClassifications.MedicalEvidence
                }
            };

        var service =
            CreateService(
                _ => CreateArtifact(new ArtifactId("other-1")),
                [],
                _ => "unused");

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(details, classifications));

        Assert.Contains("artifact identity mismatch", ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsBlankExtractedText()
    {
        var artifactId = new ArtifactId("artifact-1");
        var details = CreateDetails();

        var classifications =
            new[]
            {
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-1"),
                    ArtifactId = artifactId,
                    ClaimIssueId = details.ClaimIssue.Id,
                    Classification = EvidenceClassifications.MedicalEvidence
                }
            };

        var service =
            CreateService(
                CreateArtifact,
                [],
                _ => " ");

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(details, classifications));

        Assert.Contains("Unable to extract reviewer evidence", ex.Message);
        Assert.Contains(artifactId.Value, ex.Message);
    }

    private static VeteransReviewerEvidenceSourceService CreateService(
        Func<ArtifactId, Artifact?> artifactLookup,
        IReadOnlyList<ArtifactId> literatureArtifactIds,
        Func<ArtifactId, string?> textLookup) =>
        new(
            Proxy<IEvidenceRepository>(
                (method, args) =>
                    method.Name == "GetArtifactAsync"
                        ? Task.FromResult<Artifact?>(
                            artifactLookup((ArtifactId)args[0]!))
                        : throw new NotSupportedException(method.Name)),
            Proxy<IMedicalLiteratureRepository>(
                (method, _) =>
                    method.Name == "GetArtifactIdsAsync"
                        ? Task.FromResult(literatureArtifactIds)
                        : throw new NotSupportedException(method.Name)),
            Proxy<IArtifactTextExtractor>(
                (method, args) =>
                    method.Name == "ExtractTextAsync"
                        ? Task.FromResult<string?>(
                            textLookup((ArtifactId)args[0]!))
                        : throw new NotSupportedException(method.Name)));

    private static Artifact CreateArtifact(ArtifactId id) =>
        new()
        {
            Id = id,
            Name = $"{id.Value}.txt",
            ArtifactType = "Text"
        };

    private static ClaimIssueAdjudicationDetails CreateDetails(
        ServiceConnectionBasisRequirementDetails? requirement = null)
    {
        var issue =
            new ClaimIssue
            {
                Id = new ClaimIssueId("issue-1"),
                ClaimId = new ClaimId("claim-1"),
                ClaimIssueType = "ServiceConnection"
            };

        var theory =
            new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("theory-1"),
                ClaimIssueId = issue.Id,
                TheoryType = "Secondary"
            };

        var basis =
            new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-1"),
                ClaimIssueId = issue.Id,
                ServiceConnectionTheoryId = theory.Id
            };

        if (requirement is not null)
        {
            requirement =
                new ServiceConnectionBasisRequirementDetails
                {
                    Basis = basis,
                    Requirement = requirement.Requirement,
                    RegulatoryProvision = requirement.RegulatoryProvision,
                    Responsiveness = requirement.Responsiveness,
                    DevelopmentChecklist = requirement.DevelopmentChecklist,
                    MedicalLiterature = requirement.MedicalLiterature
                };
        }

        return new ClaimIssueAdjudicationDetails
        {
            ClaimIssue = issue,
            ClaimedConditions = [],
            ServiceConnectionTheories = [theory],
            ServiceConnectionBases = [basis],
            ServiceConnectedConditions = [],
            ServiceEvents = [],
            Requirements =
                requirement is null
                    ? []
                    : [requirement],
            Evidence = new ClaimIssueEvidenceDetails
            {
                ClaimIssue = issue,
                Checklist = new ClaimIssueEvidenceChecklist
                {
                    ClaimIssueId = issue.Id,
                    RequirementChecklists = []
                },
                DevelopmentPlans = []
            },
            Timeline = []
        };
    }

    private static ServiceConnectionBasisRequirementDetails CreateLiterature(
        string requirementId,
        string sourceId,
        string? associationRequirementId = null,
        string? associationSourceId = null)
    {
        var requirement =
            new Requirement
            {
                Id = new RequirementId(requirementId),
                RegulatoryProvisionId =
                    new RegulatoryProvisionId("provision-1"),
                Description = "Secondary service connection requirement"
            };

        var source =
            new MedicalLiteratureSource
            {
                Id = new MedicalLiteratureSourceId(sourceId),
                Title = "VA sleep apnea study",
                Authors = "VA Researchers",
                Publication = "Example Journal",
                PublicationYear = 2026,
                VaAffiliated = true,
                VaFunded = true,
                PeerReviewed = true
            };

        return new ServiceConnectionBasisRequirementDetails
        {
            Basis = new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("placeholder-basis"),
                ClaimIssueId = new ClaimIssueId("placeholder-issue"),
                ServiceConnectionTheoryId =
                    new ServiceConnectionTheoryId("placeholder-theory")
            },
            Requirement = requirement,
            RegulatoryProvision =
                new RegulatoryProvision
                {
                    Id = requirement.RegulatoryProvisionId,
                    RegulatoryAuthorityId =
                        new RegulatoryAuthorityId("authority-1"),
                    ProvisionType = "Regulation",
                    Citation = "38 CFR 3.310"
                },
            Responsiveness =
                new RequirementEvidenceResponsivenessAssessment
                {
                    RequirementId = requirement.Id,
                    Items = []
                },
            DevelopmentChecklist =
                new EvidenceDevelopmentChecklist
                {
                    RequirementId = requirement.Id,
                    Items = []
                },
            MedicalLiterature =
            [
                new RequirementMedicalLiteratureDetails
                {
                    Association =
                        new RequirementMedicalLiterature
                        {
                            RequirementId =
                                new RequirementId(
                                    associationRequirementId ??
                                    requirementId),
                            MedicalLiteratureSourceId =
                                new MedicalLiteratureSourceId(
                                    associationSourceId ??
                                    sourceId),
                            GuidanceRole =
                                EvidenceGuidanceRoles.SupportsRequirement,
                            Description = "Supports the medical mechanism."
                        },
                    Source = source
                }
            ]
        };
    }

    private static T Proxy<T>(
        Func<MethodInfo, object?[], object?>? handler = null)
        where T : class
    {
        var proxy = DispatchProxy.Create<T, TestProxy>();
        ((TestProxy)(object)proxy).Handler =
            handler ??
            ((method, _) =>
                throw new NotSupportedException(method.Name));
        return proxy;
    }

    private class TestProxy : DispatchProxy
    {
        public required Func<MethodInfo, object?[], object?> Handler
        {
            get;
            set;
        }

        protected override object? Invoke(
            MethodInfo? targetMethod,
            object?[]? args) =>
            Handler(
                targetMethod ??
                    throw new InvalidOperationException(),
                args ?? []);
    }
}

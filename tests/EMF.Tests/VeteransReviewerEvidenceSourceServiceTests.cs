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
    public async Task GetAsync_UsesActiveSupersedingClassifiedArtifact()
    {
        var unsignedId = new ArtifactId("statement-unsigned");
        var signedId = new ArtifactId("statement-signed");
        var details = CreateDetails();

        var classifications =
            new[]
            {
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-1"),
                    ArtifactId = unsignedId,
                    ClaimIssueId = details.ClaimIssue.Id,
                    Classification = EvidenceClassifications.LayEvidence
                }
            };

        var supersedes =
            new Relationship
            {
                SourceArtifactId = signedId,
                TargetArtifactId = unsignedId,
                RelationshipType = RelationshipTypes.Supersedes
            };

        var service =
            CreateService(
                CreateArtifact,
                [],
                id => $"text:{id.Value}",
                relationshipLookup:
                    id =>
                        id == unsignedId || id == signedId
                            ? [supersedes]
                            : []);

        var result =
            await service.GetAsync(details, classifications);

        var source = Assert.Single(result);
        Assert.Equal(signedId, source.ArtifactId);
        Assert.Equal("text:statement-signed", source.Text);
        Assert.Equal(
            EvidenceClassifications.LayEvidence,
            Assert.Single(source.Classifications));
    }

    [Fact]
    public async Task GetAsync_MergesClassificationsOntoActiveSupersedingArtifact()
    {
        var unsignedId = new ArtifactId("statement-unsigned");
        var signedId = new ArtifactId("statement-signed");
        var details = CreateDetails();

        var classifications =
            new[]
            {
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-unsigned"),
                    ArtifactId = unsignedId,
                    ClaimIssueId = details.ClaimIssue.Id,
                    Classification = EvidenceClassifications.LayEvidence
                },
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-signed"),
                    ArtifactId = signedId,
                    ClaimIssueId = details.ClaimIssue.Id,
                    Classification = EvidenceClassifications.MedicalEvidence
                }
            };

        var supersedes =
            new Relationship
            {
                SourceArtifactId = signedId,
                TargetArtifactId = unsignedId,
                RelationshipType = RelationshipTypes.Supersedes
            };

        var service =
            CreateService(
                CreateArtifact,
                [],
                id => $"text:{id.Value}",
                relationshipLookup:
                    id =>
                        id == unsignedId || id == signedId
                            ? [supersedes]
                            : []);

        var result =
            await service.GetAsync(details, classifications);

        var source = Assert.Single(result);
        Assert.Equal(signedId, source.ArtifactId);
        Assert.Equal(2, source.Classifications.Count);
        Assert.Contains(
            EvidenceClassifications.LayEvidence,
            source.Classifications);
        Assert.Contains(
            EvidenceClassifications.MedicalEvidence,
            source.Classifications);
    }

    [Fact]
    public async Task GetAsync_PreservesIndependentLayEvidenceWhenClassifiedArtifactIsSuperseded()
    {
        var spouseId = new ArtifactId("statement-spouse");
        var personalV1Id = new ArtifactId("statement-personal-v1");
        var personalV2Id = new ArtifactId("statement-personal-v2");
        var details = CreateDetails();

        var classifications =
            new[]
            {
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-spouse"),
                    ArtifactId = spouseId,
                    ClaimIssueId = details.ClaimIssue.Id,
                    Classification = EvidenceClassifications.LayEvidence
                },
                new EvidenceClassification
                {
                    Id = new EvidenceClassificationId("classification-personal"),
                    ArtifactId = personalV1Id,
                    ClaimIssueId = details.ClaimIssue.Id,
                    Classification = EvidenceClassifications.LayEvidence
                }
            };

        var supersedes =
            new Relationship
            {
                SourceArtifactId = personalV2Id,
                TargetArtifactId = personalV1Id,
                RelationshipType = RelationshipTypes.Supersedes
            };

        var service =
            CreateService(
                CreateArtifact,
                [],
                id => $"text:{id.Value}",
                relationshipLookup:
                    id =>
                        id == personalV1Id || id == personalV2Id
                            ? [supersedes]
                            : []);

        var result =
            await service.GetAsync(details, classifications);

        Assert.Equal(2, result.Count);

        Assert.Contains(
            result,
            source =>
                source.ArtifactId == spouseId &&
                source.Classifications.Contains(
                    EvidenceClassifications.LayEvidence));

        Assert.Contains(
            result,
            source =>
                source.ArtifactId == personalV2Id &&
                source.Classifications.Contains(
                    EvidenceClassifications.LayEvidence));

        Assert.DoesNotContain(
            result,
            source => source.ArtifactId == personalV1Id);
    }

    [Fact]
    public async Task GetAsync_PrefersReviewedLiteratureArtifact()
    {
        var reviewedArtifactId =
            new ArtifactId("literature-reviewed");
        var otherArtifactId =
            new ArtifactId("literature-other");
        var requirement =
            CreateLiterature("requirement-1", "study-1");
        var details = CreateDetails(requirement);

        var service =
            CreateService(
                CreateArtifact,
                [otherArtifactId, reviewedArtifactId],
                id => $"text:{id.Value}",
                [CreateReviewedClassification(
                    requirement,
                    reviewedArtifactId)]);

        var result =
            await service.GetAsync(details, []);

        var source = Assert.Single(result);
        Assert.Equal(reviewedArtifactId, source.ArtifactId);
        Assert.Equal(
            "text:literature-reviewed",
            source.Text);

        var reviewed =
            Assert.Single(
                source.ReviewedMedicalLiteratureClassifications);

        Assert.Equal(
            reviewedArtifactId,
            reviewed.ArtifactId);
        Assert.Equal(
            "requirement-1",
            reviewed.Association.RequirementId.Value);
        Assert.Equal(
            "Reviewed excerpt",
            Assert.Single(reviewed.SourceExcerpts).Text);
    }

    [Fact]
    public async Task GetAsync_RejectsReviewedArtifactOutsideSource()
    {
        var reviewedArtifactId =
            new ArtifactId("literature-reviewed");
        var requirement =
            CreateLiterature("requirement-1", "study-1");
        var details = CreateDetails(requirement);

        var service =
            CreateService(
                CreateArtifact,
                [new ArtifactId("literature-other")],
                id => $"text:{id.Value}",
                [CreateReviewedClassification(
                    requirement,
                    reviewedArtifactId)]);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(details, []));

        Assert.Contains(
            "reviewed artifact is not associated",
            ex.Message);
    }

    [Fact]
    public async Task GetAsync_RejectsReviewedExcerptArtifactMismatch()
    {
        var reviewedArtifactId =
            new ArtifactId("literature-reviewed");
        var requirement =
            CreateLiterature("requirement-1", "study-1");
        var details = CreateDetails(requirement);
        var reviewed =
            CreateReviewedClassification(
                requirement,
                reviewedArtifactId,
                new ArtifactId("excerpt-other"));

        var service =
            CreateService(
                CreateArtifact,
                [reviewedArtifactId],
                id => $"text:{id.Value}",
                [reviewed]);

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(details, []));

        Assert.Contains(
            "reviewed excerpt artifact identity mismatch",
            ex.Message);
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

    [Fact]
    public async Task GetAsync_RejectsClassificationClaimIssueMismatch()
    {
        var details = CreateDetails();

        var classifications =
            new[]
            {
                new EvidenceClassification
                {
                    Id =
                        new EvidenceClassificationId(
                            "classification-wrong-issue"),
                    ArtifactId =
                        new ArtifactId("artifact-wrong-issue"),
                    ClaimIssueId =
                        new ClaimIssueId("issue-other"),
                    Classification =
                        EvidenceClassifications.MedicalEvidence
                }
            };

        var service =
            CreateService(
                CreateArtifact,
                [],
                id => $"text:{id.Value}");

        var ex =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => service.GetAsync(
                    details,
                    classifications));

        Assert.Contains(
            "classification claim issue mismatch",
            ex.Message);
    }

    private static VeteransReviewerEvidenceSourceService CreateService(
        Func<ArtifactId, Artifact?> artifactLookup,
        IReadOnlyList<ArtifactId> literatureArtifactIds,
        Func<ArtifactId, string?> textLookup,
        IReadOnlyList<ReviewedMedicalLiteratureClassification>?
            reviewedClassifications = null,
        Func<ArtifactId, IReadOnlyList<Relationship>>?
            relationshipLookup = null) =>
        new(
            Proxy<IEvidenceRepository>(
                (method, args) =>
                    method.Name == "GetArtifactAsync"
                        ? Task.FromResult<Artifact?>(
                            artifactLookup((ArtifactId)args[0]!))
                        : method.Name == "GetRelationshipsAsync"
                            ? Task.FromResult<IReadOnlyList<Relationship>>(
                                relationshipLookup?.Invoke(
                                    (ArtifactId)args[0]!) ?? [])
                            : throw new NotSupportedException(method.Name)),
            Proxy<IMedicalLiteratureRepository>(
                (method, _) =>
                    method.Name switch
                    {
                        "GetArtifactIdsAsync" =>
                            Task.FromResult(literatureArtifactIds),
                        "GetReviewedClassificationsAsync" =>
                            Task.FromResult<IReadOnlyList<
                                ReviewedMedicalLiteratureClassification>>(
                                    reviewedClassifications ?? []),
                        _ =>
                            throw new NotSupportedException(method.Name)
                    }),
            Proxy<IArtifactTextExtractor>(
                (method, args) =>
                    method.Name == "ExtractTextAsync"
                        ? Task.FromResult<string?>(
                            textLookup((ArtifactId)args[0]!))
                        : throw new NotSupportedException(method.Name)));

    private static ReviewedMedicalLiteratureClassification
        CreateReviewedClassification(
            ServiceConnectionBasisRequirementDetails requirement,
            ArtifactId artifactId,
            ArtifactId? excerptArtifactId = null)
    {
        var association =
            Assert.Single(requirement.MedicalLiterature).Association;
        var timestamp =
            new DateTimeOffset(
                2026, 9, 12, 10, 0, 0, TimeSpan.Zero);

        return new ReviewedMedicalLiteratureClassification
        {
            Association = association,
            ArtifactId = artifactId,
            PromotedBy = "reviewer",
            PromotedUtc = timestamp,
            ReviewedBy = "reviewer",
            ReviewedUtc = timestamp,
            IntelligenceOutput = "{}",
            CapabilityId = "literature-classification",
            ProviderId = "provider",
            CorrelationId = "correlation-1",
            EngineName = "engine",
            StartedUtc = timestamp,
            CompletedUtc = timestamp,
            RequiresReview = false,
            Warnings = [],
            SourceExcerpts =
            [
                new MedicalLiteratureSourceExcerpt
                {
                    ArtifactId = excerptArtifactId ?? artifactId,
                    Text = "Reviewed excerpt"
                }
            ]
        };
    }

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

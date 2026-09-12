using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Extensions.VeteransClaims.Services;

namespace EMF.Tests;

public sealed class MedicalLiteratureClassificationValidatorTests
{
    [Fact]
    public void AcceptsGroundedClassification()
    {
        const string text = "Alpha beta gamma.";
        var artifactId = new ArtifactId("artifact-literature");
        var sourceId =
            new MedicalLiteratureSourceId("source-literature");

        var requirement = new Requirement
        {
            Id = new RequirementId("requirement-a"),
            RegulatoryProvisionId =
                new RegulatoryProvisionId("provision-a"),
            Description = "Candidate requirement."
        };

        var proposal = new MedicalLiteratureClassificationProposal
        {
            MedicalLiteratureSourceId = sourceId,
            ArtifactId = artifactId,
            Classifications =
            [
                new MedicalLiteratureRequirementClassification
                {
                    RequirementId = requirement.Id,
                    GuidanceRole =
                        EvidenceGuidanceRoles.SupportsRequirement,
                    Description = "Supports requirement.",
                    SourceExcerpts =
                    [
                        new MedicalLiteratureSourceExcerpt
                        {
                            ArtifactId = artifactId,
                            Text = "beta",
                            StartOffset = 6,
                            Length = 4
                        }
                    ]
                }
            ]
        };

        new MedicalLiteratureClassificationValidator()
            .ValidateAgainstSource(
                proposal,
                sourceId,
                artifactId,
                [requirement],
                text);
    }

    [Fact]
    public void AcceptsNoClassifications()
    {
        var artifactId = new ArtifactId("artifact-literature");
        var sourceId =
            new MedicalLiteratureSourceId("source-literature");

        var proposal = new MedicalLiteratureClassificationProposal
        {
            MedicalLiteratureSourceId = sourceId,
            ArtifactId = artifactId,
            Classifications = []
        };

        new MedicalLiteratureClassificationValidator()
            .ValidateAgainstSource(
                proposal,
                sourceId,
                artifactId,
                [],
                "Article text.");
    }

    [Fact]
    public void RejectsUnknownRequirement()
    {
        var artifactId = new ArtifactId("artifact-literature");
        var sourceId =
            new MedicalLiteratureSourceId("source-literature");

        var proposal = new MedicalLiteratureClassificationProposal
        {
            MedicalLiteratureSourceId = sourceId,
            ArtifactId = artifactId,
            Classifications =
            [
                new MedicalLiteratureRequirementClassification
                {
                    RequirementId = new("requirement-other"),
                    GuidanceRole = EvidenceGuidanceRoles.SupportsRequirement,
                    Description = "Description.",
                    SourceExcerpts = []
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() =>
            new MedicalLiteratureClassificationValidator()
                .ValidateAgainstSource(
                    proposal, sourceId, artifactId, [], "Article text."));
    }

    [Fact]
    public void RejectsUnsupportedRole()
    {
        var artifactId = new ArtifactId("artifact-literature");
        var sourceId =
            new MedicalLiteratureSourceId("source-literature");

        var requirement = new Requirement
        {
            Id = new("requirement-a"),
            RegulatoryProvisionId = new("provision-a"),
            Description = "Candidate."
        };

        var proposal = new MedicalLiteratureClassificationProposal
        {
            MedicalLiteratureSourceId = sourceId,
            ArtifactId = artifactId,
            Classifications =
            [
                new MedicalLiteratureRequirementClassification
                {
                    RequirementId = requirement.Id,
                    GuidanceRole = "InventedRole",
                    Description = "Description.",
                    SourceExcerpts = []
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() =>
            new MedicalLiteratureClassificationValidator()
                .ValidateAgainstSource(
                    proposal, sourceId, artifactId,
                    [requirement], "Article text."));
    }

    [Fact]
    public void RejectsDuplicateClassification()
    {
        var artifactId = new ArtifactId("artifact-literature");
        var sourceId = new MedicalLiteratureSourceId("source-literature");

        var requirement = new Requirement
        {
            Id = new("requirement-a"),
            RegulatoryProvisionId = new("provision-a"),
            Description = "Candidate."
        };

        MedicalLiteratureRequirementClassification Item() => new()
        {
            RequirementId = requirement.Id,
            GuidanceRole = EvidenceGuidanceRoles.SupportsRequirement,
            Description = "Description.",
            SourceExcerpts =
            [
                new()
                {
                    ArtifactId = artifactId,
                    Text = "Article",
                    StartOffset = 0,
                    Length = 7
                }
            ]
        };

        var proposal = new MedicalLiteratureClassificationProposal
        {
            MedicalLiteratureSourceId = sourceId,
            ArtifactId = artifactId,
            Classifications = [Item(), Item()]
        };

        Assert.Throws<InvalidOperationException>(() =>
            new MedicalLiteratureClassificationValidator()
                .ValidateAgainstSource(
                    proposal, sourceId, artifactId,
                    [requirement], "Article text."));
    }

    [Fact]
    public void RejectsExcerptFromDifferentArtifact()
    {
        var artifactId = new ArtifactId("artifact-literature");
        var sourceId = new MedicalLiteratureSourceId("source-literature");

        var proposal = new MedicalLiteratureClassificationProposal
        {
            MedicalLiteratureSourceId = sourceId,
            ArtifactId = artifactId,
            Classifications =
            [
                new()
                {
                    RequirementId = new("requirement-a"),
                    GuidanceRole = EvidenceGuidanceRoles.Clarifies,
                    Description = "Description.",
                    SourceExcerpts =
                    [
                        new()
                        {
                            ArtifactId = new("artifact-other"),
                            Text = "Article",
                            StartOffset = 0,
                            Length = 7
                        }
                    ]
                }
            ]
        };

        var requirement = new Requirement
        {
            Id = new("requirement-a"),
            RegulatoryProvisionId = new("provision-a"),
            Description = "Candidate."
        };

        Assert.Throws<InvalidOperationException>(() =>
            new MedicalLiteratureClassificationValidator()
                .ValidateAgainstSource(
                    proposal, sourceId, artifactId,
                    [requirement], "Article text."));
    }

    [Fact]
    public void RejectsInvalidExcerptRange()
    {
        var artifactId = new ArtifactId("artifact-literature");
        var sourceId = new MedicalLiteratureSourceId("source-literature");
        var requirement = new Requirement
        {
            Id = new("requirement-a"),
            RegulatoryProvisionId = new("provision-a"),
            Description = "Candidate."
        };

        var proposal = new MedicalLiteratureClassificationProposal
        {
            MedicalLiteratureSourceId = sourceId,
            ArtifactId = artifactId,
            Classifications =
            [
                new()
                {
                    RequirementId = requirement.Id,
                    GuidanceRole = EvidenceGuidanceRoles.Corroborates,
                    Description = "Description.",
                    SourceExcerpts =
                    [
                        new()
                        {
                            ArtifactId = artifactId,
                            Text = "Article",
                            StartOffset = 100,
                            Length = 7
                        }
                    ]
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() =>
            new MedicalLiteratureClassificationValidator()
                .ValidateAgainstSource(
                    proposal, sourceId, artifactId,
                    [requirement], "Article text."));
    }

    [Fact]
    public void RejectsExcerptTextMismatch()
    {
        var artifactId = new ArtifactId("artifact-literature");
        var sourceId = new MedicalLiteratureSourceId("source-literature");
        var requirement = new Requirement
        {
            Id = new("requirement-a"),
            RegulatoryProvisionId = new("provision-a"),
            Description = "Candidate."
        };

        var proposal = new MedicalLiteratureClassificationProposal
        {
            MedicalLiteratureSourceId = sourceId,
            ArtifactId = artifactId,
            Classifications =
            [
                new()
                {
                    RequirementId = requirement.Id,
                    GuidanceRole = EvidenceGuidanceRoles.Clarifies,
                    Description = "Description.",
                    SourceExcerpts =
                    [
                        new()
                        {
                            ArtifactId = artifactId,
                            Text = "Wrong",
                            StartOffset = 0,
                            Length = 5
                        }
                    ]
                }
            ]
        };

        Assert.Throws<InvalidOperationException>(() =>
            new MedicalLiteratureClassificationValidator()
                .ValidateAgainstSource(
                    proposal, sourceId, artifactId,
                    [requirement], "Article text."));
    }
}

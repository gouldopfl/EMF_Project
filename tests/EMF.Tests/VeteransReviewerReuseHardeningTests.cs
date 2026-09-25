using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Clinical;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class VeteransReviewerReuseHardeningTests
{
    internal static ClaimIssueAdjudicationDetails Details(string seed, bool reverse = false,
        string conditionName = "Bilateral pes planus", string requirementText = "Factual medical evidence")
    {
        var issue = new ClaimIssue { Id = new(seed + "issue"), ClaimId = new(seed + "claim"), ClaimIssueType = "ServiceConnection" };
        var theory = new ServiceConnectionTheory { Id = new(seed + "theory"), ClaimIssueId = issue.Id, TheoryType = "Secondary" };
        var basis = new ServiceConnectionBasis { Id = new(seed + "basis"), ClaimIssueId = issue.Id, ServiceConnectionTheoryId = theory.Id };
        var reqId = new RequirementId(seed + "requirement");
        var checklist = new EvidenceDevelopmentChecklist { RequirementId = reqId, Items = [] };
        var conditions = new[]
        {
            new EMF.Extensions.VeteransClaims.Models.Conditions.ClaimedCondition { Id = new(seed + "condition-one"), Name = conditionName, ClaimIssueId = issue.Id },
            new EMF.Extensions.VeteransClaims.Models.Conditions.ClaimedCondition { Id = new(seed + "condition-two"), Name = "Lumbar degenerative disease", ClaimIssueId = issue.Id }
        };
        return new()
        {
            ClaimIssue = issue, ClaimedConditions = reverse ? conditions.Reverse().ToArray() : conditions,
            ServiceConnectionTheories = [theory], ServiceConnectionBases = [basis],
            ServiceConnectedConditions = [], ServiceEvents = [],
            ClaimedConditionBases = conditions.Select(x => new ServiceConnectionBasisClaimedConditionDetails { Basis = basis, ClaimedCondition = x }).ToArray(),
            Requirements = [new()
            {
                Basis = basis,
                Requirement = new() { Id = reqId, RegulatoryProvisionId = new(seed + "provision"), Description = requirementText },
                RegulatoryProvision = new() { Id = new(seed + "provision"), RegulatoryAuthorityId = new(seed + "authority"), Citation = "38 CFR 3.310", ProvisionType = "Regulation" },
                Responsiveness = new() { RequirementId = reqId, Items = [] }, DevelopmentChecklist = checklist
            }],
            Evidence = new() { ClaimIssue = issue, Checklist = new() { ClaimIssueId = issue.Id, RequirementChecklists = [checklist] }, DevelopmentPlans = [] },
            Timeline = []
        };
    }

    private static VeteransReviewerEvidenceSource Source(string id, string text = "Observed gait difficulty.", bool reverse = false) => new()
    {
        ArtifactId = new(id), ArtifactName = "Clinical note", EvidenceTitle = "Bilateral pes planus",
        Classifications = reverse ? ["Treatment", "MedicalEvidence"] : ["MedicalEvidence", "Treatment"], Text = text
    };

    private static string Key(ClaimIssueAdjudicationDetails details, VeteransReviewerEvidenceSource[] sources,
        ClinicalProgressionEvent[]? events = null, EvidenceRecognitionTerm[]? terms = null) =>
        VeteransReviewerPackageIntelligenceService.CreateReuseKey(details, sources, [], terms ?? [], events ?? []);

    [Fact]
    public void ReuseKey_IgnoresGraphIdentityAndCollectionOrderButRetainsFacts()
    {
        string Run(string seed, bool reverse, string text = "Observed gait difficulty.", string condition = "Bilateral pes planus")
        {
            var details = Details(seed, reverse, condition);
            var first = Source(seed + "source-one", text, reverse);
            var second = Source(seed + "source-two", "Independent evaluation requested.", reverse);
            var term = new EvidenceRecognitionTerm
            {
                Id = new(seed + "term"), RequirementId = details.Requirements[0].Requirement.Id,
                Term = "Bilateral pes planus", TermType = "Phrase", RecognitionRole = "FactualEvidence",
                EvidenceClassification = "MedicalEvidence", AuthoritySource = "Reviewed"
            };
            var progression = new ClinicalProgressionEvent
            {
                Id = new(seed + "event"), ClaimIssueId = details.ClaimIssue.Id, SourceArtifactId = first.ArtifactId,
                EventDate = new(2026, 1, 1), RecordTitle = "Bilateral pes planus", EventType = "DiagnosticFinding",
                Summary = "Observed gait difficulty.", SourceStartPage = 1, SourceEndPage = 1
            };
            return Key(details, reverse ? [second, first] : [first, second], [progression], [term]);
        }
        Assert.Equal(Run("first", false), Run("replacement", true));
        Assert.NotEqual(Run("first", false), Run("first", false, "Different factual evidence."));
        Assert.NotEqual(Run("first", false), Run("first", false, condition: "Changed diagnosis"));
    }

    [Fact]
    public void ReuseKey_PreservesFactToBasisRelationshipsWhenBasesOtherwiseMatch()
    {
        ClaimIssueAdjudicationDetails Graph(bool swap)
        {
            var original = Details("original");
            var first = original.ServiceConnectionBases[0];
            var second = new ServiceConnectionBasis
            {
                Id = new("second-basis"), ClaimIssueId = first.ClaimIssueId,
                ServiceConnectionTheoryId = first.ServiceConnectionTheoryId
            };
            var firstRequirement = original.Requirements[0];
            var secondRequirement = new ServiceConnectionBasisRequirementDetails
            {
                Basis = second,
                Requirement = new() { Id = new("second-requirement"),
                    RegulatoryProvisionId = firstRequirement.RegulatoryProvision.Id, Description = "Different required fact" },
                RegulatoryProvision = firstRequirement.RegulatoryProvision,
                Responsiveness = new() { RequirementId = new("second-requirement"), Items = [] },
                DevelopmentChecklist = new() { RequirementId = new("second-requirement"), Items = [] }
            };
            return new()
            {
                ClaimIssue = original.ClaimIssue, ClaimedConditions = original.ClaimedConditions,
                ServiceConnectionTheories = original.ServiceConnectionTheories,
                ServiceConnectionBases = [first, second], ServiceConnectedConditions = [], ServiceEvents = [],
                ClaimedConditionBases = original.ClaimedConditions.SelectMany(x => new[]
                {
                    new ServiceConnectionBasisClaimedConditionDetails { Basis = first, ClaimedCondition = x },
                    new ServiceConnectionBasisClaimedConditionDetails { Basis = second, ClaimedCondition = x }
                }).ToArray(),
                Requirements = [firstRequirement, secondRequirement],
                BasisArtifacts =
                [
                    new() { Basis = first, ArtifactId = new(swap ? "two" : "one"), Role = "Evidence" },
                    new() { Basis = second, ArtifactId = new(swap ? "one" : "two"), Role = "Evidence" }
                ],
                Evidence = original.Evidence, Timeline = []
            };
        }
        var sources = new[] { Source("one", "First fact"), Source("two", "Different second fact") };
        Assert.NotEqual(Key(Graph(false), sources), Key(Graph(true), sources));
    }

    [Fact]
    public void ReuseKey_RejectsConflictingValuesForOneArtifactIdentity()
    {
        Assert.Throws<InvalidOperationException>(() => Key(Details("a"), [Source("same", "first"), Source("same", "conflicting")]));
    }

    [Fact]
    public void ReuseKey_RejectsUnresolvedRecognitionRequirement()
    {
        var term = new EvidenceRecognitionTerm
        {
            Id = new("term"), RequirementId = new("missing"), Term = "gait", TermType = "Keyword",
            RecognitionRole = "FactualEvidence", EvidenceClassification = "MedicalEvidence", AuthoritySource = "Reviewed"
        };
        Assert.Throws<InvalidOperationException>(() => Key(Details("a"), [], terms: [term]));
    }

    [Fact]
    public void ReuseKey_RetainsDuplicateSourceMultiplicityAndExactTextBoundaries()
    {
        var details = Details("a");
        Assert.NotEqual(Key(details, [Source("one")]), Key(details, [Source("one"), Source("two")]));
        Assert.NotEqual(Key(details, [Source("one", "first\nsecond")]), Key(details, [Source("one", "first second")]));
        Assert.NotEqual(Key(details, [Source("one", "first|second")]), Key(details, [Source("one", "first"), Source("two", "second")]));
    }
}

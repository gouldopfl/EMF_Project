using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class EvidenceRecognitionTermProposalServiceTests
{
    [Fact]
    public async Task ProposeAsync_MapsGroundedStructuredTerms()
    {
        var executor =
            new FakeExecutor(
                Successful(
                    """
                    {
                      "terms": [{
                        "requirementId": "requirement-310-a",
                        "term": " obstructive sleep apnea ",
                        "termType": "Phrase",
                        "recognitionRole": "Diagnosis",
                        "evidenceClassification": "MedicalEvidence",
                        "rationale": "Identifies documentation of the claimed condition."
                      }]
                    }
                    """));

        var service =
            new EvidenceRecognitionTermProposalService(executor);

        var result =
            await service.ProposeAsync(
                Requirement(),
                "Obstructive sleep apnea",
                ["Coronary artery disease", "Major depressive disorder"],
                Context());

        var proposal = Assert.Single(result.Proposals);

        Assert.Equal(
            new RequirementId("requirement-310-a"),
            proposal.RequirementId);
        Assert.Equal("obstructive sleep apnea", proposal.Term);
        Assert.Equal(
            EvidenceRecognitionTermTypes.Phrase,
            proposal.TermType);
        Assert.Equal(
            EvidenceRecognitionRoles.Diagnosis,
            proposal.RecognitionRole);
        Assert.Equal(
            EvidenceClassifications.MedicalEvidence,
            proposal.EvidenceClassification);
        Assert.Equal("provision-310-a", proposal.AuthoritySource);

        Assert.Equal(
            IntelligenceCapabilityIds.TextStructuredExtraction,
            executor.CapabilityId);
        Assert.NotNull(executor.Request);
        Assert.Contains(
            "Claimed condition: Obstructive sleep apnea",
            executor.Request!.Text);
        Assert.Contains(
            "- Coronary artery disease",
            executor.Request.Text);
        Assert.Contains(
            "- Major depressive disorder",
            executor.Request.Text);
        var normalizedInstruction =
            string.Join(
                " ",
                executor.Request.Instruction.Split(
                    (char[]?)null,
                    StringSplitOptions.RemoveEmptyEntries));

        Assert.Contains(
            "requirement-310-a",
            normalizedInstruction);
        Assert.Contains(
            "case-insensitive literal substring search",
            normalizedInstruction);
        Assert.Contains(
            "It does not use stemming, regex, semantic similarity, or inference.",
            normalizedInstruction);
        Assert.Contains(
            "Do not combine supplied condition names into hypothetical nexus sentences",
            normalizedInstruction);
        Assert.Contains(
            "Do not fill a quota.",
            normalizedInstruction);
        Assert.Contains(
            $"never more than {EvidenceRecognitionTermProposalService.MaximumProposals}",
            normalizedInstruction);
    }

    [Fact]
    public async Task ProposeAsync_PreservesCapabilityFailure()
    {
        var capabilityResult =
            new IntelligenceCapabilityResult<string>
            {
                Success = false,
                Message = "Unavailable.",
                RequiresReview = true,
                Metadata = Metadata()
            };

        var service =
            new EvidenceRecognitionTermProposalService(
                new FakeExecutor(capabilityResult));

        var result =
            await service.ProposeAsync(
                Requirement(),
                "Obstructive sleep apnea",
                ["Coronary artery disease"],
                Context());

        Assert.Same(capabilityResult, result.IntelligenceResult);
        Assert.Empty(result.Proposals);
    }

    [Fact]
    public async Task ProposeAsync_RejectsUnexpectedRequirement()
    {
        await AssertProposalRejected(
            """
            {
              "terms": [{
                "requirementId": "requirement-other",
                "term": "sleep apnea",
                "termType": "Phrase",
                "recognitionRole": "Diagnosis",
                "evidenceClassification": "MedicalEvidence",
                "rationale": "Relevant diagnosis."
              }]
            }
            """);
    }

    [Fact]
    public async Task ProposeAsync_RejectsUnsupportedTermType()
    {
        await AssertProposalRejected(
            Json(termType: "Regex"));
    }

    [Fact]
    public async Task ProposeAsync_RejectsUnsupportedRecognitionRole()
    {
        await AssertProposalRejected(
            Json(recognitionRole: "Treatment"));
    }

    [Fact]
    public async Task ProposeAsync_RejectsUnsupportedClassification()
    {
        await AssertProposalRejected(
            Json(evidenceClassification: "UnknownEvidence"));
    }

    [Fact]
    public async Task ProposeAsync_RejectsDuplicateProposal()
    {
        await AssertProposalRejected(
            """
            {
              "terms": [
                {
                  "requirementId": "requirement-310-a",
                  "term": "sleep apnea",
                  "termType": "Phrase",
                  "recognitionRole": "Diagnosis",
                  "evidenceClassification": "MedicalEvidence",
                  "rationale": "First."
                },
                {
                  "requirementId": "requirement-310-a",
                  "term": "SLEEP APNEA",
                  "termType": "Phrase",
                  "recognitionRole": "Diagnosis",
                  "evidenceClassification": "MedicalEvidence",
                  "rationale": "Second."
                }
              ]
            }
            """);
    }

    [Fact]
    public async Task ProposeAsync_RejectsTooManyTerms()
    {
        var terms = string.Join(
            ",",
            Enumerable.Range(
                1,
                EvidenceRecognitionTermProposalService.MaximumProposals + 1)
            .Select(
                index =>
                    $$"""
                    {
                      "requirementId": "requirement-310-a",
                      "term": "term {{index}}",
                      "termType": "Keyword",
                      "recognitionRole": "EvidenceType",
                      "evidenceClassification": null,
                      "rationale": "Candidate {{index}}."
                    }
                    """));

        await AssertProposalRejected(
            $$"""{"terms":[{{terms}}]}""");
    }

    [Fact]
    public async Task ProposeAsync_AllowsNullClassification()
    {
        var service =
            new EvidenceRecognitionTermProposalService(
                new FakeExecutor(
                    Successful(
                        Json(evidenceClassification: null))));

        var result =
            await service.ProposeAsync(
                Requirement(),
                "Obstructive sleep apnea",
                ["Coronary artery disease"],
                Context());

        Assert.Null(Assert.Single(result.Proposals).EvidenceClassification);
    }

    [Fact]
    public async Task ProposeAsync_RejectsBlankBasisCondition()
    {
        var service =
            new EvidenceRecognitionTermProposalService(
                new FakeExecutor(Successful("{\"terms\":[]}")));

        await Assert.ThrowsAsync<ArgumentException>(
            () => service.ProposeAsync(
                Requirement(),
                "Obstructive sleep apnea",
                ["Coronary artery disease", " "],
                Context()));
    }

    private static async Task AssertProposalRejected(string json)
    {
        var service =
            new EvidenceRecognitionTermProposalService(
                new FakeExecutor(Successful(json)));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.ProposeAsync(
                Requirement(),
                "Obstructive sleep apnea",
                ["Coronary artery disease"],
                Context()));
    }

    private static string Json(
        string termType = "Phrase",
        string recognitionRole = "Diagnosis",
        string? evidenceClassification = "MedicalEvidence")
    {
        var classification =
            evidenceClassification is null
                ? "null"
                : $"\"{evidenceClassification}\"";

        return $$"""
        {
          "terms": [{
            "requirementId": "requirement-310-a",
            "term": "sleep apnea",
            "termType": "{{termType}}",
            "recognitionRole": "{{recognitionRole}}",
            "evidenceClassification": {{classification}},
            "rationale": "Relevant candidate."
          }]
        }
        """;
    }

    private static Requirement Requirement() =>
        new()
        {
            Id = new RequirementId("requirement-310-a"),
            RegulatoryProvisionId =
                new RegulatoryProvisionId("provision-310-a"),
            Description =
                "A secondary disability is proximately due to or " +
                "the result of a service-connected disease or injury."
        };

    private static IntelligenceCapabilityResult<string>
        Successful(string output) =>
        new()
        {
            Success = true,
            Output = output,
            RequiresReview = true,
            Metadata = Metadata()
        };

    private static IntelligenceExecutionContext Context() =>
        new(
            "recognition-term-steward",
            new IntelligenceCorrelationId("recognition-term-test"),
            new ProtectionClassificationId("confidential"),
            []);

    private static IntelligenceExecutionMetadata Metadata() =>
        new()
        {
            CapabilityId =
                IntelligenceCapabilityIds.TextStructuredExtraction,
            ProviderId =
                new IntelligenceProviderId("development.test"),
            CorrelationId =
                new IntelligenceCorrelationId("recognition-term-test"),
            EngineName = "test",
            StartedUtc = DateTimeOffset.UtcNow,
            CompletedUtc = DateTimeOffset.UtcNow
        };

    private sealed class FakeExecutor(
        IntelligenceCapabilityResult<string> result) :
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string>
    {
        public IntelligenceCapabilityId CapabilityId
        { get; private set; }

        public TextStructuredExtractionRequest? Request
        { get; private set; }

        public IntelligenceExecutionContext? Context
        { get; private set; }

        public Task<IntelligenceCapabilityResult<string>>
            ExecuteAsync(
                IntelligenceCapabilityId capabilityId,
                TextStructuredExtractionRequest request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken = default)
        {
            CapabilityId = capabilityId;
            Request = request;
            Context = context;

            return Task.FromResult(result);
        }
    }
}

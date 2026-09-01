using System.Text.Json;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class VaDecisionDocumentInterpretationServiceTests
{
    [Fact]
    public async Task InterpretAsync_MapsStructuredOutput()
    {
        var capabilityResult =
            new IntelligenceCapabilityResult<string>
            {
                Success = true,
                Output =
                    """
                    {
                      "decisionDate": "2026-08-20T00:00:00+00:00",
                      "issueDecisions": [
                        {
                          "issueDescription": "Sleep apnea",
                          "outcome": "Denied",
                          "rationale": "No nexus established.",
                          "favorableFindings": ["Current diagnosis."],
                          "adverseFindings": ["No nexus."],
                          "citedRegulations": ["38 CFR 3.310"],
                          "referencedEvidence": ["Sleep study"],
                          "sourceExcerpts": [
                            {
                              "text": "Service connection is denied.",
                              "startOffset": 10,
                              "length": 29
                            }
                          ]
                        }
                      ]
                    }
                    """,
                RequiresReview = true,
                Metadata = TestMetadata()
            };

        var executor = new FakeExecutor(capabilityResult);

        var service =
            new VaDecisionDocumentInterpretationService(
                executor);

        var artifactId = new ArtifactId("decision-1");

        var result =
            await service.InterpretAsync(
                artifactId,
                "0123456789Service connection is denied.",
                TestContext());

        Assert.Same(
            capabilityResult,
            result.IntelligenceResult);

        var interpretation =
            Assert.IsType<
                EMF.Extensions.VeteransClaims.Models.Adjudication.
                    VaDecisionDocumentInterpretation>(
                result.Interpretation);

        Assert.Equal(
            artifactId,
            interpretation.ArtifactId);

        var issue =
            Assert.Single(
                interpretation.IssueDecisions);

        Assert.Equal("Sleep apnea", issue.IssueDescription);
        Assert.Equal("Denied", issue.Outcome);

        var excerpt =
            Assert.Single(issue.SourceExcerpts);

        Assert.Equal(artifactId, excerpt.ArtifactId);

        Assert.Equal(
            IntelligenceCapabilityIds.TextStructuredExtraction,
            executor.CapabilityId);
    }


    [Fact]
    public async Task InterpretAsync_PreservesCapabilityFailure()
    {
        var capabilityResult =
            new IntelligenceCapabilityResult<string>
            {
                Success = false,
                Message = "Unavailable.",
                RequiresReview = true,
                Metadata = TestMetadata()
            };

        var service =
            new VaDecisionDocumentInterpretationService(
                new FakeExecutor(capabilityResult));

        var result =
            await service.InterpretAsync(
                new ArtifactId("decision-1"),
                "VA decision text.",
                TestContext());

        Assert.Same(
            capabilityResult,
            result.IntelligenceResult);

        Assert.Null(result.Interpretation);
    }


    [Fact]
    public async Task InterpretAsync_RejectsUnknownOutcome()
    {
        var capabilityResult =
            new IntelligenceCapabilityResult<string>
            {
                Success = true,
                Output =
                    """
                    {
                      "decisionDate": null,
                      "issueDecisions": [{
                        "issueDescription": "Sleep apnea",
                        "outcome": "Pending",
                        "rationale": "",
                        "favorableFindings": [],
                        "adverseFindings": [],
                        "citedRegulations": [],
                        "referencedEvidence": [],
                        "sourceExcerpts": []
                      }]
                    }
                    """,
                RequiresReview = true,
                Metadata = TestMetadata()
            };

        var service =
            new VaDecisionDocumentInterpretationService(
                new FakeExecutor(capabilityResult));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InterpretAsync(
                new ArtifactId("decision-1"),
                "VA decision text.",
                TestContext()));
    }


    [Fact]
    public async Task InterpretAsync_RejectsMalformedJson()
    {
        var capabilityResult =
            new IntelligenceCapabilityResult<string>
            {
                Success = true,
                Output = "{ not-json",
                RequiresReview = true,
                Metadata = TestMetadata()
            };

        var service =
            new VaDecisionDocumentInterpretationService(
                new FakeExecutor(capabilityResult));

        await Assert.ThrowsAsync<JsonException>(
            () => service.InterpretAsync(
                new ArtifactId("decision-1"),
                "VA decision text.",
                TestContext()));
    }

    [Fact]
    public async Task InterpretAsync_RejectsUngroundedSourceExcerpt()
    {
        var capabilityResult =
            new IntelligenceCapabilityResult<string>
            {
                Success = true,
                Output =
                    """
                    {
                      "decisionDate": "2026-08-20T00:00:00+00:00",
                      "issueDecisions": [{
                        "issueDescription": "Sleep apnea",
                        "outcome": "Denied",
                        "rationale": "No nexus established.",
                        "favorableFindings": [],
                        "adverseFindings": [],
                        "citedRegulations": [],
                        "referencedEvidence": [],
                        "sourceExcerpts": [{
                          "text": "Service connection is denied.",
                          "startOffset": 0,
                          "length": 29
                        }]
                      }]
                    }
                    """,
                RequiresReview = true,
                Metadata = TestMetadata()
            };

        var service =
            new VaDecisionDocumentInterpretationService(
                new FakeExecutor(capabilityResult));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.InterpretAsync(
                new ArtifactId("decision-1"),
                "Service connection is granted.",
                TestContext()));
    }

    private sealed class FakeExecutor(
        IntelligenceCapabilityResult<string> result) :
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string>
    {
        public IntelligenceCapabilityId CapabilityId
        { get; private set; }

        public Task<IntelligenceCapabilityResult<string>>
            ExecuteAsync(
                IntelligenceCapabilityId capabilityId,
                TextStructuredExtractionRequest request,
                IntelligenceExecutionContext context,
                CancellationToken cancellationToken = default)
        {
            CapabilityId = capabilityId;
            return Task.FromResult(result);
        }
    }

    private static IntelligenceExecutionContext TestContext() =>
        new(
            "security-steward",
            new IntelligenceCorrelationId("decision-test-1"),
            new ProtectionClassificationId("confidential"),
            Array.Empty<ArtifactId>());

    private static IntelligenceExecutionMetadata TestMetadata() =>
        new()
        {
            CapabilityId =
                IntelligenceCapabilityIds
                    .TextStructuredExtraction,
            ProviderId =
                new IntelligenceProviderId("development.test"),
            CorrelationId =
                new IntelligenceCorrelationId(
                    "decision-test-1"),
            EngineName = "test",
            StartedUtc = DateTimeOffset.UtcNow,
            CompletedUtc = DateTimeOffset.UtcNow
        };
}

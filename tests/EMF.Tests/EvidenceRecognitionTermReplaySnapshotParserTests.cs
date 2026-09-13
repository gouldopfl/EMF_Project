using System.Text.Json;
using EMF.Extensions.VeteransClaims.Orchestration;

namespace EMF.Tests;

public sealed class EvidenceRecognitionTermReplaySnapshotParserTests
{
    [Fact]
    public void Parse_MapsValidSnapshot()
    {
        var parser =
            new EvidenceRecognitionTermReplaySnapshotParser();

        var result = parser.Parse(
            """
            {
              "contexts": [{
                "basisId": "basis-osa-secondary",
                "requirementId": "requirement-310-a",
                "proposals": [{
                  "requirementId": "requirement-310-a",
                  "term": " OSA ",
                  "termType": "Acronym",
                  "recognitionRole": "Diagnosis",
                  "evidenceClassification": "MedicalEvidence",
                  "authoritySource": " provision-310-a ",
                  "rationale": " Common abbreviation. "
                }]
              }]
            }
            """);

        var context = Assert.Single(result);
        Assert.Equal("basis-osa-secondary", context.BasisId.Value);
        Assert.Equal("requirement-310-a", context.RequirementId.Value);

        var proposal = Assert.Single(context.Proposals);
        Assert.Equal("requirement-310-a", proposal.RequirementId.Value);
        Assert.Equal("OSA", proposal.Term);
        Assert.Equal("Acronym", proposal.TermType);
        Assert.Equal("Diagnosis", proposal.RecognitionRole);
        Assert.Equal("MedicalEvidence", proposal.EvidenceClassification);
        Assert.Equal("provision-310-a", proposal.AuthoritySource);
        Assert.Equal("Common abbreviation.", proposal.Rationale);
    }

    [Fact]
    public void Parse_AllowsNullEvidenceClassification()
    {
        var parser =
            new EvidenceRecognitionTermReplaySnapshotParser();

        var result = parser.Parse(
            Snapshot(
                evidenceClassification: "null"));

        Assert.Null(
            Assert.Single(
                Assert.Single(result).Proposals)
                .EvidenceClassification);
    }

    [Fact]
    public void Parse_RejectsRequirementMismatch()
    {
        var parser =
            new EvidenceRecognitionTermReplaySnapshotParser();

        var exception =
            Assert.Throws<InvalidDataException>(
                () => parser.Parse(
                    Snapshot(
                        proposalRequirementId: "requirement-other")));

        Assert.Contains("does not match", exception.Message);
    }

    [Fact]
    public void Parse_RejectsDuplicateContexts()
    {
        var parser =
            new EvidenceRecognitionTermReplaySnapshotParser();

        var context =
            """
            {
              "basisId": "basis-1",
              "requirementId": "requirement-1",
              "proposals": []
            }
            """;

        var exception =
            Assert.Throws<InvalidDataException>(
                () => parser.Parse(
                    $$"""
                    { "contexts": [{{context}}, {{context}}] }
                    """));

        Assert.Contains("duplicate", exception.Message);
    }

    [Fact]
    public void Parse_RejectsUnsupportedTermType()
    {
        var parser =
            new EvidenceRecognitionTermReplaySnapshotParser();

        var exception =
            Assert.Throws<InvalidDataException>(
                () => parser.Parse(
                    Snapshot(termType: "Regex")));

        Assert.Contains("Unsupported recognition term type", exception.Message);
    }

    [Fact]
    public void Parse_RejectsMalformedJson()
    {
        var parser =
            new EvidenceRecognitionTermReplaySnapshotParser();

        Assert.Throws<JsonException>(
            () => parser.Parse("{ not-json"));
    }

    private static string Snapshot(
        string proposalRequirementId = "requirement-1",
        string termType = "Phrase",
        string evidenceClassification = "\"MedicalEvidence\"") =>
        $$"""
        {
          "contexts": [{
            "basisId": "basis-1",
            "requirementId": "requirement-1",
            "proposals": [{
              "requirementId": "{{proposalRequirementId}}",
              "term": "obstructive sleep apnea",
              "termType": "{{termType}}",
              "recognitionRole": "Diagnosis",
              "evidenceClassification": {{evidenceClassification}},
              "authoritySource": "provision-1",
              "rationale": "Useful literal term."
            }]
          }]
        }
        """;
}

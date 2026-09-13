using EMF.ConsoleApplication;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Service;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Regulatory;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Security.Models.Identities;

namespace EMF.Tests;

public sealed class VeteransEvidenceRecognitionProposalConsoleTests
{
    [Fact]
    public async Task RunAsync_PrintsProposalsWithoutPersistingTerms()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            await new VeteransClaimsSqliteSchema(databasePath)
                .InitializeAsync();

            var veteran = new Veteran
            {
                Id = new VeteranId("veteran-recognition-proposal")
            };

            await new SqliteVeteranRepository(databasePath)
                .AddVeteranAsync(veteran);

            var claim = new Claim
            {
                Id = new ClaimId("claim-recognition-proposal"),
                VeteranId = veteran.Id
            };

            await new SqliteClaimRepository(databasePath)
                .AddClaimAsync(claim);

            var issue = new ClaimIssue
            {
                Id = new ClaimIssueId("issue-recognition-proposal"),
                ClaimId = claim.Id,
                ClaimIssueType = ClaimIssueTypes.ServiceConnection
            };

            await new SqliteClaimIssueRepository(databasePath)
                .AddClaimIssueAsync(issue);

            var connections =
                new SqliteServiceConnectionRepository(databasePath);

            var theory = new ServiceConnectionTheory
            {
                Id = new ServiceConnectionTheoryId("theory-recognition-proposal"),
                ClaimIssueId = issue.Id,
                TheoryType = ServiceConnectionTheoryTypes.Secondary
            };

            await connections.AddServiceConnectionTheoryAsync(theory);

            var basis = new ServiceConnectionBasis
            {
                Id = new ServiceConnectionBasisId("basis-recognition-proposal"),
                ClaimIssueId = issue.Id,
                ServiceConnectionTheoryId = theory.Id
            };

            await connections.AddServiceConnectionBasisAsync(basis);

            var conditions = new SqliteConditionRepository(databasePath);

            var claimed = new ClaimedCondition
            {
                Id = new ClaimedConditionId("condition-recognition-proposal"),
                ClaimIssueId = issue.Id,
                Name = "Obstructive sleep apnea"
            };

            await conditions.AddClaimedConditionAsync(claimed);
            await connections.AddBasisClaimedConditionAsync(
                new ServiceConnectionBasisClaimedCondition
                {
                    ServiceConnectionBasisId = basis.Id,
                    ClaimedConditionId = claimed.Id
                });

            var serviceConnected = new MedicalCondition
            {
                Id = new MedicalConditionId("medical-condition-recognition-proposal"),
                Name = "Major depressive disorder"
            };

            await conditions.AddMedicalConditionAsync(serviceConnected);
            await conditions.AddVeteranMedicalConditionAsync(
                new VeteranMedicalCondition
                {
                    VeteranId = veteran.Id,
                    MedicalConditionId = serviceConnected.Id
                });
            await connections.AddBasisServiceConnectedConditionAsync(
                new ServiceConnectionBasisServiceConnectedCondition
                {
                    ServiceConnectionBasisId = basis.Id,
                    ServiceConnectedConditionId = serviceConnected.Id
                });

            var regulatory = new SqliteRegulatoryRepository(databasePath);
            await regulatory.InitializeAsync();

            var authority = new RegulatoryAuthority
            {
                Id = new RegulatoryAuthorityId("authority-recognition-proposal"),
                AuthorityType = "Regulation",
                Citation = "38 CFR",
                Title = "Veterans Affairs"
            };

            await regulatory.AddRegulatoryAuthorityAsync(authority);

            var provision = new RegulatoryProvision
            {
                Id = new RegulatoryProvisionId("provision-recognition-proposal"),
                RegulatoryAuthorityId = authority.Id,
                ProvisionType = RegulatoryProvisionTypes.Requirement,
                Citation = "38 CFR 3.310"
            };

            await regulatory.AddRegulatoryProvisionAsync(provision);

            var requirements = new[]
            {
                new Requirement
                {
                    Id = new RequirementId("requirement-recognition-causation"),
                    RegulatoryProvisionId = provision.Id,
                    Description = "Secondary causation requirement."
                },
                new Requirement
                {
                    Id = new RequirementId("requirement-recognition-aggravation"),
                    RegulatoryProvisionId = provision.Id,
                    Description = "Secondary aggravation requirement."
                }
            };

            foreach (var requirement in requirements)
            {
                await regulatory.AddRequirementAsync(requirement);
                await connections.AddBasisRequirementAsync(
                    new ServiceConnectionBasisRequirement
                    {
                        ServiceConnectionBasisId = basis.Id,
                        RequirementId = requirement.Id
                    });
            }

            var recognition =
                new SqliteEvidenceRecognitionTermRepository(databasePath);
            await recognition.InitializeAsync();

            using var output = new StringWriter();
            var executor = new ProposalExecutor();

            var exitCode =
                await VeteransConsoleCommand
                    .RunEvidenceRecognitionTermProposalsAsync(
                        databasePath,
                        issue.Id,
                        () => Task.FromResult(
                            new TextSummarizationConsoleRuntime
                            {
                                TextSummarizationCapabilityExecutor =
                                    new UnusedSummarizationExecutor(),
                                TextStructuredExtractionCapabilityExecutor =
                                    executor,
                                SubjectId = "console-test",
                                ClassificationId =
                                    new ProtectionClassificationId("confidential"),
                                AuditDatabasePath = "test-audit.db"
                            }),
                        output);

            var rendered = output.ToString();

            Assert.Equal(0, exitCode);
            Assert.Equal(2, executor.CallCount);
            Assert.Contains(
                "Basis             : basis-recognition-proposal",
                rendered);
            Assert.Contains(
                "Claimed Condition : Obstructive sleep apnea",
                rendered);
            Assert.Contains(
                "Service Connected : Major depressive disorder",
                rendered);
            Assert.Contains("Term             : sleep apnea", rendered);
            Assert.Contains("Recognition Role : Diagnosis", rendered);
            Assert.Contains("Requires Review    : True", rendered);

            foreach (var requirement in requirements)
            {
                Assert.Empty(
                    await recognition.GetEvidenceRecognitionTermsAsync(
                        requirement.Id));
            }
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    private sealed class ProposalExecutor :
        IIntelligenceCapabilityExecutor<
            TextStructuredExtractionRequest,
            string>
    {
        public int CallCount { get; private set; }

        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextStructuredExtractionRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default)
        {
            CallCount++;

            var requirementLine =
                request.Text
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .First(line => line.StartsWith("Requirement ID: "));

            var requirementId =
                requirementLine["Requirement ID: ".Length..].Trim();

            var json =
                $$"""
                {
                  "terms": [{
                    "requirementId": "{{requirementId}}",
                    "term": "sleep apnea",
                    "termType": "Phrase",
                    "recognitionRole": "Diagnosis",
                    "evidenceClassification": "MedicalEvidence",
                    "rationale": "Identifies documentation of the claimed condition."
                  }]
                }
                """;

            return Task.FromResult(
                new IntelligenceCapabilityResult<string>
                {
                    Success = true,
                    Output = json,
                    RequiresReview = true,
                    Metadata = new IntelligenceExecutionMetadata
                    {
                        CapabilityId = capabilityId,
                        ProviderId = new IntelligenceProviderId("test"),
                        CorrelationId = context.CorrelationId,
                        EngineName = "test",
                        StartedUtc = DateTimeOffset.UtcNow,
                        CompletedUtc = DateTimeOffset.UtcNow
                    },
                    SourceArtifactIds = context.InputArtifactIds.ToArray()
                });
        }
    }

    private sealed class UnusedSummarizationExecutor :
        IIntelligenceCapabilityExecutor<TextSummarizationRequest, string>
    {
        public Task<IntelligenceCapabilityResult<string>> ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TextSummarizationRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(
                "Recognition-term proposal must not use summarization.");
    }
}

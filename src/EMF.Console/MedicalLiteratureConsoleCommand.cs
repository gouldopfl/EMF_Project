using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using System.Text.Json;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Services;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Persistence.Repositories;

namespace EMF.ConsoleApplication;

internal static class MedicalLiteratureConsoleCommand
{
    public static async Task<int> RunSourceAsync(
        string databasePath,
        string sourcePath)
    {
        try
        {
            var json =
                await File.ReadAllTextAsync(sourcePath);

            var input =
                JsonSerializer.Deserialize<MedicalLiteratureSourceInput>(
                    json,
                    new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    })
                ?? throw new InvalidOperationException(
                    "Medical literature source JSON contained no source.");

            var repository =
                new SqliteMedicalLiteratureRepository(databasePath);

            await repository.InitializeAsync();

            var service =
                new MedicalLiteratureService(
                    new SqliteRegulatoryRepository(databasePath),
                    repository);

            var result =
                await service.AddSourceAsync(
                    new MedicalLiteratureSource
                    {
                        Id = new MedicalLiteratureSourceId(input.Id),
                        Title = input.Title,
                        Authors = input.Authors,
                        Publication = input.Publication,
                        PublicationYear = input.PublicationYear,
                        VaAffiliated = input.VaAffiliated,
                        VaFunded = input.VaFunded,
                        PeerReviewed = input.PeerReviewed,
                        FundingSource = input.FundingSource,
                        ResearchOrganization =
                            input.ResearchOrganization,
                        Doi = input.Doi,
                        Pmid = input.Pmid,
                        SourceUri = input.SourceUri,
                        SourceHash = input.SourceHash,
                        RetrievedUtc =
                            input.RetrievedUtc ??
                            DateTimeOffset.UtcNow
                    });

            global::System.Console.WriteLine(
                $"Literature ID : {result.Id.Value}");
            global::System.Console.WriteLine(
                $"Title         : {result.Title}");
            global::System.Console.WriteLine(
                $"Publication   : {result.Publication}");

            return 0;
        }
        catch (JsonException ex)
        {
            global::System.Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (ArgumentException ex)
        {
            global::System.Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            global::System.Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    public static async Task<int> RunLinkAsync(
        string databasePath,
        RequirementId requirementId,
        MedicalLiteratureSourceId sourceId,
        string guidanceRole,
        string description)
    {
        try
        {
            var repository =
                new SqliteMedicalLiteratureRepository(databasePath);

            await repository.InitializeAsync();

            var service =
                new MedicalLiteratureService(
                    new SqliteRegulatoryRepository(databasePath),
                    repository);

            var result =
                await service.AddRequirementLiteratureAsync(
                    requirementId,
                    sourceId,
                    guidanceRole,
                    description);

            global::System.Console.WriteLine(
                $"Requirement   : {result.Association.RequirementId.Value}");
            global::System.Console.WriteLine(
                $"Literature ID : {result.Source.Id.Value}");
            global::System.Console.WriteLine(
                $"Guidance Role : {result.Association.GuidanceRole}");
            global::System.Console.WriteLine(
                $"Description   : {result.Association.Description}");

            return 0;
        }
        catch (ArgumentException ex)
        {
            global::System.Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            global::System.Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }


    public static async Task<int> RunArtifactAsync(
        string databasePath,
        MedicalLiteratureSourceId sourceId,
        ArtifactId artifactId)
    {
        try
        {
            var repository =
                new SqliteMedicalLiteratureRepository(databasePath);

            await repository.InitializeAsync();

            var service =
                new MedicalLiteratureService(
                    new SqliteRegulatoryRepository(databasePath),
                    repository);

            var result =
                await service.AddSourceArtifactAsync(
                    sourceId,
                    artifactId);

            global::System.Console.WriteLine(
                $"Literature ID : {result.MedicalLiteratureSourceId.Value}");
            global::System.Console.WriteLine(
                $"Artifact ID   : {result.ArtifactId.Value}");

            return 0;
        }
        catch (ArgumentException ex)
        {
            global::System.Console.Error.WriteLine(ex.Message);
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            global::System.Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }


    internal static async Task<int> RunClassifyAsync(
        string databasePath,
        MedicalLiteratureSourceId sourceId,
        ArtifactId artifactId,
        IReadOnlyList<RequirementId> candidateRequirementIds,
        Func<Task<TextSummarizationConsoleRuntime>> runtimeFactory,
        IArtifactContentStore? contentStore,
        TextWriter output,
        bool promote = false,
        string? reviewedBy = null,
        string? supersedesCorrelationId = null)
    {
        ArgumentNullException.ThrowIfNull(candidateRequirementIds);
        ArgumentNullException.ThrowIfNull(runtimeFactory);
        ArgumentNullException.ThrowIfNull(output);

        try
        {
            if (supersedesCorrelationId is not null && !promote)
                throw new InvalidOperationException(
                    "Medical literature supersession requires promotion.");

            if (promote && string.IsNullOrWhiteSpace(reviewedBy))
                throw new InvalidOperationException(
                    "Medical literature promotion requires review. " +
                    "Set EMF_REVIEWED_BY to the reviewer identity.");

            if (contentStore is null)
                throw new InvalidOperationException(
                    "Artifact content store is not configured.");

            var literature =
                new SqliteMedicalLiteratureRepository(databasePath);
            await literature.InitializeAsync();

            var evidence = new SqliteEvidenceRepository(databasePath);
            await evidence.InitializeAsync();

            var textExtractor =
                ArtifactTextExtractionFactory.Create(
                    evidence,
                    contentStore);

            var runtime = await runtimeFactory();

            var coordinator =
                VeteransEvidenceOrchestrationFactory
                    .CreateMedicalLiteratureClassificationCoordinator(
                        literature,
                        new SqliteRegulatoryRepository(databasePath),
                        textExtractor,
                        runtime.TextStructuredExtractionCapabilityExecutor);

            var result = await coordinator.ClassifyAsync(
                sourceId,
                artifactId,
                candidateRequirementIds,
                new IntelligenceExecutionContext(
                    runtime.SubjectId,
                    new IntelligenceCorrelationId(
                        $"veterans-literature-{Guid.NewGuid():N}"),
                    runtime.ClassificationId,
                    [artifactId]));

            if (!result.IntelligenceResult.Success)
            {
                global::System.Console.Error.WriteLine(
                    ConsoleTextSanitizer.Sanitize(
                        result.IntelligenceResult.Message ??
                        "Medical literature classification failed."));
                return 1;
            }

            if (result.Proposal is null)
                throw new InvalidOperationException(
                    "Medical literature classification produced no proposal.");

            output.WriteLine($"Literature ID : {sourceId.Value}");
            output.WriteLine($"Artifact ID   : {artifactId.Value}");
            output.WriteLine(
                $"Requires Review: {result.IntelligenceResult.RequiresReview}");
            output.WriteLine(
                $"Classifications: {result.Proposal.Classifications.Count}");

            foreach (var classification in result.Proposal.Classifications)
            {
                output.WriteLine();
                output.WriteLine(
                    $"Requirement   : {classification.RequirementId.Value}");
                output.WriteLine(
                    $"Guidance Role : {ConsoleTextSanitizer.Sanitize(classification.GuidanceRole)}");
                output.WriteLine(
                    $"Description   : {ConsoleTextSanitizer.Sanitize(classification.Description)}");

                foreach (var excerpt in classification.SourceExcerpts)
                    output.WriteLine(
                        $"Excerpt       : offset={excerpt.StartOffset?.ToString() ?? "?"} " +
                        $"length={excerpt.Length?.ToString() ?? "?"} | " +
                        ConsoleTextSanitizer.Sanitize(excerpt.Text));
            }

            if (promote)
            {
                var promotedUtc = DateTimeOffset.UtcNow;
                var intelligence = result.IntelligenceResult;
                var metadata = intelligence.Metadata;

                var reviewedClassifications =
                    new List<ReviewedMedicalLiteratureClassification>(
                        result.Proposal.Classifications.Count);

                foreach (var classification in result.Proposal.Classifications)
                {
                    reviewedClassifications.Add(
                        new ReviewedMedicalLiteratureClassification
                        {
                            Association = new RequirementMedicalLiterature
                            {
                                RequirementId = classification.RequirementId,
                                MedicalLiteratureSourceId = sourceId,
                                GuidanceRole = classification.GuidanceRole,
                                Description = classification.Description
                            },
                            ArtifactId = artifactId,
                            PromotedBy = runtime.SubjectId,
                            PromotedUtc = promotedUtc,
                            ReviewedBy = reviewedBy!,
                            ReviewedUtc = promotedUtc,
                            IntelligenceOutput = intelligence.Output!,
                            CapabilityId = metadata.CapabilityId.Value,
                            ProviderId = metadata.ProviderId.Value,
                            CorrelationId = metadata.CorrelationId.Value,
                            EngineName = metadata.EngineName,
                            EngineVersion = metadata.EngineVersion,
                            ProviderOperationId = metadata.ProviderOperationId,
                            StartedUtc = metadata.StartedUtc,
                            CompletedUtc = metadata.CompletedUtc,
                            RequiresReview = intelligence.RequiresReview,
                            Warnings = intelligence.Warnings.ToArray(),
                            SourceExcerpts = classification.SourceExcerpts
                        });
                }

                if (supersedesCorrelationId is null)
                {
                    await literature.AddReviewedClassificationsAsync(
                        reviewedClassifications);
                }
                else
                {
                    await literature.SupersedeReviewedClassificationsAsync(
                        supersedesCorrelationId,
                        reviewedClassifications);
                }

                output.WriteLine();
                output.WriteLine(
                    $"Promoted      : {result.Proposal.Classifications.Count}");
                if (supersedesCorrelationId is not null)
                    output.WriteLine(
                        $"Supersedes    : {ConsoleTextSanitizer.Sanitize(supersedesCorrelationId)}");
                output.WriteLine(
                    $"Reviewed By   : {ConsoleTextSanitizer.Sanitize(reviewedBy!)}");
            }

            return 0;
        }
        catch (JsonException ex)
        {
            global::System.Console.Error.WriteLine(
                ConsoleTextSanitizer.Sanitize(ex.Message));
            return 1;
        }
        catch (ArgumentException ex)
        {
            global::System.Console.Error.WriteLine(
                ConsoleTextSanitizer.Sanitize(ex.Message));
            return 1;
        }
        catch (InvalidOperationException ex)
        {
            global::System.Console.Error.WriteLine(
                ConsoleTextSanitizer.Sanitize(ex.Message));
            return 1;
        }
    }


}

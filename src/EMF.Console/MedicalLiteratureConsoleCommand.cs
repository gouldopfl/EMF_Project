using System.Text.Json;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using EMF.Extensions.VeteransClaims.Services;

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

}

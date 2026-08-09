using EMF.Core.Models.Workflow;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class WorkflowDefinitionRepositoryTests
{
    [Fact]
    public async Task Stored_definition_can_be_retrieved_by_id_and_version()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteWorkflowDefinitionRepository(databasePath);

            await repository.InitializeAsync();

            var definition =
                new WorkflowDefinition
                {
                    Id = "claims-review",
                    Name = "Claims Review",
                    Version = "1",
                    ActivityIds = new[] { "discover", "review" }
                };

            await repository.StoreDefinitionAsync(definition);

            var stored =
                await repository.GetDefinitionAsync(
                    "claims-review",
                    "1");

            Assert.NotNull(stored);
            Assert.Equal(definition.Id, stored!.Id);
            Assert.Equal(definition.Name, stored.Name);
            Assert.Equal(definition.Version, stored.Version);
            Assert.Equal(definition.ActivityIds, stored.ActivityIds);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Multiple_versions_of_same_definition_can_coexist()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteWorkflowDefinitionRepository(databasePath);

            await repository.InitializeAsync();

            await repository.StoreDefinitionAsync(
                new WorkflowDefinition
                {
                    Id = "claims-review",
                    Name = "Claims Review",
                    Version = "1",
                    ActivityIds = new[] { "discover" }
                });

            await repository.StoreDefinitionAsync(
                new WorkflowDefinition
                {
                    Id = "claims-review",
                    Name = "Claims Review",
                    Version = "2",
                    ActivityIds = new[] { "discover", "review" }
                });

            var version1 =
                await repository.GetDefinitionAsync(
                    "claims-review",
                    "1");

            var version2 =
                await repository.GetDefinitionAsync(
                    "claims-review",
                    "2");

            Assert.NotNull(version1);
            Assert.NotNull(version2);
            Assert.Equal(
                new[] { "discover" },
                version1!.ActivityIds);
            Assert.Equal(
                new[] { "discover", "review" },
                version2!.ActivityIds);
        }
        finally
        {
            File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task Existing_definition_version_cannot_be_overwritten()
    {
        var databasePath = Path.GetTempFileName();

        try
        {
            var repository =
                new SqliteWorkflowDefinitionRepository(databasePath);

            await repository.InitializeAsync();

            await repository.StoreDefinitionAsync(
                new WorkflowDefinition
                {
                    Id = "claims-review",
                    Name = "Claims Review",
                    Version = "1",
                    ActivityIds = new[] { "discover" }
                });

            await Assert.ThrowsAnyAsync<Exception>(
                () => repository.StoreDefinitionAsync(
                    new WorkflowDefinition
                    {
                        Id = "claims-review",
                        Name = "Changed Claims Review",
                        Version = "1",
                        ActivityIds = new[] { "different" }
                    }));
        }
        finally
        {
            File.Delete(databasePath);
        }
    }
}

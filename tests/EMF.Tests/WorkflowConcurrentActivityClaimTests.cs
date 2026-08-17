using EMF.Core.Models.Identities;
using EMF.Persistence.Repositories;

namespace EMF.Tests;

public sealed class WorkflowConcurrentActivityClaimTests
{
    [Fact]
    public async Task Concurrent_claims_have_one_winner()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-claims-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(path);

            await repository.InitializeAsync();

            var workflowId =
                new WorkflowId("concurrent-workflow");

            var claims = Enumerable.Range(1, 16)
                .Select(index =>
                    repository.TryClaimActivityAsync(
                        workflowId,
                        "activity",
                        $"claim-{index}",
                        DateTimeOffset.UtcNow));

            var results = await Task.WhenAll(claims);

            Assert.Single(results.Where(result => result));
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }
}

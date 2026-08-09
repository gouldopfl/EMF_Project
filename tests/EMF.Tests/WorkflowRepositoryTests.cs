using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public class WorkflowRepositoryTests
{
    [Fact]
    public async Task AddCheckpointAsync_stores_checkpoint()
    {
        var repository = new InMemoryWorkflowRepository();

        var workflowId = new WorkflowId("workflow-001");

        var checkpoint = new WorkflowCheckpoint
        {
            WorkflowId = workflowId,
            Step = "Discovery Complete",
            Status = WorkflowStatus.Completed,
            RecordedUtc = DateTimeOffset.UtcNow
        };

        await repository.AddCheckpointAsync(checkpoint);

        var results = await repository.GetCheckpointsAsync(workflowId);

        Assert.Single(results);
        Assert.Equal("Discovery Complete", results[0].Step);
        Assert.Equal(WorkflowStatus.Completed, results[0].Status);
    }

    [Fact]
    public async Task GetCheckpointsAsync_returns_only_matching_workflow()
    {
        var repository = new InMemoryWorkflowRepository();

        await repository.AddCheckpointAsync(
            new WorkflowCheckpoint
            {
                WorkflowId = new WorkflowId("workflow-001"),
                Step = "Step A",
                Status = WorkflowStatus.Completed,
                RecordedUtc = DateTimeOffset.UtcNow
            });

        await repository.AddCheckpointAsync(
            new WorkflowCheckpoint
            {
                WorkflowId = new WorkflowId("workflow-002"),
                Step = "Step B",
                Status = WorkflowStatus.Running,
                RecordedUtc = DateTimeOffset.UtcNow
            });

        var results =
            await repository.GetCheckpointsAsync(
                new WorkflowId("workflow-001"));

        Assert.Single(results);
        Assert.Equal("Step A", results[0].Step);
    }

    [Fact]
    public async Task Repository_stores_workflow_execution_definition_version()
    {
        var repository = new InMemoryWorkflowRepository();

        var workflowId = new WorkflowId("workflow-definition-test");

        var execution = new WorkflowExecutionRecord
        {
            WorkflowId = workflowId,
            DefinitionId = "evidence-processing",
            DefinitionVersion = "1",
            CreatedUtc = DateTimeOffset.UtcNow,
            CurrentStatus = WorkflowStatus.Running,
            RecoveryStatus = WorkflowRecoveryStatus.None
        };

        await repository.CreateExecutionAsync(execution);

        var stored = await repository.GetExecutionAsync(workflowId);

        Assert.NotNull(stored);
        Assert.Equal("evidence-processing", stored!.DefinitionId);
        Assert.Equal("1", stored.DefinitionVersion);
        Assert.Equal(WorkflowStatus.Running, stored.CurrentStatus);
    }

}

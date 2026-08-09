using EMF.Core.Models.Identities;
using EMF.Core.Models.Workflow;
using EMF.Orchestration.Services;

namespace EMF.Tests;

public sealed class WorkflowRecoveryPolicyTests
{
    [Fact]
    public async Task Interrupted_workflow_with_checkpoint_returns_resume()
    {
        var policy = new WorkflowRecoveryPolicy();

        var execution = new WorkflowExecutionRecord
        {
            WorkflowId = new WorkflowId("workflow-test"),
            DefinitionId = "test",
            DefinitionVersion = "1",
            CreatedUtc = DateTimeOffset.UtcNow,
            CurrentStatus = WorkflowStatus.Interrupted,
            RecoveryStatus = WorkflowRecoveryStatus.None
        };

        var checkpoints = new[]
        {
            new WorkflowCheckpoint
            {
                WorkflowId = execution.WorkflowId,
                Step = "Step A",
                Status = WorkflowStatus.Completed,
                RecordedUtc = DateTimeOffset.UtcNow
            }
        };

        var result = await policy.EvaluateAsync(execution, checkpoints);

        Assert.Equal(RecoveryDecision.Resume, result);
    }
    [Fact]
    public async Task Failed_workflow_returns_require_review()
    {
        var policy = new WorkflowRecoveryPolicy();

        var execution = new WorkflowExecutionRecord
        {
            WorkflowId = new WorkflowId("workflow-failed-test"),
            DefinitionId = "test",
            DefinitionVersion = "1",
            CreatedUtc = DateTimeOffset.UtcNow,
            CurrentStatus = WorkflowStatus.Failed,
            RecoveryStatus = WorkflowRecoveryStatus.None
        };

        var checkpoints = Array.Empty<WorkflowCheckpoint>();

        var result = await policy.EvaluateAsync(execution, checkpoints);

        Assert.Equal(RecoveryDecision.RequireReview, result);
    }

}

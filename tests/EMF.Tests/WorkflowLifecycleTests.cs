using EMF.Core.Models.Workflow;

namespace EMF.Tests;

public sealed class WorkflowLifecycleTests
{
    [Theory]
    [InlineData(WorkflowStatus.Pending, WorkflowStatus.Running)]
    [InlineData(WorkflowStatus.Running, WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Running, WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Running, WorkflowStatus.Interrupted)]
    [InlineData(WorkflowStatus.Running, WorkflowStatus.Paused)]
    [InlineData(WorkflowStatus.Paused, WorkflowStatus.Running)]
    [InlineData(WorkflowStatus.Paused, WorkflowStatus.Interrupted)]
    [InlineData(WorkflowStatus.Interrupted, WorkflowStatus.Running)]
    [InlineData(WorkflowStatus.Interrupted, WorkflowStatus.Failed)]
    public void Allows_defined_transition(
        WorkflowStatus current,
        WorkflowStatus next)
    {
        Assert.True(
            WorkflowLifecycle.CanTransition(current, next));
    }

    [Theory]
    [InlineData(WorkflowStatus.Pending, WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Pending, WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Paused, WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Paused, WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Interrupted, WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Completed, WorkflowStatus.Running)]
    [InlineData(WorkflowStatus.Completed, WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Failed, WorkflowStatus.Running)]
    [InlineData(WorkflowStatus.Failed, WorkflowStatus.Completed)]
    public void Rejects_undefined_transition(
        WorkflowStatus current,
        WorkflowStatus next)
    {
        Assert.False(
            WorkflowLifecycle.CanTransition(current, next));
    }

    [Theory]
    [InlineData(WorkflowStatus.Completed)]
    [InlineData(WorkflowStatus.Failed)]
    public void Terminal_states_have_no_outgoing_transitions(
        WorkflowStatus terminal)
    {
        foreach (var next in Enum.GetValues<WorkflowStatus>())
        {
            Assert.False(
                WorkflowLifecycle.CanTransition(terminal, next));
        }
    }

    [Fact]
    public void Same_state_is_not_a_transition()
    {
        foreach (var status in Enum.GetValues<WorkflowStatus>())
        {
            Assert.False(
                WorkflowLifecycle.CanTransition(status, status));
        }
    }
}

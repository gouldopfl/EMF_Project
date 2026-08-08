namespace EMF.Core.Models.Workflow;

public sealed class WorkflowDefinition
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public required string Version { get; init; }

    public required IReadOnlyList<string> ActivityIds { get; init; }
}

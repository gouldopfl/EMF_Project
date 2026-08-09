namespace EMF.Orchestration.Models;

public sealed class WorkflowActivityResult
{
    public required bool Succeeded { get; init; }

    public string? Message { get; init; }

    public DateTimeOffset CompletedUtc { get; init; }
}

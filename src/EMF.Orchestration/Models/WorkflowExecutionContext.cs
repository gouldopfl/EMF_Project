using EMF.Core.Models.Identities;

namespace EMF.Orchestration.Models;

public sealed class WorkflowExecutionContext
{
    public required WorkflowId WorkflowId { get; init; }

    public required DateTimeOffset StartedUtc { get; init; }

    public required string CurrentStep { get; set; }

    public IDictionary<string, object> Metadata { get; init; }
        = new Dictionary<string, object>();
}

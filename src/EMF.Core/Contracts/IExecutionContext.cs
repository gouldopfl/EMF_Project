namespace EMF.Core.Contracts;

public interface IExecutionContext
{
    Guid ExecutionId { get; }

    CancellationToken CancellationToken { get; }
}
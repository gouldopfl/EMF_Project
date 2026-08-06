namespace EMF.Core.Contracts;

public interface IOperation
{
    string Name { get; }

    string Description { get; }

    Task<IOperationResult> ExecuteAsync(
        IExecutionContext context,
        CancellationToken cancellationToken = default);
}
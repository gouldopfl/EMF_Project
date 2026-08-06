namespace EMF.Core.Contracts;

public interface IOperationResult
{
    bool Success { get; }

    string? Message { get; }
}
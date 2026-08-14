using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Contracts;

public interface IIntelligenceCapability<
    TRequest,
    TResult>
    where TRequest : notnull
    where TResult : notnull
{
    IntelligenceCapabilityId Id { get; }

    Task<TResult> ExecuteAsync(
        TRequest request,
        IntelligenceExecutionContext context,
        CancellationToken cancellationToken = default);
}

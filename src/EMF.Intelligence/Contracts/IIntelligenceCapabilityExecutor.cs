using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Contracts;

public interface IIntelligenceCapabilityExecutor<
    TRequest,
    TResult>
    where TRequest : notnull
    where TResult : notnull
{
    Task<IntelligenceCapabilityResult<TResult>>
        ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default);
}

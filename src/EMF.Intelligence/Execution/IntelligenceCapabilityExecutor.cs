using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;

namespace EMF.Intelligence.Execution;

public sealed class IntelligenceCapabilityExecutor<
    TRequest,
    TResult>
    where TRequest : notnull
    where TResult : notnull
{
    private readonly
        IntelligenceCapabilityProviderRouter<
            TRequest,
            TResult> _router;

    public IntelligenceCapabilityExecutor(
        IntelligenceCapabilityProviderRouter<
            TRequest,
            TResult> router)
    {
        ArgumentNullException.ThrowIfNull(router);

        _router = router;
    }

    public async Task<
        IntelligenceCapabilityResult<TResult>>
        ExecuteAsync(
            IntelligenceCapabilityId capabilityId,
            TRequest request,
            IntelligenceExecutionContext context,
            CancellationToken cancellationToken =
                default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            capabilityId.Value);

        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);

        var provider =
            await _router.SelectAsync(
                capabilityId,
                context,
                cancellationToken);

        if (provider is null)
        {
            throw new
                IntelligenceProviderUnavailableException(
                    capabilityId);
        }

        return await provider.ExecuteAsync(
            request,
            context,
            cancellationToken);
    }
}

namespace EMF.Security.Authorization.Services;

public sealed class CompositeAuthorizationPolicy : IAuthorizationPolicy
{
    private readonly IAuthorizationPolicy _authorizationPolicy;
    private readonly IProtectionPolicy _protectionPolicy;

    public CompositeAuthorizationPolicy(
        IAuthorizationPolicy authorizationPolicy,
        IProtectionPolicy protectionPolicy)
    {
        ArgumentNullException.ThrowIfNull(authorizationPolicy);
        ArgumentNullException.ThrowIfNull(protectionPolicy);

        _authorizationPolicy = authorizationPolicy;
        _protectionPolicy = protectionPolicy;
    }

    public async Task<AuthorizationDecision> EvaluateAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var authorizationDecision =
            await _authorizationPolicy.EvaluateAsync(
                request,
                cancellationToken);

        if (authorizationDecision != AuthorizationDecision.Allow)
        {
            return AuthorizationDecision.Deny;
        }

        var protectionDecision =
            await _protectionPolicy.EvaluateAsync(
                request,
                cancellationToken);

        return protectionDecision == AuthorizationDecision.Allow
            ? AuthorizationDecision.Allow
            : AuthorizationDecision.Deny;
    }
}

namespace EMF.Security.Authorization;

public interface IAuthorizationPolicy
{
    Task<AuthorizationDecision> EvaluateAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

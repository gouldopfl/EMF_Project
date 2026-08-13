namespace EMF.Security.Authorization;

public interface IProtectionPolicy
{
    Task<AuthorizationDecision> EvaluateAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken = default);
}

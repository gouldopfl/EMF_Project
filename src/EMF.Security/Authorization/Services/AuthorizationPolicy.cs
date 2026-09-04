namespace EMF.Security.Authorization.Services;

public sealed class AuthorizationPolicy : IAuthorizationPolicy
{
    private readonly IAuthorizationContextProvider _contextProvider;

    public AuthorizationPolicy(
        IAuthorizationContextProvider contextProvider)
    {
        ArgumentNullException.ThrowIfNull(contextProvider);

        _contextProvider = contextProvider;
    }

    public async Task<AuthorizationDecision> EvaluateAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.SubjectId) ||
            string.IsNullOrWhiteSpace(request.ResourceType) ||
            string.IsNullOrWhiteSpace(request.ResourceId))
        {
            return AuthorizationDecision.Deny;
        }

        var context =
            await _contextProvider.GetContextAsync(
                request.SubjectId,
                cancellationToken);

        if (context is null)
        {
            return AuthorizationDecision.Deny;
        }

        if (!string.Equals(
                context.SubjectId,
                request.SubjectId,
                StringComparison.Ordinal))
        {
            return AuthorizationDecision.Deny;
        }

        return context.PermissionIds.Contains(
            request.PermissionId)
            ? AuthorizationDecision.Allow
            : AuthorizationDecision.Deny;
    }
}

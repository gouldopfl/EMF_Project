using EMF.Security.Models;

namespace EMF.Security.Authorization.Services;

public sealed class ProtectionPolicy : IProtectionPolicy
{
    public Task<AuthorizationDecision> EvaluateAsync(
        AuthorizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var classification =
            request.ProtectionClassificationId.Value;

        var decision =
            classification switch
            {
                ProtectionClassifications.Public
                    => AuthorizationDecision.Allow,

                ProtectionClassifications.Internal
                    => AuthorizationDecision.Allow,

                ProtectionClassifications.Confidential
                    => AuthorizationDecision.Allow,

                ProtectionClassifications.Restricted
                    => AuthorizationDecision.Deny,

                ProtectionClassifications.Regulated
                    => AuthorizationDecision.Deny,

                _ => AuthorizationDecision.Deny
            };

        return Task.FromResult(decision);
    }
}

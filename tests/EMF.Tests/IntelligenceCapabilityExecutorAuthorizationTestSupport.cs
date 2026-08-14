using EMF.Security.Authorization;

namespace EMF.Tests;

public sealed partial class
    IntelligenceCapabilityExecutorTests
{
    private sealed class RecordingAuthorizationPolicy :
        IAuthorizationPolicy
    {
        public AuthorizationDecision Decision
        {
            get;
            init;
        } = AuthorizationDecision.Allow;

        public List<AuthorizationRequest> Requests
        {
            get;
        } = [];

        public Task<AuthorizationDecision>
            EvaluateAsync(
                AuthorizationRequest request,
                CancellationToken cancellationToken =
                    default)
        {
            cancellationToken
                .ThrowIfCancellationRequested();

            Requests.Add(request);

            return Task.FromResult(Decision);
        }
    }
}

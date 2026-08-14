using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Models;

namespace EMF.Intelligence.Execution;

public sealed class IntelligenceCapabilityExecutor<
    TRequest,
    TResult> :
    IIntelligenceCapabilityExecutor<
        TRequest,
        TResult>
    where TRequest : notnull
    where TResult : notnull
{
    private readonly
        IntelligenceCapabilityProviderRouter<
            TRequest,
            TResult> _router;

    private readonly IAuthorizationPolicy
        _authorizationPolicy;

    private readonly IntelligenceCapabilityAuditWriter
        _auditWriter;

    public IntelligenceCapabilityExecutor(
        IntelligenceCapabilityProviderRouter<
            TRequest,
            TResult> router,
        IAuthorizationPolicy authorizationPolicy,
        ISecurityAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(
            authorizationPolicy);
        ArgumentNullException.ThrowIfNull(auditSink);

        _router = router;
        _authorizationPolicy =
            authorizationPolicy;
        _auditWriter =
            new IntelligenceCapabilityAuditWriter(
                auditSink);
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

        IIntelligenceCapabilityProvider<
            TRequest,
            TResult>? provider = null;

        IntelligenceExecutionMetadata? metadata = null;
        AuthorizationDecision? authorizationDecision = null;
        IntelligenceCapabilityResult<TResult> result;

        if (cancellationToken.IsCancellationRequested)
        {
            await _auditWriter.WriteAsync(
                capabilityId,
                context,
                null,
                null,
                null,
                SecurityAuditOutcome.Cancelled,
                DateTimeOffset.UtcNow);

            cancellationToken.ThrowIfCancellationRequested();
        }

        try
        {
            foreach (var artifactId in
                context.InputArtifactIds)
            {
                authorizationDecision =
                    await _authorizationPolicy
                        .EvaluateAsync(
                            new AuthorizationRequest
                            {
                                SubjectId =
                                    context.SubjectId,
                                PermissionId =
                                    SecurityPermissions
                                        .ArtifactIntelligenceUse,
                                ArtifactId = artifactId,
                                ProtectionClassificationId =
                                    context
                                        .ProtectionClassificationId
                            },
                            cancellationToken);

                if (authorizationDecision !=
                    AuthorizationDecision.Allow)
                {
                    await _auditWriter.WriteAsync(
                        capabilityId,
                        context,
                        null,
                        null,
                        authorizationDecision,
                        SecurityAuditOutcome.Denied,
                        DateTimeOffset.UtcNow);

                    throw new
                        IntelligenceInputAuthorizationException(
                            artifactId);
                }
            }

            provider =
                await _router.SelectAsync(
                    capabilityId,
                    context,
                    cancellationToken);

            if (provider is null)
            {
                await _auditWriter.WriteAsync(
                    capabilityId,
                    context,
                    null,
                    null,
                    AuthorizationDecision.Deny,
                    SecurityAuditOutcome.Denied,
                    DateTimeOffset.UtcNow);

                throw new
                    IntelligenceProviderUnavailableException(
                        capabilityId);
            }

            result =
                await provider.ExecuteAsync(
                    request,
                    context,
                    cancellationToken);

            metadata = result?.Metadata;

            IntelligenceCapabilityResultValidator.Validate(
                result,
                capabilityId,
                provider!.ProviderId,
                context);
        }
        catch (IntelligenceInputAuthorizationException)
        {
            throw;
        }
        catch (IntelligenceProviderUnavailableException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            await _auditWriter.WriteAsync(
                capabilityId,
                context,
                provider?.ProviderId,
                metadata,
                provider is null
                    ? null
                    : AuthorizationDecision.Allow,
                SecurityAuditOutcome.Cancelled,
                DateTimeOffset.UtcNow);

            throw;
        }
        catch (Exception)
        {
            await _auditWriter.WriteAsync(
                capabilityId,
                context,
                provider?.ProviderId,
                metadata,
                provider is null
                    ? null
                    : AuthorizationDecision.Allow,
                SecurityAuditOutcome.Failed,
                DateTimeOffset.UtcNow);

            throw;
        }

        await _auditWriter.WriteAsync(
            capabilityId,
            context,
            provider!.ProviderId,
            result.Metadata,
            AuthorizationDecision.Allow,
            result.Success
                ? SecurityAuditOutcome.Succeeded
                : SecurityAuditOutcome.Failed,
            DateTimeOffset.UtcNow);

        return result;
    }
}

using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.Routing;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;

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

    private readonly IntelligenceCapabilityAuditWriter
        _auditWriter;

    public IntelligenceCapabilityExecutor(
        IntelligenceCapabilityProviderRouter<
            TRequest,
            TResult> router,
        ISecurityAuditSink auditSink)
    {
        ArgumentNullException.ThrowIfNull(router);
        ArgumentNullException.ThrowIfNull(auditSink);

        _router = router;
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

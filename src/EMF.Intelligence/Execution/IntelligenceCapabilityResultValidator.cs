using System.Diagnostics.CodeAnalysis;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Execution;

internal static class
    IntelligenceCapabilityResultValidator
{
    public static void Validate<TResult>(
        IntelligenceCapabilityResult<TResult>? result,
        IntelligenceCapabilityId capabilityId,
        IntelligenceProviderId providerId,
        IntelligenceExecutionContext context)
        where TResult : notnull
    {
        if (result is null)
        {
            Fail("The provider returned no result.");
        }

        if (result.Metadata is null)
        {
            Fail("The provider returned no execution metadata.");
        }

        var metadata = result.Metadata;

        if (metadata.CapabilityId != capabilityId)
        {
            Fail("The result capability does not match the request.");
        }

        if (metadata.ProviderId != providerId)
        {
            Fail("The result provider does not match the selected provider.");
        }

        if (metadata.CorrelationId != context.CorrelationId)
        {
            Fail("The result correlation does not match the execution context.");
        }

        if (string.IsNullOrWhiteSpace(
                metadata.EngineName))
        {
            Fail("The result engine name is required.");
        }

        if (metadata.StartedUtc == default ||
            metadata.CompletedUtc == default ||
            metadata.CompletedUtc <
                metadata.StartedUtc)
        {
            Fail("The result execution timestamps are invalid.");
        }

        if (result.Success && result.Output is null)
        {
            Fail("A successful result must contain output.");
        }
    }

    [DoesNotReturn]
    private static void Fail(string reason)
    {
        throw new
            IntelligenceCapabilityResultValidationException(
                reason);
    }
}

using System.Diagnostics.CodeAnalysis;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Execution;

internal static class
    IntelligenceCapabilityResultValidator
{
    public static void Validate<TResult>(
        [NotNull] IntelligenceCapabilityResult<TResult>? result,
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

        ValidateUsage(metadata);

        if (result.Success && result.Output is null)
        {
            Fail("A successful result must contain output.");
        }
    }

    private static void ValidateUsage(
        IntelligenceExecutionMetadata metadata)
    {
        var hasInput = metadata.InputTokenCount.HasValue;
        var hasOutput = metadata.OutputTokenCount.HasValue;
        var hasTotal = metadata.TotalTokenCount.HasValue;

        if (hasInput || hasOutput || hasTotal)
        {
            if (!hasInput || !hasOutput || !hasTotal)
            {
                Fail("Token usage metadata must be complete.");
            }

            if (metadata.InputTokenCount < 0 ||
                metadata.OutputTokenCount < 0 ||
                metadata.TotalTokenCount < 0 ||
                metadata.TotalTokenCount !=
                    metadata.InputTokenCount +
                    metadata.OutputTokenCount)
            {
                Fail("Token usage metadata is invalid.");
            }
        }

        var hasInputRate =
            metadata.InputCostUsdPerMillionTokens.HasValue;
        var hasOutputRate =
            metadata.OutputCostUsdPerMillionTokens.HasValue;

        if (hasInputRate != hasOutputRate)
        {
            Fail("Cost-rate metadata must be complete.");
        }

        if (metadata.InputCostUsdPerMillionTokens is < 0 ||
            metadata.OutputCostUsdPerMillionTokens is < 0 ||
            metadata.EstimatedCostUsd is < 0)
        {
            Fail("Cost metadata cannot be negative.");
        }

        if (metadata.EstimatedCostUsd.HasValue &&
            (!hasInput || !hasOutput ||
             !hasInputRate || !hasOutputRate))
        {
            Fail("Estimated cost requires token usage and cost rates.");
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

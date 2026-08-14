using System.Diagnostics.CodeAnalysis;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Agents;

internal static class IntelligenceAgentResultValidator
{
    public static void Validate<TResult>(
        [NotNull]
        IntelligenceAgentResult<TResult>? result,
        AgentId agentId,
        IntelligenceExecutionContext context)
        where TResult : notnull
    {
        if (result is null)
        {
            Fail("The agent returned no result.");
        }

        if (result.AgentId != agentId)
        {
            Fail(
                "The result agent does not match " +
                "the requested agent.");
        }

        if (result.CorrelationId !=
            context.CorrelationId)
        {
            Fail(
                "The result correlation does not " +
                "match the execution context.");
        }

        if (result.StartedUtc == default ||
            result.CompletedUtc == default ||
            result.CompletedUtc < result.StartedUtc)
        {
            Fail(
                "The agent execution timestamps " +
                "are invalid.");
        }

        if (result.Success && result.Output is null)
        {
            Fail(
                "A successful agent result must " +
                "contain output.");
        }

        if (result.CapabilityExecutions is null)
        {
            Fail(
                "Capability execution metadata " +
                "cannot be null.");
        }
    }

    [DoesNotReturn]
    private static void Fail(string reason)
    {
        throw new
            IntelligenceAgentResultValidationException(
                reason);
    }
}

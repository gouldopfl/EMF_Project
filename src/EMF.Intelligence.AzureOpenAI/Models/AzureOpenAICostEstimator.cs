namespace EMF.Intelligence.AzureOpenAI.Models;

internal static class AzureOpenAICostEstimator
{
    public static decimal? EstimateUsd(
        int? inputTokenCount,
        int? outputTokenCount,
        decimal? inputCostUsdPerMillionTokens,
        decimal? outputCostUsdPerMillionTokens)
    {
        if (!inputTokenCount.HasValue ||
            !outputTokenCount.HasValue ||
            !inputCostUsdPerMillionTokens.HasValue ||
            !outputCostUsdPerMillionTokens.HasValue)
        {
            return null;
        }

        if (inputTokenCount.Value < 0 ||
            outputTokenCount.Value < 0 ||
            inputCostUsdPerMillionTokens.Value < 0 ||
            outputCostUsdPerMillionTokens.Value < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(inputTokenCount),
                "Token counts and cost rates cannot be negative.");
        }

        return
            inputTokenCount.Value /
                1_000_000m *
                inputCostUsdPerMillionTokens.Value +
            outputTokenCount.Value /
                1_000_000m *
                outputCostUsdPerMillionTokens.Value;
    }
}

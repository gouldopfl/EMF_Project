using EMF.Intelligence.AzureOpenAI.Exceptions;

namespace EMF.Tests;

public sealed class AzureOpenAIFailureClassifierTests
{
    [Theory]
    [InlineData(
        0,
        AzureOpenAIFailureKind.Transport)]
    [InlineData(
        401,
        AzureOpenAIFailureKind.Authentication)]
    [InlineData(
        403,
        AzureOpenAIFailureKind.Authorization)]
    [InlineData(
        408,
        AzureOpenAIFailureKind.Timeout)]
    [InlineData(
        429,
        AzureOpenAIFailureKind.Throttling)]
    [InlineData(
        504,
        AzureOpenAIFailureKind.Timeout)]
    [InlineData(
        500,
        AzureOpenAIFailureKind.Provider)]
    public void Classify_ReturnsExpectedFailureKind(
        int statusCode,
        AzureOpenAIFailureKind expected)
    {
        Assert.Equal(
            expected,
            AzureOpenAIFailureClassifier.Classify(
                statusCode));
    }
}

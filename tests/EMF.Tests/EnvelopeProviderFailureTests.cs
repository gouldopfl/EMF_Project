using Azure;
using EMF.Security.Azure.Encryption;
using EMF.Security.Azure.Keys;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Encryption.Envelope;
namespace EMF.Tests;

public sealed class EnvelopeProviderFailureTests
{
    [Theory]
    [InlineData(404, EnvelopeFailureCategory.KeyUnavailable, EnvelopeFailureRetryability.No)]
    [InlineData(403, EnvelopeFailureCategory.AccessDenied, EnvelopeFailureRetryability.No)]
    [InlineData(429, EnvelopeFailureCategory.Throttled, EnvelopeFailureRetryability.Yes)]
    [InlineData(503, EnvelopeFailureCategory.ProviderUnavailable, EnvelopeFailureRetryability.Yes)]
    [InlineData(500, EnvelopeFailureCategory.UnknownProviderFailure, EnvelopeFailureRetryability.Unknown)]
    public async Task Azure_structured_statuses_are_sanitized_without_diagnostic_inner_exception(int status, EnvelopeFailureCategory category, EnvelopeFailureRetryability retryability)
    {
        var failure = new RequestFailedException(status, "synthetic-sensitive-marker", "unknown-code", new Exception("nested-sensitive-marker"));
        var service = new AzureEnvelopeEncryptionService(new FailingKeys(failure), new NeverFactory());
        var outward = await Assert.ThrowsAsync<EnvelopeProviderFailure>(() => service.EncryptAsync(new byte[] { 1 }));
        Assert.Equal(category, outward.Category); Assert.Equal(retryability, outward.Retryability); Assert.Null(outward.InnerException);
        Assert.DoesNotContain("sensitive-marker", outward.ToString()); Assert.Equal(32, outward.CorrelationId.Length);
    }
    [Fact]
    public async Task Cancellation_retains_token_and_unexpected_provider_errors_have_unknown_retryability()
    {
        using var source = new CancellationTokenSource(); source.Cancel();
        var cancellation = new OperationCanceledException(source.Token);
        var service = new AzureEnvelopeEncryptionService(new FailingKeys(cancellation), new NeverFactory());
        var outward = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EncryptAsync(new byte[] { 1 }, source.Token));
        Assert.Equal(source.Token, outward.CancellationToken);
        var unknown = await Assert.ThrowsAsync<EnvelopeProviderFailure>(() => EnvelopeProviderBoundary.CallAsync<int>(
            () => Task.FromException<int>(new Exception("synthetic-sensitive-marker")), EnvelopeKeyOperation.WrapKey, "development"));
        Assert.Equal(EnvelopeFailureRetryability.Unknown, unknown.Retryability); Assert.Null(unknown.InnerException);
        Assert.DoesNotContain("synthetic-sensitive-marker", unknown.ToString());
    }
    private sealed class FailingKeys(Exception failure) : IAzureKeyReferenceProvider
    {
        public Task<AzureKeyReference> GetCurrentKeyAsync(CancellationToken ct = default) => Task.FromException<AzureKeyReference>(failure);
        public Task<AzureKeyReference?> GetKeyAsync(string id, CancellationToken ct = default) => Task.FromException<AzureKeyReference?>(failure);
    }
    private sealed class NeverFactory : IAzureKeyCryptographyFactory
    { public IAzureKeyCryptography Create(AzureKeyReference reference) => throw new InvalidOperationException("Factory must not run."); }
}

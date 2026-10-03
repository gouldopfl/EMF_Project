using Azure;
using EMF.Security.Encryption.Envelope;
namespace EMF.Security.Azure.Encryption;

internal static class AzureEnvelopeProviderBoundary
{
    internal static async Task<T> CallAsync<T>(Func<Task<T>> call, EnvelopeKeyOperation operation)
    {
        try { return await call(); }
        catch (OperationCanceledException) { throw; }
        catch (EnvelopeProviderFailure) { throw; }
        catch (RequestFailedException error)
        {
            var (category, retryability) = error.Status switch
            {
                404 => (EnvelopeFailureCategory.KeyUnavailable, EnvelopeFailureRetryability.No),
                401 or 403 => (EnvelopeFailureCategory.AccessDenied, EnvelopeFailureRetryability.No),
                429 => (EnvelopeFailureCategory.Throttled, EnvelopeFailureRetryability.Yes),
                502 or 503 or 504 => (EnvelopeFailureCategory.ProviderUnavailable, EnvelopeFailureRetryability.Yes),
                _ => (EnvelopeFailureCategory.UnknownProviderFailure, EnvelopeFailureRetryability.Unknown)
            };
            throw new EnvelopeProviderFailure(operation, category, "azure", retryability);
        }
        catch { throw new EnvelopeProviderFailure(operation, EnvelopeFailureCategory.UnknownProviderFailure, "azure"); }
    }
}

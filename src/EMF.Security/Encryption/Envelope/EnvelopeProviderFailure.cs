using System.Security.Cryptography;
namespace EMF.Security.Encryption.Envelope;

public enum EnvelopeFailureCategory
{
    KeyUnavailable, AccessDenied, ProviderUnavailable, Throttled,
    InvalidEnvelope, AuthenticationFailed, UnsupportedAlgorithm, KeyVerificationFailed, UnknownProviderFailure
}
public enum EnvelopeFailureRetryability { Yes, No, Unknown }
public enum EnvelopeKeyOperation { ResolveHistoricalKey, ResolveCurrentKey, WrapKey, UnwrapKey, AuthenticateContent }
public sealed class EnvelopeProviderFailure : CryptographicException
{
    public EnvelopeKeyOperation Operation { get; }
    public EnvelopeFailureCategory Category { get; }
    public EnvelopeFailureRetryability Retryability { get; }
    public string Provider { get; }
    public string CorrelationId { get; } = Guid.NewGuid().ToString("N");
    public EnvelopeProviderFailure(EnvelopeKeyOperation operation, EnvelopeFailureCategory category, string provider,
        EnvelopeFailureRetryability retryability = EnvelopeFailureRetryability.Unknown)
        : base("Envelope key operation could not be completed.")
    {
        if (provider is not ("azure" or "development")) throw new ArgumentException("Unapproved provider identity.");
        if (!Enum.IsDefined(operation) || !Enum.IsDefined(category) || !Enum.IsDefined(retryability)) throw new ArgumentException("Invalid failure category.");
        Operation = operation; Category = category; Provider = provider; Retryability = retryability;
    }
}
public static class EnvelopeProviderBoundary
{
    public static async Task<T> CallAsync<T>(Func<Task<T>> call, EnvelopeKeyOperation operation, string provider)
    {
        try { return await call(); }
        catch (OperationCanceledException) { throw; }
        catch (EnvelopeProviderFailure) { throw; }
        catch { throw new EnvelopeProviderFailure(operation, EnvelopeFailureCategory.UnknownProviderFailure, provider); }
    }
}

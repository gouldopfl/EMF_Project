using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using EMF.Security.Encryption.Envelope.Models;
[assembly: InternalsVisibleTo("EMF.Tests")]
namespace EMF.Security.Encryption.Envelope;

// Shared provider cryptography. Lifecycle callers never receive authentication plaintext.
public static class EnvelopeContentAuthentication
{
    public const int DefaultMaximumPlaintextBytes = 64 * 1024 * 1024;
    public static byte[] Decrypt(EncryptedEnvelope envelope, ReadOnlySpan<byte> dek,
        ReadOnlyMemory<byte>? context)
    {
        var aad = envelope.FormatVersion == EncryptedEnvelopeFormat.ContextBoundVersion
            ? EncryptedEnvelopeFormat.GetContextBoundAuthenticatedData(envelope.Algorithm,
                context ?? throw new CryptographicException("Authenticated context is required."))
            : EncryptedEnvelopeFormat.GetAuthenticatedData(envelope.FormatVersion, envelope.Algorithm);
        var plaintext = new byte[envelope.Ciphertext.Length];
        try
        {
            using var aes = new AesGcm(dek, 16);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.AuthenticationTag, plaintext, aad);
            return plaintext;
        }
        catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
    }
    public static byte[] DecryptBounded(EncryptedEnvelope envelope, ReadOnlySpan<byte> dek,
        ReadOnlyMemory<byte> context, EnvelopeDecryptionLimits limits, CancellationToken cancellationToken = default)
        => DecryptBounded(envelope, dek, context, limits, cancellationToken, null, null);

    internal static byte[] DecryptBounded(EncryptedEnvelope envelope, ReadOnlySpan<byte> dek,
        ReadOnlyMemory<byte> context, EnvelopeDecryptionLimits limits, CancellationToken ct,
        Action<byte[]>? allocated, Action? decrypted)
    {
        ArgumentNullException.ThrowIfNull(limits);
        EncryptedEnvelopeFormat.Validate(envelope);
        if (envelope.FormatVersion != EncryptedEnvelopeFormat.ContextBoundVersion ||
            context.Length > limits.MaximumAuthenticatedContextBytes)
            throw new CryptographicException("Bounded context-bound envelope required.");
        ct.ThrowIfCancellationRequested();
        // The fixed AAD prefix is 27 bytes. Context is checked before that allocation.
        _ = checked(27 + context.Length);
        var aad = EncryptedEnvelopeFormat.GetContextBoundAuthenticatedData(envelope.Algorithm, context);
        byte[]? plaintext = null;
        try
        {
            // Enforced at the actual plaintext allocation site, independently of the codec.
            if (envelope.Ciphertext.Length > limits.MaximumPlaintextBytes)
                throw new CryptographicException("Plaintext allocation ceiling exceeded.");
            ct.ThrowIfCancellationRequested();
            plaintext = new byte[envelope.Ciphertext.Length];
            allocated?.Invoke(plaintext);
            ct.ThrowIfCancellationRequested();
            using var aes = new AesGcm(dek, 16);
            aes.Decrypt(envelope.Nonce, envelope.Ciphertext, envelope.AuthenticationTag, plaintext, aad);
            decrypted?.Invoke();
            ct.ThrowIfCancellationRequested();
            return plaintext;
        }
        catch { if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext); throw; }
        finally { CryptographicOperations.ZeroMemory(aad); }
    }
    public static void ValidateBound(EncryptedEnvelope envelope, int maximumBytes)
    {
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        EncryptedEnvelopeFormat.Validate(envelope);
        if (envelope.Ciphertext.Length > maximumBytes || envelope.WrappedDataEncryptionKey.Length > 16384 ||
            envelope.KeyEncryptionKeyId.Length > 1024)
            throw new CryptographicException("Envelope authentication size limit exceeded.");
    }
    public static void Authenticate(EncryptedEnvelope envelope, ReadOnlySpan<byte> dek,
        ReadOnlyMemory<byte>? context, int maximumBytes, string provider = "development")
    {
        ValidateBound(envelope, maximumBytes);
        byte[] plaintext;
        try { plaintext = Decrypt(envelope, dek, context); }
        catch (CryptographicException)
        {
            throw new EnvelopeProviderFailure(EnvelopeKeyOperation.AuthenticateContent,
            EnvelopeFailureCategory.AuthenticationFailed, provider, EnvelopeFailureRetryability.No);
        }
        try { /* Authentication alone: no consumer is given this buffer. */ }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
    }
}

using System.Security.Cryptography;
using EMF.Security.Encryption.Envelope.Models;
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

using System.Security.Cryptography;
using System.Text;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Encryption.Envelope;

public static class EncryptedEnvelopeFormat
{
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    public const int LegacyVersion = 0;
    public const int CurrentVersion = 1;
    public const int ContextBoundVersion = 2;
    public const string Aes256GcmAlgorithm =
        "AES-256-GCM";


    public static void Validate(
        EncryptedEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        Validate(
            envelope.FormatVersion,
            envelope.Algorithm);

        if (envelope.Ciphertext is null ||
            envelope.Nonce is not { Length: 12 } ||
            envelope.AuthenticationTag is not { Length: 16 } ||
            envelope.WrappedDataEncryptionKey is not
            { Length: > 0 } ||
            envelope.WrappedDataEncryptionKey.Length > 16384 ||
            string.IsNullOrWhiteSpace(envelope.KeyEncryptionKeyId) ||
            envelope.KeyEncryptionKeyId.Length > 1024 || envelope.KeyEncryptionKeyId.Any(char.IsControl))
        {
            throw new CryptographicException(
                "Encrypted envelope structure is invalid.");
        }
        try
        {
            if (StrictUtf8.GetByteCount(envelope.KeyEncryptionKeyId) > 1024)
                throw new CryptographicException("Envelope key identity exceeds its bound.");
        }
        catch (EncoderFallbackException) { throw new CryptographicException("Envelope key identity is invalid."); }
    }


    public static void Validate(
        int formatVersion,
        string algorithm)
    {
        if (!string.Equals(
                algorithm,
                Aes256GcmAlgorithm,
                StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Unsupported envelope algorithm.");
        }

        if (formatVersion is not (
            LegacyVersion
            or CurrentVersion
            or ContextBoundVersion))
        {
            throw new CryptographicException(
                "Unsupported envelope format version.");
        }
    }

    public static byte[] GetAuthenticatedData(
        int formatVersion,
        string algorithm)
    {
        if (!string.Equals(
                algorithm,
                Aes256GcmAlgorithm,
                StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Unsupported envelope algorithm.");
        }

        return formatVersion switch
        {
            LegacyVersion =>
                Array.Empty<byte>(),
            CurrentVersion =>
                Encoding.UTF8.GetBytes(
                    $"EMF-ENVELOPE\0" +
                    $"{CurrentVersion}\0" +
                    $"{Aes256GcmAlgorithm}"),
            _ =>
                throw new CryptographicException(
                    "Unsupported envelope format version.")
        };
    }

    public static byte[] GetContextBoundAuthenticatedData(
        string algorithm,
        ReadOnlyMemory<byte> authenticatedContext)
    {
        if (authenticatedContext.IsEmpty)
            throw new CryptographicException(
                "Authenticated context is required.");

        var prefix = GetAuthenticatedData(
            CurrentVersion,
            algorithm);

        var result =
            new byte[prefix.Length + 1 + authenticatedContext.Length];

        prefix.CopyTo(result, 0);
        result[prefix.Length] = 0;

        authenticatedContext.Span.CopyTo(
            result.AsSpan(prefix.Length + 1));

        return result;
    }

}

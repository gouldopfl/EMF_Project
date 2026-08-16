using System.Security.Cryptography;
using System.Text;

namespace EMF.Security.Encryption.Envelope;

public static class EncryptedEnvelopeFormat
{
    public const int LegacyVersion = 0;
    public const int CurrentVersion = 1;
    public const string Aes256GcmAlgorithm =
        "AES-256-GCM";

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
}

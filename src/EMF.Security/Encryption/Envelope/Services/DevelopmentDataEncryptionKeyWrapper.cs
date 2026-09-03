using System.Security.Cryptography;

namespace EMF.Security.Encryption.Envelope.Services;

internal static class DevelopmentDataEncryptionKeyWrapper
{
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public static byte[] Wrap(
        byte[] keyEncryptionKey,
        byte[] dataEncryptionKey)
    {
        ArgumentNullException.ThrowIfNull(
            keyEncryptionKey);

        ArgumentNullException.ThrowIfNull(
            dataEncryptionKey);

        var nonce =
            RandomNumberGenerator.GetBytes(
                NonceSize);

        var ciphertext =
            new byte[dataEncryptionKey.Length];

        var tag = new byte[TagSize];

        using var aes =
            new AesGcm(
                keyEncryptionKey,
                TagSize);

        aes.Encrypt(
            nonce,
            dataEncryptionKey,
            ciphertext,
            tag);

        var result =
            new byte[
                NonceSize +
                TagSize +
                ciphertext.Length];

        Buffer.BlockCopy(
            nonce,
            0,
            result,
            0,
            NonceSize);

        Buffer.BlockCopy(
            tag,
            0,
            result,
            NonceSize,
            TagSize);

        Buffer.BlockCopy(
            ciphertext,
            0,
            result,
            NonceSize + TagSize,
            ciphertext.Length);

        return result;
    }

    public static byte[] Unwrap(
        byte[] keyEncryptionKey,
        byte[] wrappedDataEncryptionKey)
    {
        ArgumentNullException.ThrowIfNull(
            keyEncryptionKey);

        ArgumentNullException.ThrowIfNull(
            wrappedDataEncryptionKey);

        if (wrappedDataEncryptionKey.Length <
            NonceSize + TagSize)
        {
            throw new CryptographicException(
                "Invalid wrapped data-encryption key.");
        }

        var nonce =
            wrappedDataEncryptionKey[..NonceSize];

        var tag =
            wrappedDataEncryptionKey[
                NonceSize..(NonceSize + TagSize)];

        var ciphertext =
            wrappedDataEncryptionKey[
                (NonceSize + TagSize)..];

        var dataEncryptionKey =
            new byte[ciphertext.Length];

        try
        {
            using var aes =
                new AesGcm(
                    keyEncryptionKey,
                    TagSize);

            aes.Decrypt(
                nonce,
                ciphertext,
                tag,
                dataEncryptionKey);

            return dataEncryptionKey;
        }
        catch
        {
            CryptographicOperations.ZeroMemory(
                dataEncryptionKey);
            throw;
        }
    }
}

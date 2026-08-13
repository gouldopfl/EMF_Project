namespace EMF.Security.Encryption.Envelope.Models;

public sealed class EncryptedEnvelope
{
    public required byte[] Ciphertext { get; init; }

    public required byte[] Nonce { get; init; }

    public required byte[] AuthenticationTag { get; init; }

    public required byte[] WrappedDataEncryptionKey { get; init; }

    public required string KeyEncryptionKeyId { get; init; }

    public required string Algorithm { get; init; }
}

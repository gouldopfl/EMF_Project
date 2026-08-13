namespace EMF.Security.Encryption;

public sealed class EncryptedContent
{
    public required byte[] Ciphertext { get; init; }

    public required byte[] Nonce { get; init; }

    public required byte[] AuthenticationTag { get; init; }

    public required string KeyId { get; init; }
}

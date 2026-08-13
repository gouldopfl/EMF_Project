namespace EMF.Security.Encryption.Providers.Models;

public sealed class ContentDecryptionRequest
{
    public required byte[] Ciphertext { get; init; }

    public required byte[] Nonce { get; init; }

    public required byte[] AuthenticationTag { get; init; }

    public required string KeyId { get; init; }
}

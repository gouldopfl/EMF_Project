namespace EMF.Security.Encryption.Models;

public sealed class EncryptionKey
{
    public required string KeyId { get; init; }

    public required byte[] KeyMaterial { get; init; }
}

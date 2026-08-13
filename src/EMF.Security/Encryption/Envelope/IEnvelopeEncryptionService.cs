using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Encryption.Envelope;

public interface IEnvelopeEncryptionService
{
    Task<EncryptedEnvelope> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default);

    Task<byte[]> DecryptAsync(
        EncryptedEnvelope envelope,
        CancellationToken cancellationToken = default);
}

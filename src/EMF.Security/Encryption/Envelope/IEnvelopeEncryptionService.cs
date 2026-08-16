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

    Task<EncryptedEnvelope> EncryptWithContextAsync(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> authenticatedContext,
        CancellationToken cancellationToken = default);

    Task<byte[]> DecryptWithContextAsync(
        EncryptedEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedContext,
        CancellationToken cancellationToken = default);
}

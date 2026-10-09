using EMF.Security.Encryption.Envelope.Models;
namespace EMF.Security.Encryption.Envelope;

public interface IBoundedEnvelopeDecryptionService
{
    Task<byte[]> DecryptWithContextBoundedAsync(EncryptedEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedContext, EnvelopeDecryptionLimits limits,
        CancellationToken cancellationToken = default);
}

using EMF.Security.Encryption.Envelope.Models;
namespace EMF.Security.Encryption.Envelope;

public interface IAuthenticatedEnvelopeKeyRewrappingService
{
    Task<EncryptedEnvelope> RewrapAuthenticatedAsync(EncryptedEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedContext, CancellationToken cancellationToken = default);
}

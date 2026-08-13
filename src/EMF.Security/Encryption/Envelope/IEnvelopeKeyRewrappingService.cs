using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Encryption.Envelope;

public interface IEnvelopeKeyRewrappingService
{
    Task<EncryptedEnvelope> RewrapAsync(
        EncryptedEnvelope envelope,
        CancellationToken cancellationToken = default);
}

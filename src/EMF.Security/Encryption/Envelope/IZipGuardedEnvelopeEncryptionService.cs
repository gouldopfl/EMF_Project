using EMF.Core.Contracts.Zip;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Encryption.Envelope;

/// <summary>Optional transient ZIP authority capability; authority is never envelope metadata.</summary>
public interface IZipGuardedEnvelopeEncryptionService
{
    Task<EncryptedEnvelope> EncryptWithContextGuardedAsync(ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> authenticatedContext, IZipEffectBoundaryGuard guard,
        CancellationToken cancellationToken = default);

    Task<byte[]> DecryptWithContextBoundedGuardedAsync(EncryptedEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedContext, EnvelopeDecryptionLimits limits,
        IZipEffectBoundaryGuard guard, CancellationToken cancellationToken = default);
}

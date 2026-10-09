using EMF.Core.Contracts.Storage;
using EMF.Security.Encryption.Envelope;
namespace EMF.Orchestration.Models;

/// <summary>Enforced parent array/representation ceilings, not a total process-memory peak bound.
/// Runtime crypto/JSON/ZIP bookkeeping and retained child buffers are outside these counts.</summary>
public sealed class ZipParentAllocationProfile
{
    public const int FirstSliceMaximumPlaintextBytes = 33554432;
    public int MaximumPlaintextBytes { get; }
    public int MaximumDecodedCiphertextBytes => MaximumPlaintextBytes;
    public long MaximumEncodedCiphertextBytes { get; }
    public long MaximumProtectedSourceBytes { get; }
    public long MaximumRetainedEnvelopeBytes => MaximumProtectedSourceBytes;
    public long MaximumPhysicalPreparationBytes => MaximumProtectedSourceBytes;
    // Simultaneously live major buffers only; no claim about all managed/native allocations.
    public long ReadMajorBufferBytes => checked(MaximumProtectedSourceBytes + 2L * MaximumPlaintextBytes);
    // This three-buffer phase assumes ciphertext has already been cleared/released.
    public long PreparationAfterCiphertextReleaseMajorBufferBytes => checked(2L * MaximumProtectedSourceBytes + MaximumPlaintextBytes);
    public long ConservativeFourMajorBufferBytes => checked(2L * MaximumProtectedSourceBytes + 2L * MaximumPlaintextBytes);
    public ZipParentAllocationProfile(long maximumPlaintextBytes = FirstSliceMaximumPlaintextBytes)
    {
        if (maximumPlaintextBytes <= 0 || maximumPlaintextBytes > FirstSliceMaximumPlaintextBytes)
            throw new ArgumentOutOfRangeException(nameof(maximumPlaintextBytes));
        MaximumPlaintextBytes = checked((int)maximumPlaintextBytes);
        MaximumEncodedCiphertextBytes = BoundedEncryptedEnvelopeCodec.Base64Length(maximumPlaintextBytes);
        MaximumProtectedSourceBytes = BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(maximumPlaintextBytes);
        _ = new BoundedArtifactContentReadRequest(MaximumProtectedSourceBytes, maximumPlaintextBytes);
    }
    public void ValidateProviderMaximum(long maximumStoredBytes)
    {
        if (maximumStoredBytes < MaximumProtectedSourceBytes)
            throw new InvalidOperationException("Provider maximum cannot support the selected ZIP allocation profile.");
    }
    public BoundedArtifactContentReadRequest CreateReadRequest() =>
        new(MaximumProtectedSourceBytes, MaximumPlaintextBytes);
}

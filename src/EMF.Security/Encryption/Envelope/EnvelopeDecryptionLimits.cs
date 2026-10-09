namespace EMF.Security.Encryption.Envelope;

public sealed class EnvelopeDecryptionLimits
{
    public int MaximumPlaintextBytes { get; }
    public int MaximumAuthenticatedContextBytes { get; }
    public EnvelopeDecryptionLimits(long maximumPlaintextBytes, long maximumAuthenticatedContextBytes = 256)
    {
        if (maximumPlaintextBytes <= 0 || maximumPlaintextBytes > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(maximumPlaintextBytes));
        // Context-bound AAD adds a fixed 27 bytes.
        if (maximumAuthenticatedContextBytes <= 0 || maximumAuthenticatedContextBytes > Array.MaxLength - 27L)
            throw new ArgumentOutOfRangeException(nameof(maximumAuthenticatedContextBytes));
        MaximumPlaintextBytes = checked((int)maximumPlaintextBytes);
        MaximumAuthenticatedContextBytes = checked((int)maximumAuthenticatedContextBytes);
    }
}

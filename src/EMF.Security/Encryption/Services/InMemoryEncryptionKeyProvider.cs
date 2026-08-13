using EMF.Security.Encryption.Models;

namespace EMF.Security.Encryption.Services;

public sealed class InMemoryEncryptionKeyProvider :
    IEncryptionKeyProvider
{
    private readonly IReadOnlyDictionary<string, EncryptionKey> _keys;

    public InMemoryEncryptionKeyProvider(
        IEnumerable<EncryptionKey> keys)
    {
        ArgumentNullException.ThrowIfNull(keys);

        _keys = keys.ToDictionary(
            key => key.KeyId,
            StringComparer.Ordinal);
    }

    public Task<string?> GetCurrentKeyIdAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            _keys.Keys.FirstOrDefault());
    }

    public Task<EncryptionKey?> GetKeyAsync(
        string keyId,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _keys.TryGetValue(keyId, out var key);

        return Task.FromResult(key);
    }
}

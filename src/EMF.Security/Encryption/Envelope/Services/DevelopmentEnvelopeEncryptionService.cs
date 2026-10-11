using EMF.Core.Contracts.Zip;
using System.Security.Cryptography;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Encryption.Envelope.Services;

public sealed class DevelopmentEnvelopeEncryptionService :
    IEnvelopeEncryptionService, IBoundedEnvelopeDecryptionService, IZipGuardedEnvelopeEncryptionService
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    private readonly IEncryptionKeyProvider _keyProvider;
    internal Action<byte[]>? BoundedDataKeyUnwrapped { get; set; }

    public DevelopmentEnvelopeEncryptionService(
        IEncryptionKeyProvider keyProvider)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        _keyProvider = keyProvider;
    }

    public Task<EncryptedEnvelope> EncryptAsync(
        ReadOnlyMemory<byte> plaintext,
        CancellationToken cancellationToken = default) =>
        EncryptCoreAsync(plaintext, null, cancellationToken);

    public Task<EncryptedEnvelope> EncryptWithContextAsync(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> authenticatedContext,
        CancellationToken cancellationToken = default) =>
        EncryptCoreAsync(
            plaintext,
            authenticatedContext,
            cancellationToken);

    public async Task<EncryptedEnvelope> EncryptWithContextGuardedAsync(ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte> authenticatedContext, IZipEffectBoundaryGuard guard,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, guard.Invalidated);
        await guard.RevalidateAsync(linked.Token);
        var envelope = await EncryptCoreAsync(plaintext, authenticatedContext, linked.Token, guard);
        try
        {
            await guard.RevalidateAsync(linked.Token);
            using (AcceptZipEffect(guard,linked.Token)) { }
            linked.Token.ThrowIfCancellationRequested();
            return envelope;
        }
        catch { BoundedEncryptedEnvelopeCodec.Clear(envelope); throw; }
    }

    public async Task<byte[]> DecryptWithContextBoundedGuardedAsync(EncryptedEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedContext, EnvelopeDecryptionLimits limits,
        IZipEffectBoundaryGuard guard, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(guard);
        ArgumentNullException.ThrowIfNull(limits);
        EnvelopeContentAuthentication.ValidateBound(envelope, limits.MaximumPlaintextBytes);
        if (authenticatedContext.Length > limits.MaximumAuthenticatedContextBytes || envelope.FormatVersion != 2)
            throw new CryptographicException("Bounded context-bound envelope required.");
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, guard.Invalidated);
        await guard.RevalidateAsync(linked.Token);
        var plaintext = await DecryptCoreAsync(envelope, authenticatedContext, linked.Token, limits, guard);
        try
        {
            await guard.RevalidateAsync(linked.Token);
            using (AcceptZipEffect(guard,linked.Token)) { }
            linked.Token.ThrowIfCancellationRequested();
            return plaintext;
        }
        catch { CryptographicOperations.ZeroMemory(plaintext); throw; }
    }

    private async Task<EncryptedEnvelope> EncryptCoreAsync(
        ReadOnlyMemory<byte> plaintext,
        ReadOnlyMemory<byte>? authenticatedContext,
        CancellationToken cancellationToken, IZipEffectBoundaryGuard? guard = null)
    {
        var keyId =
            await _keyProvider.GetCurrentKeyIdAsync(
                cancellationToken);

        if (guard is not null) await guard.RevalidateAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(keyId))
            throw new CryptographicException("No current key.");

        var key =
            await _keyProvider.GetKeyAsync(
                keyId,
                cancellationToken);

        if (guard is not null) await guard.RevalidateAsync(cancellationToken);

        if (key is null ||
            key.KeyMaterial is not { Length: KeySize } ||
            !string.Equals(
                key.KeyId,
                keyId,
                StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Invalid encryption key.");
        }

        var authenticatedData =
            authenticatedContext.HasValue
                ? EncryptedEnvelopeFormat
                    .GetContextBoundAuthenticatedData(
                        EncryptedEnvelopeFormat
                            .Aes256GcmAlgorithm,
                        authenticatedContext.Value)
                : EncryptedEnvelopeFormat.GetAuthenticatedData(
                    EncryptedEnvelopeFormat.CurrentVersion,
                    EncryptedEnvelopeFormat.Aes256GcmAlgorithm);

        byte[] nonce = [], ciphertext = [], tag = [], dek = [];
        byte[]? wrappedDek = null;
        bool handedOff = false;
        try
        {
            if (guard is not null) await guard.RevalidateAsync(cancellationToken);
            using (AcceptZipEffect(guard,cancellationToken))
            {
                nonce = RandomNumberGenerator.GetBytes(NonceSize);
                ciphertext = new byte[plaintext.Length];
                tag = new byte[TagSize];
                dek = RandomNumberGenerator.GetBytes(KeySize);
            }
            using (AcceptZipEffect(guard,cancellationToken))
            {
                using (var aes = new AesGcm(dek, TagSize))
                {
                    aes.Encrypt(
                        nonce,
                        plaintext.Span,
                        ciphertext,
                        tag,
                        authenticatedData);
                }

                wrappedDek =
                    DevelopmentDataEncryptionKeyWrapper.Wrap(
                        key.KeyMaterial,
                        dek);
            }

            handedOff = true;
            return new EncryptedEnvelope
            {
                FormatVersion =
                    authenticatedContext.HasValue
                        ? EncryptedEnvelopeFormat.ContextBoundVersion
                        : EncryptedEnvelopeFormat.CurrentVersion,
                Ciphertext = ciphertext,
                Nonce = nonce,
                AuthenticationTag = tag,
                WrappedDataEncryptionKey = wrappedDek,
                KeyEncryptionKeyId = key.KeyId,
                Algorithm =
                    EncryptedEnvelopeFormat.Aes256GcmAlgorithm
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
            CryptographicOperations.ZeroMemory(authenticatedData);
            if (!handedOff)
            {
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(tag);
                if (wrappedDek is not null) CryptographicOperations.ZeroMemory(wrappedDek);
            }
        }
    }

    public Task<byte[]> DecryptAsync(
        EncryptedEnvelope envelope,
        CancellationToken cancellationToken = default) =>
        DecryptCoreAsync(envelope, null, cancellationToken);

    public Task<byte[]> DecryptWithContextAsync(
        EncryptedEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedContext,
        CancellationToken cancellationToken = default) =>
        DecryptCoreAsync(
            envelope,
            authenticatedContext,
            cancellationToken);

    public Task<byte[]> DecryptWithContextBoundedAsync(EncryptedEnvelope envelope,
        ReadOnlyMemory<byte> authenticatedContext, EnvelopeDecryptionLimits limits,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(limits);
        EnvelopeContentAuthentication.ValidateBound(envelope, limits.MaximumPlaintextBytes);
        if (authenticatedContext.Length > limits.MaximumAuthenticatedContextBytes || envelope.FormatVersion != 2)
            throw new CryptographicException("Bounded context-bound envelope required.");
        return DecryptCoreAsync(envelope, authenticatedContext, cancellationToken, limits);
    }

    private async Task<byte[]> DecryptCoreAsync(
        EncryptedEnvelope envelope,
        ReadOnlyMemory<byte>? authenticatedContext,
        CancellationToken cancellationToken, EnvelopeDecryptionLimits? limits = null, IZipEffectBoundaryGuard? guard = null)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        EncryptedEnvelopeFormat.Validate(envelope);


        var key =
            await _keyProvider.GetKeyAsync(
                envelope.KeyEncryptionKeyId,
                cancellationToken);

        if (guard is not null) await guard.RevalidateAsync(cancellationToken);

        if (key is null ||
            key.KeyMaterial is not { Length: KeySize } ||
            !string.Equals(
                key.KeyId,
                envelope.KeyEncryptionKeyId,
                StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Invalid encryption key.");
        }

        byte[] dek = [];
        try
        {
            using (AcceptZipEffect(guard,cancellationToken))
                dek = DevelopmentDataEncryptionKeyWrapper.Unwrap(
                    key.KeyMaterial,
                    envelope.WrappedDataEncryptionKey);
            if (limits is not null) BoundedDataKeyUnwrapped?.Invoke(dek);
            return limits is null
                ? EnvelopeContentAuthentication.Decrypt(envelope, dek, authenticatedContext)
                : EnvelopeContentAuthentication.DecryptBounded(envelope, dek, authenticatedContext!.Value, limits, cancellationToken, guard);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    private static IDisposable? AcceptZipEffect(IZipEffectBoundaryGuard? guard,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return guard is null ? null : guard.Accept(ct)
            ?? throw new UnauthorizedAccessException("ZIP authority returned no acceptance capability.");
    }

}

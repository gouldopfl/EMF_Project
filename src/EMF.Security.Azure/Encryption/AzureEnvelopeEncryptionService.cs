using EMF.Core.Contracts.Zip;
using System.Security.Cryptography;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Keys;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Azure.Encryption;

public sealed class AzureEnvelopeEncryptionService :
    IEnvelopeEncryptionService, IBoundedEnvelopeDecryptionService, IZipGuardedEnvelopeEncryptionService
{
    private readonly IAzureKeyReferenceProvider _keyProvider;
    private readonly IAzureKeyCryptographyFactory _cryptographyFactory;

    public AzureEnvelopeEncryptionService(
        IAzureKeyReferenceProvider keyProvider,
        IAzureKeyCryptographyFactory cryptographyFactory)
    {
        ArgumentNullException.ThrowIfNull(keyProvider);
        ArgumentNullException.ThrowIfNull(cryptographyFactory);

        _keyProvider = keyProvider;
        _cryptographyFactory = cryptographyFactory;
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
        cancellationToken.ThrowIfCancellationRequested();

        var keyReference =
            await AzureEnvelopeProviderBoundary.CallAsync(() => _keyProvider.GetCurrentKeyAsync(cancellationToken), EnvelopeKeyOperation.ResolveCurrentKey);

        if (guard is not null) await guard.RevalidateAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(keyReference.KeyVersion))
            throw new CryptographicException("Key version is required.");

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
        Task<byte[]>? wrapping = null;
        bool handedOff = false;
        try
        {
            if (guard is not null) await guard.RevalidateAsync(cancellationToken);
            using (AcceptZipEffect(guard,cancellationToken))
            {
                nonce = RandomNumberGenerator.GetBytes(12);
                ciphertext = new byte[plaintext.Length];
                tag = new byte[16];
                dek = RandomNumberGenerator.GetBytes(32);
            }
            using (AcceptZipEffect(guard,cancellationToken))
            {
                using var aes = new AesGcm(dek, 16);
                aes.Encrypt(
                    nonce,
                    plaintext.Span,
                    ciphertext,
                    tag,
                    authenticatedData);
            }
            if (guard is not null) await guard.RevalidateAsync(cancellationToken);
            var cryptography =
                await AzureEnvelopeProviderBoundary.CallAsync(() => Task.FromResult(_cryptographyFactory.Create(keyReference)), EnvelopeKeyOperation.UnwrapKey);

            if (guard is not null) await guard.RevalidateAsync(cancellationToken);

            if (cryptography is null)
                throw new CryptographicException(
                    "Encryption key cryptography factory returned no implementation.");

            using (AcceptZipEffect(guard,cancellationToken))
                wrapping = AzureEnvelopeProviderBoundary.CallAsync(() => cryptography.WrapKeyAsync(dek, cancellationToken), EnvelopeKeyOperation.WrapKey);
            wrappedDek = await wrapping;

            if (guard is not null) await guard.RevalidateAsync(cancellationToken);
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
                KeyEncryptionKeyId =
                    $"{keyReference.KeyName}/{keyReference.KeyVersion}",
                Algorithm =
                    EncryptedEnvelopeFormat.Aes256GcmAlgorithm
            };
        }
        finally
        {
            // Acceptance disposal may fail after the provider operation started.
            // Observe that already-accepted operation and clear any returned buffer.
            if(wrappedDek is null && wrapping is not null)
            {
                try { wrappedDek=await wrapping; } catch { /* Preserve the initiating failure. */ }
            }
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


        var parts = envelope.KeyEncryptionKeyId.Split('/', 2);

        if (parts.Length != 2)
            throw new CryptographicException("Invalid key identifier.");

        var keyReference = await AzureEnvelopeProviderBoundary.CallAsync(() => _keyProvider.GetKeyAsync(envelope.KeyEncryptionKeyId, cancellationToken), EnvelopeKeyOperation.ResolveHistoricalKey);

        if (guard is not null) await guard.RevalidateAsync(cancellationToken);

        if (keyReference is null)
            throw new CryptographicException("Encryption key not found.");

        if (!string.Equals(
                keyReference.KeyName,
                parts[0],
                StringComparison.Ordinal) ||
            !string.Equals(
                keyReference.KeyVersion,
                parts[1],
                StringComparison.Ordinal))
        {
            throw new CryptographicException(
                "Encryption key identity mismatch.");
        }

        var cryptography =
            await AzureEnvelopeProviderBoundary.CallAsync(() => Task.FromResult(_cryptographyFactory.Create(keyReference)), EnvelopeKeyOperation.UnwrapKey);

        if (guard is not null) await guard.RevalidateAsync(cancellationToken);

        if (cryptography is null)
            throw new CryptographicException(
                "Encryption key cryptography factory returned no implementation.");

        Task<byte[]>? unwrapping=null;
        byte[] dek=[];
        try
        {
            using (AcceptZipEffect(guard,cancellationToken))
                unwrapping=AzureEnvelopeProviderBoundary.CallAsync(() => cryptography.UnwrapKeyAsync(envelope.WrappedDataEncryptionKey,cancellationToken),EnvelopeKeyOperation.UnwrapKey);
            dek=await unwrapping;
            if (guard is not null) await guard.RevalidateAsync(cancellationToken);
            if (dek.Length != 32)
            {
                throw new CryptographicException(
                    "Invalid data encryption key length.");
            }

            return limits is null
                ? EnvelopeContentAuthentication.Decrypt(envelope, dek, authenticatedContext)
                : EnvelopeContentAuthentication.DecryptBounded(envelope, dek, authenticatedContext!.Value, limits, cancellationToken, guard);
        }
        finally
        {
            if(dek.Length==0 && unwrapping is not null)
            {
                try { dek=await unwrapping; } catch { /* Preserve the initiating failure. */ }
            }
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

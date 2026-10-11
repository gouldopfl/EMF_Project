using EMF.Core.Contracts.Zip;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
namespace EMF.Orchestration.Services;

internal static class ZipPhaseIIWork
{
    internal static async Task CheckAsync(IZipEffectBoundaryGuard? guard, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); if (guard is not null) await guard.RevalidateAsync(ct); ct.ThrowIfCancellationRequested(); }
    internal static IDisposable? Accept(IZipEffectBoundaryGuard? guard, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); return guard?.Accept(ct); }
    internal static string Hash(ReadOnlySpan<byte> bytes,IZipEffectBoundaryGuard? guard,CancellationToken ct)
    {using var accepted=Accept(guard,ct);return Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes));}
    internal static EncryptedEnvelope Decode(ReadOnlySpan<byte> bytes,EnvelopeDecryptionLimits limits,IZipEffectBoundaryGuard? guard,CancellationToken ct)
    {using var accepted=Accept(guard,ct);return BoundedEncryptedEnvelopeCodec.Read(bytes,limits,ct);}
    internal static byte[] Encode(EncryptedEnvelope envelope,EnvelopeDecryptionLimits limits,IZipEffectBoundaryGuard? guard,CancellationToken ct)
    {using var accepted=Accept(guard,ct);return BoundedEncryptedEnvelopeCodec.Write(envelope,limits,ct);}
    internal static IZipGuardedPrivateContentStorage Guarded(IZipPrivateContentStorage store)
        => store as IZipGuardedPrivateContentStorage ?? throw new NotSupportedException("Guarded ZIP storage required.");
    internal static Task<byte[]?> Candidate(IZipPrivateContentStorage store,ArtifactContentOperationId op,IZipEffectBoundaryGuard? guard,CancellationToken ct)
        => guard is null ? store.ReadCandidateAsync(op,ct) : Guarded(store).ReadCandidateGuardedAsync(op,guard,ct);
    internal static Task<IArtifactContentReadLease?> Read(IZipPrivateContentStorage store,ArtifactId id,ArtifactContentRevision revision,IZipEffectBoundaryGuard? guard,CancellationToken ct)
        => guard is null ? store.ReadProtectedAsync(id,revision,ct) : Guarded(store).ReadProtectedGuardedAsync(id,revision,guard,ct);
    internal static Task Stage(IZipPrivateContentStorage store,ArtifactContentOperationId op,ReadOnlyMemory<byte> bytes,IZipEffectBoundaryGuard? guard,CancellationToken ct)
        => guard is null ? store.StageAsync(op,bytes,ct) : Guarded(store).StageGuardedAsync(op,bytes,guard,ct);
    internal static Task<IPreparedArtifactContentMutation> Prepare(IZipPrivateContentStorage store,ArtifactId id,ReadOnlyMemory<byte> bytes,ArtifactContentMutationContext context,IZipEffectBoundaryGuard? guard,CancellationToken ct)
        => guard is null ? store.PrepareCreateAsync(id,bytes,context,ct) : Guarded(store).PrepareCreateGuardedAsync(id,bytes,context,guard,ct);
    internal static Task<EncryptedEnvelope> Encrypt(IEnvelopeEncryptionService crypto,ReadOnlyMemory<byte> plain,ReadOnlyMemory<byte> context,IZipEffectBoundaryGuard? guard,CancellationToken ct)
        => guard is null ? crypto.EncryptWithContextAsync(plain,context,ct) : (crypto as IZipGuardedEnvelopeEncryptionService
            ?? throw new NotSupportedException("Guarded ZIP encryption required.")).EncryptWithContextGuardedAsync(plain,context,guard,ct);
    internal static Task<byte[]> Decrypt(IBoundedEnvelopeDecryptionService crypto,EncryptedEnvelope envelope,ReadOnlyMemory<byte> context,EnvelopeDecryptionLimits limits,IZipEffectBoundaryGuard? guard,CancellationToken ct)
        => guard is null ? crypto.DecryptWithContextBoundedAsync(envelope,context,limits,ct) : (crypto as IZipGuardedEnvelopeEncryptionService
            ?? throw new NotSupportedException("Guarded ZIP decryption required.")).DecryptWithContextBoundedGuardedAsync(envelope,context,limits,guard,ct);
}

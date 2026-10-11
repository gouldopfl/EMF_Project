using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

// No stock registration or issuer exists. Callers supply a genuinely authenticated
// pre-ParentBound root; durable approval and source leases cannot manufacture it.
public sealed class ZipAuthorizedParentRetentionService(IZipParentAdmissionJournal admissions,
    ZipParentRetentionService retention,ZipDurableProfile profile,TimeProvider? timeProvider=null)
{
    private readonly TimeProvider clock=timeProvider??TimeProvider.System;
    public async Task<ZipParentAdmission> RetainAsync(IZipAuthorizedParentRetention grant,
        IZipGuardedBoundedArtifactContentStore sourceStore,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(grant);ArgumentNullException.ThrowIfNull(sourceStore);
        await using var attempt=new ZipParentRetentionAttempt(grant,clock,ct);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,attempt.Invalidated);ct=linked.Token;
        await ValidateAsync(attempt,ct);
        await using var guard=attempt.Boundary();
        using var permit=await ZipParentReadAdmission.ProcessWide.AcquireAsync(ct);
        await attempt.RevalidateAsync(ct);
        IArtifactContentReadLease? source=null;
        try
        {
            await guard.RevalidateAsync(ct);
            source=await sourceStore.ReadBoundedVersionedGuardedAsync(new(attempt.Current.Binding.SourceContentId),
                new(EMF.Security.Encryption.Envelope.BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Parent),ZipNumericLimits.Parent,new(attempt.Current.Binding.SourceRevision)),guard,ct)
                ??throw new InvalidDataException("Missing admitted source revision.");
            await guard.RevalidateAsync(ct);
            ZipRetainedBinding binding;
            using(var accepted=guard.Accept(ct))
            {
                if(source.ArtifactId.Value!=attempt.Current.Binding.SourceContentId || source.Revision.Value!=attempt.Current.Binding.SourceRevision ||
                    source.ReturnedLength!=source.Content.Length || source.ReturnedLength>ZipNumericLimits.Parent)
                    throw new InvalidDataException("Original source differs from admission.");
                binding=new(source.ArtifactId.Value,source.Revision.Value,Convert.ToHexString(SHA256.HashData(source.Content.Span)),source.ReturnedLength);
            }
            attempt.Source=binding;
            if(attempt.Current.State==ZipAdmissionState.Approved)
                attempt.Current=await admissions.AppendAdmissionEventAsync(attempt.Current,new(Guid.NewGuid().ToString("N"),
                    ZipAdmissionEventKind.SourceBound,grant.ExecutingActorId,clock.GetUtcNow(),Source:binding),guard,ct);
            else if(attempt.Current.Source!=binding)throw new InvalidDataException("Frozen source differs.");
            var parent=await retention.RetainAsync(attempt.Current.OperationId,attempt.Current.Binding.ParentArtifactId,
                profile.Fingerprint,source,attempt,ct);
            attempt.Current=await admissions.AppendAdmissionEventAsync(attempt.Current,new(Guid.NewGuid().ToString("N"),
                ZipAdmissionEventKind.ParentBound,grant.ExecutingActorId,clock.GetUtcNow(),Parent:parent),guard,ct);
        }
        finally{if(source is not null)await source.DisposeAsync();}
        await guard.FinishAsync(ct);
        return attempt.Current; // Evidence only; execution requires a new Phase I root.
    }
    public async Task<ZipParentAdmission> RecoverAsync(IZipAuthorizedParentRetention grant,
        ZipParentRetentionIdentity expected,CancellationToken ct=default)
    {
        ArgumentNullException.ThrowIfNull(expected);
        await using var attempt=new ZipParentRetentionAttempt(grant,clock,ct);
        using var linked=CancellationTokenSource.CreateLinkedTokenSource(ct,attempt.Invalidated);ct=linked.Token;
        await ValidateAsync(attempt,ct);
        if(attempt.Current.State!=ZipAdmissionState.SourceBound || attempt.Current.Source!=expected.Source ||
            expected.ParentOperationId!=attempt.Current.OperationId || expected.ParentArtifactId!=attempt.Current.Binding.ParentArtifactId ||
            expected.ProfileHash!=profile.Fingerprint || expected.NamespaceId!=profile.ParentNamespaceId)
            throw new UnauthorizedAccessException("Retention recovery differs from original admission.");
        attempt.Source=expected.Source;
        await using var guard=attempt.Boundary();
        var parent=await retention.RecoverCreationAsync(expected,attempt,ct);
        attempt.Current=await admissions.AppendAdmissionEventAsync(attempt.Current,new(Guid.NewGuid().ToString("N"),
            ZipAdmissionEventKind.ParentBound,grant.ExecutingActorId,clock.GetUtcNow(),Parent:parent),guard,ct);
        await guard.FinishAsync(ct);
        return attempt.Current;
    }
    private async Task ValidateAsync(ZipParentRetentionAttempt attempt,CancellationToken ct)
    {
        profile.ValidateRecoveryBinding(attempt.Current.Binding.ProfileJson,attempt.Current.Binding.ProfileHash);
        await attempt.RevalidateAsync(ct);
        var current=await admissions.ReadAdmissionAsync(attempt.Current.OperationId,ct);
        if(current!=attempt.Current)throw new ZipFenceException();
        var required=attempt.Current.State==ZipAdmissionState.Approved?ZipAuthorityCapabilities.Admit:ZipAuthorityCapabilities.Recover;
        if(attempt.Current.LatestAuthorityEvidence is null || (attempt.Current.LatestAuthorityEvidence.Capabilities&required)==0)
            throw new UnauthorizedAccessException("Retention approval lacks required durable eligibility.");
        await attempt.RevalidateAsync(ct);
    }
}

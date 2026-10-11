using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Models;
using EMF.Persistence.Repositories;

namespace EMF.Tests.TestInfrastructure;

internal sealed class ZipOwnershipFixture : IAsyncDisposable
{
    internal ArtifactIngestionFixture Evidence = null!;
    internal ZipDurableRuntimeFixture Runtime = null!;
    internal ZipAdmissionFixture.Clock Clock { get; } = new();
    internal ZipParentAdmission Admission = null!;
    internal ZipDurableProfile Profile = null!;
    internal SqliteZipExtractionJournal Journal => Runtime.Journal;
    internal static async Task<ZipOwnershipFixture> CreateAsync(
        ZipAuthorityCapabilities capabilities = ZipAuthorityCapabilities.Admit | ZipAuthorityCapabilities.Recover, bool two = false, byte[]? sourceBytes = null)
    {
        var f = new ZipOwnershipFixture { Evidence = await ArtifactIngestionFixture.CreateAsync() };
        try
        {
            f.Runtime = new(f.Evidence, f.Clock); await f.Journal.InitializeAsync();
            f.Profile = ZipDurableProfile.Create(f.Runtime.ParentStorage.NamespaceId, f.Runtime.ChildStorage.NamespaceId,
                new("clamd", "test-policy", new string('C', 64)), "synthetic-test-protection");
            var binding = new ZipAdmissionBinding("workflow", "workflow-operation", "zip-archives", "parent",
                "source-parent", "source-revision", "synthetic-original-actor", "synthetic-authorized-operation",
                f.Profile.CanonicalJson, f.Profile.Fingerprint, f.Profile.ParentNamespaceId, f.Profile.ChildNamespaceId);
            var p = await f.Journal.ResolveOrReserveAdmissionAsync(new("synthetic-test-issuer", "stable-request"), binding);
            var approval = new ZipAdmissionApproval(p.OperationId, ZipAdmissionValidation.BindingHash(binding), p.Key.IssuerId,
                "synthetic-decision", "authority-revision", "policy-v1", capabilities, f.Clock.Now, f.Clock.Now.AddHours(1));
            p = await f.Journal.AppendAdmissionEventAsync(p, new("approval", ZipAdmissionEventKind.Approved, binding.OriginalActorId, f.Clock.Now, approval));
            var bytes = sourceBytes ?? ZipDurableRuntimeFixture.Zip(two);
            var source = new ZipRetainedBinding(binding.SourceContentId, binding.SourceRevision, Convert.ToHexString(SHA256.HashData(bytes)), bytes.Length);
            p = await f.Journal.AppendAdmissionEventAsync(p, new("source", ZipAdmissionEventKind.SourceBound, binding.OriginalActorId, f.Clock.Now, Source: source));
            using var lease = new ArtifactContentReadLease(new(source.ContentId), new(source.Revision), source.Length, bytes);
            var parent = await f.Runtime.ParentRetention.RetainAsync(p.OperationId, binding.ParentArtifactId, f.Profile.Fingerprint, lease);
            f.Admission = await f.Journal.AppendAdmissionEventAsync(p, new("parent", ZipAdmissionEventKind.ParentBound, binding.OriginalActorId, f.Clock.Now, Parent: parent));
            return f;
        }
        catch { await f.DisposeAsync(); throw; }
    }
    internal async Task<ZipOwnershipClaimRequest> RequestAsync(ZipOwnershipPurpose purpose, string owner = "worker")
    {
        var parent = (await Journal.ReadAsync(Admission.OperationId))!;
        return new(purpose, Admission, parent.Fence, parent.OwnerUntil, owner, Profile.OwnershipLease);
    }
    public ValueTask DisposeAsync() => Evidence.DisposeAsync();
}

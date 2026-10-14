using System.Runtime.Versioning;
using EMF.Core.Contracts.Ingestion;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Persistence.Repositories;
using EMF.Persistence.Storage;

namespace EMF.Tests.TestInfrastructure;

[SupportedOSPlatform("linux")]
internal sealed class ZipCandidateCustodyFoundationFixture:IDisposable
{
    internal string Root {get;}=Path.Combine(Path.GetTempPath(),"emf-d1-"+Guid.NewGuid().ToString("N"));
    internal byte[] Bytes {get;}=Enumerable.Range(1,257).Select(x=>(byte)x).ToArray();
    internal Guard Authority {get;}=new();
    internal LinuxZipCandidateNamespace Native {get;private set;}=null!;
    internal CandidateCleanupIntent Intent {get;private set;}=null!;
    internal string Active=>Path.Combine(Root,"zip-candidates",ZipCandidateCustodyEncoding.ActiveLeaf("create-op"));
    internal string Custody=>Path.Combine(Root,Intent.CustodyRelativePath);
    internal static async Task<ZipCandidateCustodyFoundationFixture> Create(IZipCandidateNamespaceSyscalls? calls=null)
    {
        var f=new ZipCandidateCustodyFoundationFixture();Directory.CreateDirectory(f.Root,UnixFileMode.UserRead|UnixFileMode.UserWrite|UnixFileMode.UserExecute);
        var staging=new FileSystemArtifactContentStagingStore(Path.Combine(f.Root,"zip-candidates"),4096);await staging.StageAsync(new("create-op"),f.Bytes);
        f.Native=new(f.Root,4096,calls);var identity=Identity(ZipCandidateCustodyEncoding.Namespace(f.Root));
        // Inspect with real syscalls first so failure injection targets movement.
        var inspection=await new LinuxZipCandidateNamespace(f.Root,4096).InspectActiveAsync(identity,f.Authority);
        f.Intent=IntentFor(identity,inspection);
        f.Intent=ZipCandidateCustodyEncoding.Seal(f.Intent with{InitiatingHistory=f.Intent.InitiatingHistory with{BootId=File.ReadAllText("/proc/sys/kernel/random/boot_id").Trim()}});return f;
    }
    internal static ZipRetentionIdentity Identity(string ns)=>new("parent-op",0,"zip-quarantine-child","create-op","release-op","owner-token",ns,new string('A',64),10);
    internal static CandidateCleanupIntent IntentFor(ZipRetentionIdentity identity,CandidateFileInspection inspection)
    {
        var at=DateTimeOffset.Parse("2026-10-13T00:00:00Z");var planHash=new string('C',64);
        var parent=new ZipParentBinding(identity.ParentOperationId,"parent-artifact",new("parent-content","parent-rev",new string('D',64),100),new string('B',64));
        var entry=new ZipEntryPlan(0,0,"child.txt","child.txt",5,10,42,false,false,"child-op","provisional");
        var input=new ZipRetainedBinding(identity.ObjectId,"created-rev",identity.PlaintextSha256,identity.Length);
        var ingestion=new ZipChildIngestionProof("child-op","provisional","provisional",ArtifactIngestionDisposition.ProvisionalArtifactAdopted,identity.PlaintextSha256,"classification","classification-rev",2);
        var fence=new ZipFence(identity.ParentOperationId,"worker",1,5,planHash,0);
        var ack=new ZipAcknowledgementBinding(identity.ParentOperationId,0,planHash,"child-op","provisional",identity.PlaintextSha256,new(0,"provisional",parent.Input,input,ingestion,fence,fence with{Revision=6},at));
        var create=new ArtifactContentMutationReceipt(new(identity.CreateOperationId),new ArtifactId(identity.ObjectId),ArtifactContentMutationKind.Create,null,new("created-rev"),new(identity.OwnerToken),ArtifactContentMutationOutcome.Created,at,null);
        var release=new ArtifactContentMutationReceipt(new(identity.ReleaseOperationId),new ArtifactId(identity.ObjectId),ArtifactContentMutationKind.Delete,new("created-rev"),new("deleted-rev"),new(identity.OwnerToken),ArtifactContentMutationOutcome.Deleted,at,null);
        var anchors=new ZipCandidateCleanupAnchors(parent,planHash,entry,identity,input,ingestion,ack,1,2,new string('E',64),new string('F',64),create,release);
        var history=new ZipCandidateCleanupHistory("actor",ZipOwnershipPurpose.InitialExecution,"attempt","invocation",fence,at.AddMinutes(10),at.AddMinutes(5),new string('B',64),"00000000-0000-0000-0000-000000000001",at);
        return ZipCandidateCustodyEncoding.Seal(new(1,ZipCandidateCustodyEncoding.Operation(anchors,inspection.Hash),anchors,inspection.Hash,inspection.File.Length,inspection.Root,inspection.SourceDirectory,inspection.File,ZipCandidateCustodyEncoding.RelativePath(anchors,inspection.Hash),1,2,history,""));
    }
    internal static CandidateCustodyCompletion Completion(CandidateCleanupIntent i,ZipCandidateFileIdentity? file=null)=>ZipCandidateCustodyEncoding.Seal(new(1,i.CleanupOperationId,i.IntentDigest,i.Anchors.Retention.NamespaceId,i.Anchors.Retention.ParentOperationId,i.Anchors.Retention.FileOrdinal,i.CandidateHash,i.StoredEncryptedLength,i.CustodyRelativePath,file??i.SourceIdentity,i.SourceDirectoryIdentity,true,true,true,true,ZipCandidateCustodyObservation.RecoveryVerifiedRetainedObject,ZipCandidateCustodyEncoding.ReceiptDigest(i.Anchors.ReleaseReceipt),2,3,i.InitiatingHistory with{Purpose=ZipOwnershipPurpose.Recovery,AttemptId="new-attempt"},null,null,""),i);
    public void Dispose(){Authority.Dispose();if(Directory.Exists(Root))Directory.Delete(Root,true);}
    internal sealed class Guard:IZipEffectBoundaryGuard,IDisposable
    {
        private readonly CancellationTokenSource signal=new();internal bool ThrowOnRelease,MissingAcceptance;
        public CancellationToken Invalidated=>signal.Token;internal void Revoke()=>signal.Cancel();
        public ValueTask RevalidateAsync(CancellationToken ct=default){ct.ThrowIfCancellationRequested();Invalidated.ThrowIfCancellationRequested();return ValueTask.CompletedTask;}
        public IDisposable Accept(CancellationToken ct=default){ct.ThrowIfCancellationRequested();Invalidated.ThrowIfCancellationRequested();return MissingAcceptance?null!:new Acceptance(this);}
        private sealed class Acceptance(Guard guard):IDisposable{public void Dispose(){if(guard.ThrowOnRelease)throw new IOException("Injected release failure");}}
        public void Dispose()=>signal.Dispose();
    }
}

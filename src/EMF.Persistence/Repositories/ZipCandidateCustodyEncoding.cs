using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EMF.Core.Contracts.Zip;
using EMF.Core.Contracts.Storage;

namespace EMF.Persistence.Repositories;

// v1 canonical grammar: null=0; present object=1; string=2+U32BE length+strict
// UTF8; signed integer=3+I64BE; unsigned=4+U64BE; bool=5+0/1. Fields below are
// fixed in order; future fields require another protocol rather than changing v1.
internal static class ZipCandidateCustodyEncoding
{
    private static readonly UTF8Encoding Utf8=new(false,true);
    private static readonly JsonSerializerOptions JsonOptions=new()
    { UnmappedMemberHandling=JsonUnmappedMemberHandling.Disallow,RespectRequiredConstructorParameters=true };
    internal static string Json<T>(T value)=>JsonSerializer.Serialize(value,JsonOptions);
    internal static T Decode<T>(string json)
    {
        using var document=JsonDocument.Parse(json);Unique(document.RootElement);
        return JsonSerializer.Deserialize<T>(json,JsonOptions)??throw new InvalidDataException("Missing D1 evidence.");
    }
    private static void Unique(JsonElement value)
    {
        if(value.ValueKind==JsonValueKind.Object)
        {
            var names=new HashSet<string>(StringComparer.Ordinal);
            foreach(var property in value.EnumerateObject())
            {if(!names.Add(property.Name))throw new InvalidDataException("Duplicate D1 field.");Unique(property.Value);}
        }
        else if(value.ValueKind==JsonValueKind.Array)foreach(var item in value.EnumerateArray())Unique(item);
    }
    internal static string Hash(ReadOnlySpan<byte> bytes)=>Convert.ToHexString(SHA256.HashData(bytes));
    internal static void Digest(string value)
    {if(value is null||value.Length!=64||value.Any(c=>!char.IsAsciiHexDigit(c)||char.IsAsciiLetterLower(c)))throw new InvalidDataException("Noncanonical D1 digest.");}
    internal static string Namespace(string root)=>Hash(Utf8.GetBytes(Path.GetFullPath(root)));
    internal static string ActiveLeaf(string create)=>Hash(Encoding.ASCII.GetBytes(ArtifactContentIdentity.Validate(create)))+".candidate";
    internal static string Key(ZipCandidateCleanupAnchors a,string candidate)
    {
        Digest(a.Retention.NamespaceId);Digest(a.PlanHash);Digest(candidate);
        using var stream=new MemoryStream();
        void LP(string s){var b=Utf8.GetBytes(s);Span<byte> n=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(n,(uint)b.Length);stream.Write(n);stream.Write(b);}
        void Ordinal(int n){if(n<0)throw new InvalidDataException("D1 ordinal.");Span<byte> b=stackalloc byte[4];BinaryPrimitives.WriteInt32BigEndian(b,n);stream.Write(b);}
        LP("EMF-ZIP-D1-CUSTODY-KEY-V1");LP(a.Retention.NamespaceId);LP(a.Parent.OperationId);LP(a.PlanHash);
        Ordinal(a.Occurrence.CentralOrdinal);Ordinal(a.Retention.FileOrdinal);
        foreach(var id in new[]{a.Retention.ObjectId,a.Retention.CreateOperationId,a.Retention.ReleaseOperationId,a.Retention.OwnerToken})LP(ArtifactContentIdentity.Validate(id));
        LP(candidate);return Hash(stream.ToArray());
    }
    internal static string Operation(ZipCandidateCleanupAnchors a,string hash)=>"zip-custody-"+Key(a,hash);
    internal static string RelativePath(ZipCandidateCleanupAnchors a,string hash)=>
        "zip-candidate-custody/v1/"+ActiveLeaf(a.Retention.CreateOperationId)[..^10]+"/"+Key(a,hash)+".custody";
    internal static CandidateCleanupIntent Seal(CandidateCleanupIntent i)
    {Validate(i,false);return i with{IntentDigest=Hash(IntentBytes(i))};}
    internal static CandidateCustodyCompletion Seal(CandidateCustodyCompletion c,CandidateCleanupIntent i)
    {Validate(c,i,false);return c with{CompletionDigest=Hash(CompletionBytes(c))};}
    internal static void Validate(CandidateCleanupIntent i,bool checkDigest=true)
    {
        if(i is null||i.Anchors is null||i.Anchors.Retention is null||i.Anchors.Parent is null||i.Anchors.Parent.Input is null||i.Anchors.Occurrence is null||i.Anchors.ChildInput is null||i.Anchors.Ingestion is null||i.Anchors.Acknowledgement is null||i.Anchors.CreateReceipt is null||i.Anchors.ReleaseReceipt is null||i.RootIdentity is null||i.SourceDirectoryIdentity is null||i.SourceIdentity is null||i.InitiatingHistory is null)
            throw new InvalidDataException("Missing D1 intent fields.");
        var a=i.Anchors;var r=a.Retention;
        DirectoryIdentity(i.RootIdentity); DirectoryIdentity(i.SourceDirectoryIdentity);
        if(i.ProtocolVersion!=1||i.CleanupOperationId!=Operation(a,i.CandidateHash)||i.CustodyRelativePath!=RelativePath(a,i.CandidateHash)||
           a.Parent.OperationId!=r.ParentOperationId||a.Occurrence.FileOrdinal!=r.FileOrdinal||a.Occurrence.IsDirectory||
           a.Acknowledgement.ParentOperationId!=r.ParentOperationId||a.Acknowledgement.FileOrdinal!=r.FileOrdinal||a.Acknowledgement.PlanHash!=a.PlanHash||
           !Enum.IsDefined(a.Ingestion.Disposition)||(a.Ingestion.Disposition!=EMF.Core.Contracts.Ingestion.ArtifactIngestionDisposition.ProvisionalArtifactAdopted&&a.Ingestion.Disposition!=EMF.Core.Contracts.Ingestion.ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact)||
           a.Ingestion.Disposition==EMF.Core.Contracts.Ingestion.ArtifactIngestionDisposition.ProvisionalArtifactAdopted&&a.Ingestion.CanonicalArtifactId!=a.Ingestion.ProvisionalArtifactId||
           a.Occurrence.ExpandedLength!=r.Length||a.Occurrence.CompressedLength<0||r.Length<0||
           a.Ingestion.ProvisionalArtifactId!=a.Occurrence.ProvisionalArtifactId||
           a.Ingestion.ChildOperationId!=a.Occurrence.ChildOperationId||a.Acknowledgement.ChildOperationId!=a.Ingestion.ChildOperationId||
           a.Acknowledgement.CanonicalArtifactId!=a.Ingestion.CanonicalArtifactId||a.Acknowledgement.ChildSha256!=r.PlaintextSha256||
           a.ChildInput.ContentId!=r.ObjectId||a.ChildInput.Revision!=a.CreateReceipt.CurrentRevision?.Value||
           a.ChildInput.Sha256!=r.PlaintextSha256||a.ChildInput.Length!=r.Length||a.Ingestion.Sha256!=r.PlaintextSha256||
           i.StoredEncryptedLength<=0||i.StoredEncryptedLength>Array.MaxLength||i.SourceIdentity.Length!=i.StoredEncryptedLength||i.SourceIdentity.LinkCount!=1||
           i.SourceIdentity.Inode==0||i.SourceIdentity.MountId==0||i.SourceIdentity.Mode!=0x8180||i.SourceIdentity.ModifiedNanoseconds>=1000000000||i.SourceIdentity.ChangedNanoseconds>=1000000000||
           i.BaseRetentionRevision<1||i.CommittedRetentionRevision!=checked(i.BaseRetentionRevision+1)||a.ContainsId<=0||a.DerivedFromId<=0)
            throw new InvalidDataException("Invalid D1 intent binding.");
        var details=a.Acknowledgement.Details;
        if(details is null||details.CentralOrdinal!=a.Occurrence.CentralOrdinal||details.ProvisionalArtifactId!=a.Occurrence.ProvisionalArtifactId||details.ParentInput!=a.Parent.Input||details.ChildInput!=a.ChildInput||details.Ingestion!=a.Ingestion||details.AdmittedFence is null||details.CommittedFence is null||details.AdmittedFence.OperationId!=r.ParentOperationId||details.CommittedFence.OperationId!=r.ParentOperationId||details.AdmittedFence.PlanHash!=a.PlanHash||details.CommittedFence.PlanHash!=a.PlanHash)
            throw new InvalidDataException("D1 ACK details changed.");
        Digest(r.PlaintextSha256);Digest(a.AdoptionDigest);Digest(a.AuditFactsDigest);History(i.InitiatingHistory,r.ParentOperationId);
        Receipt(a.CreateReceipt,r,false);Receipt(a.ReleaseReceipt,r,true);
        if(a.ReleaseReceipt.PriorRevision!=a.CreateReceipt.CurrentRevision)throw new InvalidDataException("D1 release revision.");
        if(checkDigest){Digest(i.IntentDigest);if(i.IntentDigest!=Hash(IntentBytes(i)))throw new InvalidDataException("D1 intent digest changed.");}
    }
    private static void Receipt(ArtifactContentMutationReceipt c,ZipRetentionIdentity r,bool release)
    {
        if(c.OperationId.Value!=(release?r.ReleaseOperationId:r.CreateOperationId)||c.ArtifactId.Value!=r.ObjectId||c.OwnershipToken?.Value!=r.OwnerToken||
           c.Kind!=(release?ArtifactContentMutationKind.Delete:ArtifactContentMutationKind.Create)||c.Outcome!=(release?ArtifactContentMutationOutcome.Deleted:ArtifactContentMutationOutcome.Created)||
           c.CurrentRevision is null||c.AuditEventId is not null||c.AuditObligationVersion!=1||
           (!release&&c.PriorRevision is not null)||(release&&(c.PriorRevision is null||c.PriorRevision==c.CurrentRevision)))throw new InvalidDataException("D1 receipt binding.");
    }
    private static void History(ZipCandidateCleanupHistory h,string parent)
    {
        if(h is null||h.Fence is null||!Enum.IsDefined(h.Purpose)||string.IsNullOrWhiteSpace(h.Actor)||h.Fence.OperationId!=parent||h.Fence.Epoch<1||h.Fence.Revision<1)
            throw new InvalidDataException("D1 historical provenance.");
        if(!Guid.TryParseExact(h.BootId,"D",out _))throw new InvalidDataException("D1 boot provenance is missing.");
        ArtifactContentIdentity.Validate(h.AttemptId);ArtifactContentIdentity.Validate(h.InvocationId);Digest(h.AdmissionDigest);
    }
    internal static bool Conserved(ZipCandidateFileIdentity a,ZipCandidateFileIdentity b)=>
        a.DeviceMajor==b.DeviceMajor&&a.DeviceMinor==b.DeviceMinor&&a.Inode==b.Inode&&a.LinkCount==b.LinkCount&&a.UserId==b.UserId&&a.GroupId==b.GroupId&&a.Mode==b.Mode&&a.Length==b.Length&&a.ModifiedSeconds==b.ModifiedSeconds&&a.ModifiedNanoseconds==b.ModifiedNanoseconds;
    internal static void Validate(CandidateCustodyCompletion c,CandidateCleanupIntent i,bool checkDigest=true)
    {
        Validate(i);
        if(c is null||c.CustodyIdentity is null||c.CustodyDirectoryIdentity is null||c.VerifyingHistory is null)throw new InvalidDataException("Missing D1 completion fields.");
        DirectoryIdentity(c.CustodyDirectoryIdentity);
        if(c.CustodyIdentity.Inode==0||c.CustodyIdentity.MountId==0||c.CustodyIdentity.ModifiedNanoseconds>=1000000000||c.CustodyIdentity.ChangedNanoseconds>=1000000000)
            throw new InvalidDataException("Invalid D1 custody file identity.");
        var r=i.Anchors.Retention;
        if(c.ProtocolVersion!=1||c.CleanupOperationId!=i.CleanupOperationId||c.IntentDigest!=i.IntentDigest||c.NamespaceId!=r.NamespaceId||
           c.ParentOperationId!=r.ParentOperationId||c.FileOrdinal!=r.FileOrdinal||c.CandidateHash!=i.CandidateHash||c.StoredEncryptedLength!=i.StoredEncryptedLength||
           c.CustodyRelativePath!=i.CustodyRelativePath||!Conserved(i.SourceIdentity,c.CustodyIdentity)||
           !c.ActiveSourceAbsent||!c.CustodyObjectVerified||!c.SourceDirectoryFlushed||!c.CustodyDirectoryFlushed||!Enum.IsDefined(c.Observation)||
           c.ReleaseReceiptDigest!=ReceiptDigest(i.Anchors.ReleaseReceipt)||c.BaseRetentionRevision<i.CommittedRetentionRevision||c.CommittedRetentionRevision!=checked(c.BaseRetentionRevision+1)||
           c.Observation==ZipCandidateCustodyObservation.RecoveryVerifiedRetainedObject&&(c.EffectActor is not null||c.EffectAttemptId is not null))
            throw new InvalidDataException("Invalid D1 custody completion.");
        if(c.Observation==ZipCandidateCustodyObservation.MoveObservedInThisAttempt&&(c.EffectActor!=c.VerifyingHistory.Actor||c.EffectAttemptId!=c.VerifyingHistory.AttemptId))throw new InvalidDataException("D1 effect attribution is not observed provenance.");
        History(c.VerifyingHistory,r.ParentOperationId);
        if(checkDigest){Digest(c.CompletionDigest);if(c.CompletionDigest!=Hash(CompletionBytes(c)))throw new InvalidDataException("D1 completion digest changed.");}
    }
    private sealed class Writer:IDisposable
    {
        private readonly MemoryStream stream=new();
        internal Writer(string domain){Text(domain);}
        internal void Text(string? s){if(s is null){stream.WriteByte(0);return;}stream.WriteByte(2);var b=Utf8.GetBytes(s);Span<byte> n=stackalloc byte[4];BinaryPrimitives.WriteUInt32BigEndian(n,(uint)b.Length);stream.Write(n);stream.Write(b);}
        internal void N(long n){stream.WriteByte(3);Span<byte> b=stackalloc byte[8];BinaryPrimitives.WriteInt64BigEndian(b,n);stream.Write(b);}
        internal void U(ulong n){stream.WriteByte(4);Span<byte> b=stackalloc byte[8];BinaryPrimitives.WriteUInt64BigEndian(b,n);stream.Write(b);}
        internal void Bool(bool b){stream.WriteByte(5);stream.WriteByte(b?(byte)1:(byte)0);}
        internal void Object(bool present){stream.WriteByte(present?(byte)1:(byte)0);}
        internal void Time(DateTimeOffset t)=>N(t.UtcTicks);
        internal byte[] Bytes()=>stream.ToArray();public void Dispose()=>stream.Dispose();
    }
    private static void Directory(Writer w,ZipCandidateDirectoryIdentity d){w.U(d.DeviceMajor);w.U(d.DeviceMinor);w.U(d.Inode);w.U(d.MountId);}
    private static void File(Writer w,ZipCandidateFileIdentity f){w.U(f.DeviceMajor);w.U(f.DeviceMinor);w.U(f.Inode);w.U(f.MountId);w.U(f.LinkCount);w.U(f.UserId);w.U(f.GroupId);w.U(f.Mode);w.N(f.Length);w.N(f.ModifiedSeconds);w.U(f.ModifiedNanoseconds);w.N(f.ChangedSeconds);w.U(f.ChangedNanoseconds);}
    private static void Fence(Writer w,ZipFence f){w.Text(f.OperationId);w.Text(f.Owner);w.N(f.Epoch);w.N(f.Revision);w.Text(f.PlanHash);w.N(f.ConfirmedOrdinal);}
    private static void Retained(Writer w,ZipRetainedBinding r){w.Text(r.ContentId);w.Text(r.Revision);w.Text(r.Sha256);w.N(r.Length);}
    private static void Ingestion(Writer w,ZipChildIngestionProof p){w.Text(p.ChildOperationId);w.Text(p.ProvisionalArtifactId);w.Text(p.CanonicalArtifactId);w.N((int)p.Disposition);w.Text(p.Sha256);w.Text(p.ClassificationId);w.Text(p.ClassificationRevision);w.N(p.IngestionRevision);}
    private static void DirectoryIdentity(ZipCandidateDirectoryIdentity d)
    { if(d.Inode==0||d.MountId==0)throw new InvalidDataException("Invalid D1 directory identity."); }
    private static void Receipt(Writer w,ArtifactContentMutationReceipt r){w.Text(r.OperationId.Value);w.Text(r.ArtifactId.Value);w.N((int)r.Kind);w.Text(r.PriorRevision?.Value);w.Text(r.CurrentRevision?.Value);w.Text(r.OwnershipToken?.Value);w.N((int)r.Outcome);w.Time(r.OccurredUtc);w.Text(r.AuditEventId?.Value);w.N(r.AuditObligationVersion);}
    internal static string ReceiptDigest(ArtifactContentMutationReceipt r){using var w=new Writer("EMF-ZIP-D1-RECEIPT-V1");Receipt(w,r);return Hash(w.Bytes());}
    private static void History(Writer w,ZipCandidateCleanupHistory h){w.Text(h.Actor);w.N((int)h.Purpose);w.Text(h.AttemptId);w.Text(h.InvocationId);Fence(w,h.Fence);w.Time(h.OwnerUntil);w.Time(h.GrantValidUntil);w.Text(h.AdmissionDigest);w.Text(h.BootId);w.Time(h.RecordedUtc);}
    internal static byte[] IntentBytes(CandidateCleanupIntent i)
    {
        using var w=new Writer("EMF-ZIP-D1-INTENT-V1");w.N(i.ProtocolVersion);w.Text(i.CleanupOperationId);
        var a=i.Anchors;var r=a.Retention;w.Text(a.Parent.OperationId);w.Text(a.Parent.ParentArtifactId);Retained(w,a.Parent.Input);w.Text(a.Parent.ProfileHash);w.Text(a.PlanHash);
        var e=a.Occurrence;w.N(e.CentralOrdinal);w.Object(e.FileOrdinal is not null);if(e.FileOrdinal is {} ordinal)w.N(ordinal);w.Text(e.FullName);w.Text(e.DisplayName);w.N(e.CompressedLength);w.N(e.ExpandedLength);w.U(e.Crc32);w.Bool(e.IsEncrypted);w.Bool(e.IsDirectory);w.Text(e.ChildOperationId);w.Text(e.ProvisionalArtifactId);
        w.Text(r.ParentOperationId);w.N(r.FileOrdinal);w.Text(r.ObjectId);w.Text(r.CreateOperationId);w.Text(r.ReleaseOperationId);w.Text(r.OwnerToken);w.Text(r.NamespaceId);w.Text(r.PlaintextSha256);w.N(r.Length);Retained(w,a.ChildInput);Ingestion(w,a.Ingestion);
        var ack=a.Acknowledgement;w.Text(ack.ParentOperationId);w.N(ack.FileOrdinal);w.Text(ack.PlanHash);w.Text(ack.ChildOperationId);w.Text(ack.CanonicalArtifactId);w.Text(ack.ChildSha256);w.Object(ack.Details is not null);
        if(ack.Details is {} d){w.N(d.CentralOrdinal);w.Text(d.ProvisionalArtifactId);Retained(w,d.ParentInput);Retained(w,d.ChildInput);Ingestion(w,d.Ingestion);Fence(w,d.AdmittedFence);Fence(w,d.CommittedFence);w.Time(d.CreatedUtc);}
        w.N(a.ContainsId);w.N(a.DerivedFromId);w.Text(a.AdoptionDigest);w.Text(a.AuditFactsDigest);Receipt(w,a.CreateReceipt);Receipt(w,a.ReleaseReceipt);
        w.Text(i.CandidateHash);w.N(i.StoredEncryptedLength);Directory(w,i.RootIdentity);Directory(w,i.SourceDirectoryIdentity);File(w,i.SourceIdentity);w.Text(i.CustodyRelativePath);w.N(i.BaseRetentionRevision);w.N(i.CommittedRetentionRevision);History(w,i.InitiatingHistory);return w.Bytes();
    }
    internal static byte[] CompletionBytes(CandidateCustodyCompletion c)
    {
        using var w=new Writer("EMF-ZIP-D1-COMPLETION-V1");w.N(c.ProtocolVersion);w.Text(c.CleanupOperationId);w.Text(c.IntentDigest);w.Text(c.NamespaceId);w.Text(c.ParentOperationId);w.N(c.FileOrdinal);w.Text(c.CandidateHash);w.N(c.StoredEncryptedLength);w.Text(c.CustodyRelativePath);File(w,c.CustodyIdentity);Directory(w,c.CustodyDirectoryIdentity);w.Bool(c.ActiveSourceAbsent);w.Bool(c.CustodyObjectVerified);w.Bool(c.SourceDirectoryFlushed);w.Bool(c.CustodyDirectoryFlushed);w.N((int)c.Observation);w.Text(c.ReleaseReceiptDigest);w.N(c.BaseRetentionRevision);w.N(c.CommittedRetentionRevision);History(w,c.VerifyingHistory);w.Text(c.EffectActor);w.Text(c.EffectAttemptId);return w.Bytes();
    }
}

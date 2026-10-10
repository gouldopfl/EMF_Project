using System.IO.Compression;
using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
namespace EMF.Orchestration.Services;

public sealed class ZipSequentialExtractor(IZipExtractionJournal parents,IZipExtractionExecutionJournal work,
    IZipRetentionJournal retentions,ZipProtectedRetentionService retention)
{
    internal Func<string,Task>? Checkpoint { get; set; }
    internal Action<byte[]>? Allocated { get; set; }
    private Task At(string name)=>Checkpoint?.Invoke(name)??Task.CompletedTask;
    public async Task<ZipParentSnapshot> MaterializeNextAsync(ZipParentSnapshot parent,IArtifactContentReadLease input,CancellationToken ct=default)
    {
        await ZipChildWorkGate.Gate.WaitAsync(ct);
        try
        {
            using var budget=CancellationTokenSource.CreateLinkedTokenSource(ct);budget.CancelAfter(ZipNumericLimits.AttemptTimeout);ct=budget.Token;
            var current=await parents.ReadAsync(parent.Binding.OperationId,ct);if(current?.Fence!=parent.Fence)throw new ZipFenceException();parent=current;
            if(parent.State is not (ZipParentState.Planned or ZipParentState.Processing))throw new InvalidOperationException("ZIP extraction is not eligible.");
            var ordinal=checked(parent.Fence.ConfirmedOrdinal+1);
            var planned=parent.Entries.SingleOrDefault(e=>e.Plan.FileOrdinal==ordinal);if(planned is null)return parent;
            byte[]? plaintext=null;
            try
            {
                var p=planned.Plan;
                if(p.IsEncrypted)throw new InvalidDataException("Encrypted ZIP child requires review.");
                if(p.IsDirectory||p.ExpandedLength<0||p.ExpandedLength>ZipNumericLimits.Child)throw new InvalidDataException("ZIP child exceeds expanded ceiling.");
                var existing=await retentions.ReadRetentionAsync(parent.Binding.OperationId,ordinal,ct);
                if(existing is not null&&existing.State!=ZipRetentionState.Reserved)
                {
                    var recovered=await retention.SealAsync(parent,ordinal,null,ct);parent=recovered.Parent;
                    return await work.MaterializedAsync(parent.Fence,ordinal,recovered.Retention,ct);
                }
                if(planned.State!=ZipEntryState.Planned)throw new InvalidDataException("ZIP progress has no matching private retention.");
                if(input.ArtifactId.Value!=parent.Binding.Input.ContentId||input.Revision.Value!=parent.Binding.Input.Revision||
                    input.ReturnedLength!=parent.Binding.Input.Length||input.Content.Length!=input.ReturnedLength||
                    !Convert.ToHexString(SHA256.HashData(input.Content.Span)).Equals(parent.Binding.Input.Sha256,StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("ZIP retained parent changed.");
                using var raw=input.OpenReadStream();
                if(raw.GetType()!=typeof(MemoryStream))throw new InvalidDataException("ZIP extraction requires the certified same-array MemoryStream.");
                var reservation=new ZipWorkReservation(Guid.NewGuid().ToString("N"),ZipWorkKind.Extraction,ordinal,p.ExpandedLength,p.CompressedLength);
                parent=await parents.ReserveAsync(parent.Fence,reservation,ct);await At("Reserved");
                async Task Charge(long expanded,long reads)
                {
                    parent=await work.ChargeAsync(parent.Fence,new(Guid.NewGuid().ToString("N"),reservation.Id,ordinal,expanded,reads,expanded),ct);
                    await At(expanded>0?"PayloadCharged":"ParentReadCharged");
                }
                using(var metered=new ChargedStream(raw,n=>Charge(0,n).GetAwaiter().GetResult(),ct))
                using(var archive=new ZipArchive(metered,ZipArchiveMode.Read,leaveOpen:true))
                {
                    if(archive.Entries.Count!=parent.Plan!.Entries.Count)throw new InvalidDataException("ZIP runtime occurrence count changed.");
                    var entry=archive.Entries[p.CentralOrdinal];
                    if(entry.FullName!=p.FullName||entry.Length!=p.ExpandedLength||entry.CompressedLength!=p.CompressedLength||entry.Crc32!=p.Crc32||entry.IsEncrypted!=p.IsEncrypted)
                        throw new InvalidDataException("ZIP runtime occurrence changed from admitted plan.");
                    ct.ThrowIfCancellationRequested();plaintext=new byte[checked((int)p.ExpandedLength)];Allocated?.Invoke(plaintext);await At("Allocated");
                    using var child=entry.Open();await At("Opened");var crc=new ZipIncrementalCrc32();
                    for(var offset=0;offset<plaintext.Length;)
                    {
                        var count=Math.Min(65_536,plaintext.Length-offset);
                        await Charge(count,0);ct.ThrowIfCancellationRequested();
                        await child.ReadExactlyAsync(plaintext.AsMemory(offset,count),ct);await At("Decompressed");
                        crc.Append(plaintext.AsSpan(offset,count));offset=checked(offset+count);ct.ThrowIfCancellationRequested();
                    }
                    // The separate one-byte allowance was charged with the durable attempt reservation.
                    await At("ProbeStarting");ct.ThrowIfCancellationRequested();
                    var probe=new byte[1];
                    try{if(child.Read(probe,0,1)!=0)throw new InvalidDataException("ZIP child expands beyond its admitted length.");}
                    finally{CryptographicOperations.ZeroMemory(probe);}
                    if(crc.Value!=p.Crc32)throw new InvalidDataException("ZIP child CRC32 mismatch.");await At("CrcVerified");
                }
                ct.ThrowIfCancellationRequested();var hash=Convert.ToHexString(SHA256.HashData(plaintext));ct.ThrowIfCancellationRequested();
                parent=await work.VerifyChildAsync(parent.Fence,new(reservation.Id,ordinal,plaintext.LongLength,p.Crc32,hash,parent.Fence.PlanHash!),ct);
                await At("IntegrityPersisted");
                // Archive and child streams are closed before encryption/private promotion.
                var sealedChild=await retention.SealAsync(parent,ordinal,plaintext,ct);parent=sealedChild.Parent;await At("Retained");
                return await work.MaterializedAsync(parent.Fence,ordinal,sealedChild.Retention,ct);
            }
            catch(Exception error) when(error is InvalidDataException or IOException or CryptographicException)
            {
                using var review=new CancellationTokenSource(ZipNumericLimits.AttemptTimeout);
                var latest=await parents.ReadAsync(parent.Binding.OperationId,review.Token);
                if(latest?.Fence==parent.Fence&&latest.State is (ZipParentState.Planned or ZipParentState.Processing))
                    await work.RejectExtractionAsync(parent.Fence,ordinal,"ZipExtractionRejected",review.Token);
                throw;
            }
            finally{if(plaintext is not null)CryptographicOperations.ZeroMemory(plaintext);}
        }
        finally{ZipChildWorkGate.Gate.Release();}
    }
    private sealed class ChargedStream(Stream inner,Action<int> charge,CancellationToken ct) : Stream
    {
        public override bool CanRead=>true;public override bool CanSeek=>true;public override bool CanWrite=>false;
        public override long Length=>inner.Length;public override long Position{get=>inner.Position;set=>inner.Position=value;}
        public override int Read(byte[] bytes,int offset,int count)=>Read(bytes.AsSpan(offset,count));
        public override int Read(Span<byte> bytes)
        {ct.ThrowIfCancellationRequested();var count=checked((int)Math.Min(bytes.Length,Math.Max(0,Length-Position)));if(count>0)charge(count);ct.ThrowIfCancellationRequested();return inner.Read(bytes);}
        public override long Seek(long offset,SeekOrigin origin)=>inner.Seek(offset,origin);
        public override void Flush()=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
        public override void Write(byte[] bytes,int offset,int count)=>throw new NotSupportedException();
    }
}
internal sealed class ZipIncrementalCrc32
{
    private static readonly uint[] Table=CreateTable();private uint _state=uint.MaxValue;
    public uint Value=>~_state;
    public void Append(ReadOnlySpan<byte> bytes){foreach(var b in bytes)_state=Table[(byte)(_state^b)]^(_state>>8);}
    private static uint[] CreateTable(){var t=new uint[256];for(uint i=0;i<256;i++){var v=i;for(var bit=0;bit<8;bit++)v=(v&1)!=0?0xEDB88320u^(v>>1):v>>1;t[i]=v;}return t;}
}

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using EMF.ConsoleApplication;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Persistence.Storage;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Ingestion;
namespace EMF.Tests.TestInfrastructure;
internal sealed class ZipDurableRuntimeFixture
{
    internal ArtifactIngestionFixture Evidence;internal string Root=>Evidence.Root;internal SqliteZipExtractionJournal Journal;
    internal ZipPrivateContentStorage ParentStorage,ChildStorage;internal Crypto ParentCrypto,ChildCrypto;
    internal ZipParentRetentionService ParentRetention;internal ZipProtectedRetentionService ChildRetention;
    internal ZipSequentialExtractor Extractor;internal ZipChildScanService Scan;internal ZipChildIngestionService Ingest;internal ZipDurableExtractionDriver Driver=null!;
    internal string Events=>Path.Combine(Root,"zip-events.jsonl");
    internal ZipDurableRuntimeFixture(ArtifactIngestionFixture evidence,TimeProvider? time=null)
    {
        Evidence=evidence;Journal=new(evidence.DatabasePath,time);
        ParentStorage=new(Path.Combine(Root,"zip-parent-private"),BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Parent));
        ChildStorage=new(Path.Combine(Root,"zip-child-private"),BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Child));
        ParentCrypto=new(this,"parent");ChildCrypto=new(this,"child");
        ParentRetention=new(Journal,Journal,ParentStorage,ParentCrypto);ChildRetention=new(Journal,Journal,ChildStorage,ChildCrypto);
        Extractor=new(Journal,Journal,Journal,ChildRetention);Extractor.Checkpoint=n=>{if(n=="Reserved")Log("extraction-start");return Task.CompletedTask;};
        Scan=new(Journal,Journal,Journal,ChildRetention,new Scanner(this));
        evidence.Encryption.EncryptHook=()=>{Log("ingestion-encrypt");return Task.CompletedTask;};
        var host=new ZipChildIngestionHost(evidence.DatabasePath,evidence.Physical,evidence.Staging,evidence.Encryption,
            (operation,ct)=>{evidence.SecurityContext.Operation=new(new(operation),evidence.SecurityContext.Operation.AuthorizedOperationId,"synthetic-zip-child",new("zip"));return Task.FromResult<IArtifactIngestionSecurityContext>(evidence.SecurityContext);},
            evidence.Classification,evidence.Authorization,evidence.Audit,evidence.Fingerprints);
        Ingest=new(Journal,Journal,Journal,ChildRetention,host);Ingest.Checkpoint=n=>{if(n is "Ingesting" or "Resuming")Log(n);return Task.CompletedTask;};
        ComposeDriver();
    }
    internal void ComposeDriver()=>Driver=new(Journal,Journal,Journal,Journal,Journal,ParentRetention,ChildRetention,new(),Extractor,Scan,Ingest);
    internal void Log(string kind)
    {using var file=new FileStream(Events,FileMode.Append,FileAccess.Write,FileShare.Read);var data=System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new {Kind=kind,Process=Environment.ProcessId})+"\n");file.Write(data);file.Flush(true);}
    internal int Count(string kind)=>File.Exists(Events)?File.ReadLines(Events).Count(line=>{using var record=JsonDocument.Parse(line);return record.RootElement.GetProperty("Kind").GetString()==kind;}):0;
    internal static byte[] Zip(bool two=false)
    {using var bytes=new MemoryStream();using(var zip=new ZipArchive(bytes,ZipArchiveMode.Create,true)){for(var i=0;i<(two?2:1);i++){using var entry=zip.CreateEntry("duplicate.txt").Open();entry.Write("synthetic durable child payload"u8);}}return bytes.ToArray();}
    internal async Task<ZipParentSnapshot> CreateParent(bool two=false)
    {
        await Journal.InitializeAsync();var zip=Zip(two);using var source=new ArtifactContentReadLease(new("source-parent"),new("source-revision"),zip.Length,zip);
        await ParentRetention.RetainAsync("zip","parent",new string('B',64),source);return await Journal.ClaimAsync("zip","worker",TimeSpan.FromMinutes(30));
    }
    internal async Task<ZipParentSnapshot> Read()=> (await Journal.ReadAsync("zip"))!;
    internal sealed class Crypto(ZipDurableRuntimeFixture owner,string label):IEnvelopeEncryptionService,IBoundedEnvelopeDecryptionService
    {
        internal byte[]? LastPlaintext;
        private readonly DevelopmentEnvelopeEncryptionService _inner=new(new ArtifactIngestionFixture.Keys());
        public Task<EncryptedEnvelope> EncryptWithContextAsync(ReadOnlyMemory<byte> p,ReadOnlyMemory<byte> a,CancellationToken ct=default){owner.Log(label+"-encrypt");return _inner.EncryptWithContextAsync(p,a,ct);}
        public Task<byte[]> DecryptWithContextAsync(EncryptedEnvelope e,ReadOnlyMemory<byte> a,CancellationToken ct=default)=>_inner.DecryptWithContextAsync(e,a,ct);
        public Task<EncryptedEnvelope> EncryptAsync(ReadOnlyMemory<byte> p,CancellationToken ct=default)=>_inner.EncryptAsync(p,ct);
        public Task<byte[]> DecryptAsync(EncryptedEnvelope e,CancellationToken ct=default)=>_inner.DecryptAsync(e,ct);
        public async Task<byte[]> DecryptWithContextBoundedAsync(EncryptedEnvelope e,ReadOnlyMemory<byte> a,EnvelopeDecryptionLimits l,CancellationToken ct=default){owner.Log(label+"-decrypt");return LastPlaintext=await _inner.DecryptWithContextBoundedAsync(e,a,l,ct);}
    }
    private sealed class Scanner(ZipDurableRuntimeFixture owner):IMalwareScanner
    {
        private readonly ZipChildScanTests.Scanner _inner=new();public MalwareScannerPolicy Policy=>_inner.Policy;
        public Task<MalwareScanEvidence> ScanAsync(MalwareScanRequest r,Stream source,CancellationToken ct=default){owner.Log("scan");return _inner.ScanAsync(r,source,ct);}
    }
}

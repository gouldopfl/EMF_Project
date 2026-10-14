using System.Runtime.Versioning;
using System.Globalization;
using System.Text.Json;
using EMF.Core.Contracts.Zip;
using EMF.Persistence.Repositories;
using EMF.Persistence.Storage;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

[SupportedOSPlatform("linux")]
public sealed class ZipCandidateCustodyEncodingTests
{
    internal static CandidateCleanupIntent Golden()
    {
        var file=new ZipCandidateFileIdentity(8,1,101,42,1,1000,1000,0x8180,257,100,2,200,3);
        return ZipCandidateCustodyFoundationFixture.IntentFor(ZipCandidateCustodyFoundationFixture.Identity(new string('A',64)),new(new(8,1,11,42),new(8,1,12,42),file,new string('9',64)));
    }
    [Fact]
    public void Fixed_v1_vector_and_roundtrip_are_culture_independent()
    {
        var i=Golden();var bytes=ZipCandidateCustodyEncoding.IntentBytes(i);
        Console.WriteLine("D1_VECTOR_JSON="+ZipCandidateCustodyEncoding.Json(i));
        Console.WriteLine("D1_VECTOR_HEX="+Convert.ToHexString(bytes));
        Assert.Equal("EE24B792896E597B1EB5950108A8930E747E2C4FCD1D90124FB61CA4DC6F4BC1",i.IntentDigest);
        var previous=CultureInfo.CurrentCulture;
        try{CultureInfo.CurrentCulture=new("ar-SA");Assert.Equal(bytes,ZipCandidateCustodyEncoding.IntentBytes(i));Assert.Equal(i,ZipCandidateCustodyEncoding.Decode<CandidateCleanupIntent>(ZipCandidateCustodyEncoding.Json(i)));}
        finally{CultureInfo.CurrentCulture=previous;}
        var c=ZipCandidateCustodyFoundationFixture.Completion(i);ZipCandidateCustodyEncoding.Validate(c,i);
        Assert.NotEqual(i.IntentDigest,c.CompletionDigest);
        Assert.Equal(c,ZipCandidateCustodyEncoding.Decode<CandidateCustodyCompletion>(ZipCandidateCustodyEncoding.Json(c)));
    }
    [Fact]
    public void Occurrence_and_completion_structural_identities_are_exact()
    {
        var i = Golden(); var c = ZipCandidateCustodyFoundationFixture.Completion(i);
        foreach (var invalid in new[] { c with { CustodyIdentity = c.CustodyIdentity with { MountId = 0 } }, c with { CustodyIdentity = c.CustodyIdentity with { ChangedNanoseconds = 1000000000 } }, c with { CustodyDirectoryIdentity = c.CustodyDirectoryIdentity with { Inode = 0 } }, c with { CustodyDirectoryIdentity = c.CustodyDirectoryIdentity with { MountId = 0 } } })
            Assert.Throws<InvalidDataException>(() => ZipCandidateCustodyEncoding.Seal(invalid, i));
        var wrongIngestion = i.Anchors.Ingestion with { ProvisionalArtifactId = "other-provisional", Disposition = EMF.Core.Contracts.Ingestion.ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact };
        var anchors = i.Anchors with { Ingestion = wrongIngestion, Acknowledgement = i.Anchors.Acknowledgement with { Details = i.Anchors.Acknowledgement.Details! with { Ingestion = wrongIngestion } } };
        Assert.Throws<InvalidDataException>(() => ZipCandidateCustodyEncoding.Seal(i with { Anchors = anchors }));
    }
    [Fact]
    public void All_binding_groups_affect_digest_without_rebinding_recovery_path()
    {
        var i=Golden();var variants=new[]{
            i with{RootIdentity=i.RootIdentity with{Inode=99}},
            i with{SourceDirectoryIdentity=i.SourceDirectoryIdentity with{Inode=99}},
            i with{SourceIdentity=i.SourceIdentity with{ChangedSeconds=999}},
            i with{InitiatingHistory=i.InitiatingHistory with{Actor="other-actor"}},
            i with{Anchors=i.Anchors with{AdoptionDigest=new string('1',64)}},
            i with{Anchors=i.Anchors with{AuditFactsDigest=new string('2',64)}},
            i with{Anchors=i.Anchors with{ContainsId=99}},
            i with{BaseRetentionRevision=2,CommittedRetentionRevision=3}};
        foreach(var v in variants){var sealedValue=ZipCandidateCustodyEncoding.Seal(v);Assert.NotEqual(i.IntentDigest,sealedValue.IntentDigest);Assert.Equal(i.CustodyRelativePath,sealedValue.CustodyRelativePath);}
        Assert.NotEqual(ZipCandidateCustodyEncoding.Key(i.Anchors,i.CandidateHash),ZipCandidateCustodyEncoding.Key(i.Anchors with{Parent=i.Anchors.Parent with{OperationId="other-parent"}},i.CandidateHash));
    }
    [Fact]
    public void Malformed_versions_hashes_and_incomplete_or_rebound_proof_fail_closed()
    {
        var i=Golden();Assert.Throws<InvalidDataException>(()=>ZipCandidateCustodyEncoding.Seal(i with{ProtocolVersion=2}));
        Assert.Throws<InvalidDataException>(()=>ZipCandidateCustodyEncoding.Seal(i with{CandidateHash="lowercase"}));
        Assert.Throws<InvalidDataException>(()=>ZipCandidateCustodyEncoding.Seal(i with{StoredEncryptedLength=-1}));
        Assert.Throws<InvalidDataException>(()=>ZipCandidateCustodyEncoding.Validate(i with{IntentDigest=new string('0',64)}));
        var json=ZipCandidateCustodyEncoding.Json(i);
        Assert.Throws<InvalidDataException>(()=>ZipCandidateCustodyEncoding.Decode<CandidateCleanupIntent>(json.Insert(1,"\"ProtocolVersion\":1,")));
        Assert.Throws<JsonException>(()=>ZipCandidateCustodyEncoding.Decode<CandidateCleanupIntent>(json.Insert(1,"\"Unknown\":1,")));
        Assert.Throws<JsonException>(()=>ZipCandidateCustodyEncoding.Decode<CandidateCleanupIntent>("{}"));
        var c=ZipCandidateCustodyFoundationFixture.Completion(i);
        Assert.Throws<InvalidDataException>(()=>ZipCandidateCustodyEncoding.Seal(c with{SourceDirectoryFlushed=false},i));
        Assert.Throws<InvalidDataException>(()=>ZipCandidateCustodyEncoding.Seal(c with{EffectActor="invented-old-actor"},i));
        Assert.False(typeof(ZipCandidateCleanupReceipt).IsAssignableFrom(typeof(ZipCandidateCustodyReceipt)));
        Assert.DoesNotContain(typeof(CandidateCleanupIntent).GetProperties(),p=>typeof(Delegate).IsAssignableFrom(p.PropertyType)||p.PropertyType==typeof(CancellationToken));
    }
}

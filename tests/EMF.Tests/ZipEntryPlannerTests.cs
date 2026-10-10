using System.Security.Cryptography;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Core.Models.Identities;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Tests.TestInfrastructure;
using static EMF.Tests.ZipCentralDirectoryPreflightTests;

namespace EMF.Tests;

public sealed class ZipEntryPlannerTests
{
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes,false)
    {
        public override int Read(Span<byte> destination)=>base.Read(destination[..Math.Min(1,destination.Length)]);
    }
    private sealed class CustomLease(byte[] bytes) : IArtifactContentReadLease
    {
        private readonly ArtifactContentReadLease _inner=Lease(bytes);
        public ArtifactId ArtifactId=>_inner.ArtifactId;
        public ArtifactContentRevision Revision=>_inner.Revision;
        public long StoredLength=>_inner.StoredLength;
        public long ReturnedLength=>_inner.ReturnedLength;
        public ReadOnlyMemory<byte> Content=>_inner.Content;
        public Stream OpenReadStream()=>new FragmentedStream(bytes);
        public void Dispose()=>_inner.Dispose();
        public ValueTask DisposeAsync()=>_inner.DisposeAsync();
    }
    private static async Task<ZipParentSnapshot> Parent(IZipExtractionJournal journal,byte[] bytes)
    {
        var binding=new ZipParentBinding("zip-parent","artifact-parent",new("retained","revision",Convert.ToHexString(SHA256.HashData(bytes)),bytes.Length),new string('B',64));
        await journal.CreateAsync(binding);return await journal.ClaimAsync(binding.OperationId,"owner",TimeSpan.FromMinutes(5));
    }
    private static ArtifactContentReadLease Lease(byte[] b)=>new(new ArtifactId("retained"),new("revision"),b.Length,b);
    [Fact]
    public async Task Duplicate_occurrences_have_stable_distinct_ids_and_replay_does_not_parse_again()
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();var j=new SqliteZipExtractionJournal(f.DatabasePath);await j.InitializeAsync();
        var b=Zip(2);var p=await Parent(j,b);using var lease=Lease(b);var calls=0;var planner=new ZipEntryPlanner{ArchiveConstructionStarting=()=>calls++};
        p=await planner.AdmitAsync(j,p,lease);Assert.Equal(1,calls);Assert.Equal(2,p.Entries.Count);
        Assert.Equal(p.Entries[0].Plan.FullName,p.Entries[1].Plan.FullName);Assert.NotEqual(p.Entries[0].Plan.ChildOperationId,p.Entries[1].Plan.ChildOperationId);
        var restored=(await new SqliteZipExtractionJournal(f.DatabasePath).ReadAsync(p.Binding.OperationId))!;
        var replay=await planner.AdmitAsync(j,restored,lease);Assert.Equal(1,calls);Assert.Equal(p.Plan!.Hash,replay.Plan!.Hash);
    }
    [Theory]
    [InlineData("../secret")] [InlineData("/absolute")] [InlineData("C:\\secret")] [InlineData("\\\\host\\share")] [InlineData("folder/../secret")] [InlineData("folder\\..\\secret")]
    public async Task Unsafe_names_are_reviewed_as_metadata_and_never_trusted_paths(string name)
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();var j=new SqliteZipExtractionJournal(f.DatabasePath);await j.InitializeAsync();
        var b=Zip(name:name);var p=await Parent(j,b);using var lease=Lease(b);
        await Assert.ThrowsAsync<InvalidDataException>(()=>new ZipEntryPlanner().AdmitAsync(j,p,lease));
        var reviewed=(await j.ReadAsync(p.Binding.OperationId))!;Assert.Equal(ZipParentState.RequiresReview,reviewed.State);Assert.Null(reviewed.Plan);
    }
    [Fact]
    public async Task Malformed_or_overcount_input_rejects_before_runtime_constructor()
    {
        foreach(var b in new[]{Zip(1001),new byte[22]})
        {
            await using var f=await ArtifactIngestionFixture.CreateAsync();var j=new SqliteZipExtractionJournal(f.DatabasePath);await j.InitializeAsync();
            var p=await Parent(j,b);using var lease=Lease(b);var calls=0;
            var planner=new ZipEntryPlanner{ArchiveConstructionStarting=()=>calls++};
            await Assert.ThrowsAsync<InvalidDataException>(()=>planner.AdmitAsync(j,p,lease));Assert.Equal(0,calls);
            Assert.Equal(1,(await j.ReadAsync(p.Binding.OperationId))!.Budget.PreflightAttempts);
        }
    }
    [Fact]
    public async Task Directory_has_no_child_id_and_crc_encryption_and_parent_binding_are_recorded()
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();var j=new SqliteZipExtractionJournal(f.DatabasePath);await j.InitializeAsync();
        var b=Zip(name:"folder/");var p=await Parent(j,b);using var lease=Lease(b);p=await new ZipEntryPlanner().AdmitAsync(j,p,lease);
        var e=Assert.Single(p.Entries).Plan;Assert.True(e.IsDirectory);Assert.Null(e.FileOrdinal);Assert.Null(e.ChildOperationId);Assert.Null(e.ProvisionalArtifactId);
        Assert.False(e.IsEncrypted);Assert.NotEqual(0u,e.Crc32);Assert.Contains(p.Binding.Input.Sha256,p.Plan!.PreflightReceiptJson);
    }
    [Fact]
    public async Task Changed_input_binding_rejects_without_constructor_or_reservation()
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();var j=new SqliteZipExtractionJournal(f.DatabasePath);await j.InitializeAsync();
        var b=Zip();var p=await Parent(j,b);b[0]^=1;using var lease=Lease(b);var calls=0;
        var planner=new ZipEntryPlanner{ArchiveConstructionStarting=()=>calls++};
        await Assert.ThrowsAsync<InvalidDataException>(()=>planner.AdmitAsync(j,p,lease));Assert.Equal(0,calls);
        Assert.Equal(0,(await j.ReadAsync(p.Binding.OperationId))!.Budget.PreflightAttempts);
    }

    [Fact]
    public async Task Stale_snapshot_rejects_before_constructor_or_new_reservation()
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();var j=new SqliteZipExtractionJournal(f.DatabasePath);await j.InitializeAsync();
        var b=Zip();var stale=await Parent(j,b);
        var current=await j.ClaimAsync(stale.Binding.OperationId,"owner",TimeSpan.FromMinutes(5));
        using var lease=Lease(b);var calls=0;var planner=new ZipEntryPlanner{ArchiveConstructionStarting=()=>calls++};
        await Assert.ThrowsAsync<ZipFenceException>(()=>planner.AdmitAsync(j,stale,lease));Assert.Equal(0,calls);
        Assert.Equal(0,(await j.ReadAsync(current.Binding.OperationId))!.Budget.PreflightAttempts);
    }
    [Fact]
    public async Task Ownership_epoch_changes_during_detached_parsing_fence_final_id_persistence()
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();var j=new SqliteZipExtractionJournal(f.DatabasePath);await j.InitializeAsync();
        var b=Zip(2);var p=await Parent(j,b);using var lease=Lease(b);
        var planner=new ZipEntryPlanner{ArchiveConstructionStarting=()=>j.ClaimAsync(p.Binding.OperationId,"owner",TimeSpan.FromMinutes(5)).GetAwaiter().GetResult()};
        await Assert.ThrowsAsync<ZipFenceException>(()=>planner.AdmitAsync(j,p,lease));
        var current=(await j.ReadAsync(p.Binding.OperationId))!;Assert.Null(current.Plan);Assert.Empty(current.Entries);
        Assert.Equal(1,current.Budget.PreflightAttempts);
    }

    [Fact]
    public async Task Unsupported_stream_semantics_fail_closed_before_runtime_constructor()
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();var j=new SqliteZipExtractionJournal(f.DatabasePath);await j.InitializeAsync();
        var b=Zip();var p=await Parent(j,b);using var lease=new CustomLease(b);var calls=0;
        var planner=new ZipEntryPlanner{ArchiveConstructionStarting=()=>calls++};
        await Assert.ThrowsAsync<InvalidDataException>(()=>planner.AdmitAsync(j,p,lease));Assert.Equal(0,calls);
        Assert.Equal(ZipParentState.RequiresReview,(await j.ReadAsync(p.Binding.OperationId))!.State);
    }

    [Fact]
    public async Task Encrypted_status_is_admitted_metadata_without_opening_or_decompressing_child()
    {
        await using var f=await ArtifactIngestionFixture.CreateAsync();var j=new SqliteZipExtractionJournal(f.DatabasePath);await j.InitializeAsync();
        var b=Zip();var start=(int)U32(b,b.Length-6);W16(b,start+8,U16(b,start+8)|1);W16(b,6,U16(b,6)|1);
        var p=await Parent(j,b);using var lease=Lease(b);p=await new ZipEntryPlanner().AdmitAsync(j,p,lease);
        Assert.True(Assert.Single(p.Entries).Plan.IsEncrypted);Assert.Equal(0,p.Budget.ExtractionAttempts);
        Assert.Equal(0x352441c2u,p.Entries[0].Plan.Crc32);
    }
}

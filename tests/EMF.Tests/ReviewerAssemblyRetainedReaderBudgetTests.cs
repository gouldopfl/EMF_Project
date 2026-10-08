using System.Runtime.InteropServices;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using Xunit;

namespace EMF.Tests;

public sealed class ReviewerAssemblyRetainedReaderBudgetTests
{
    private static long SessionLive(ReviewerAssemblyRetainedReaderLimits l)
    {
        long k=ReviewerAssemblyRetainedReaderFixture.Foundation.MaximumEncodedBytes,m=ReviewerAssemblyRetainedReaderFixture.Foundation.MaximumMembers;
        return checked(256+16*k+4096+(64+16*m)+128*m+(128+96*m)+(128+96*((long)l.MaximumRegisteredBuffersPerSession+1))+(64+16*((long)l.MaximumMetadataDepth+1)));
    }
    private static long SessionWork(ReviewerAssemblyRetainedReaderLimits l)=>checked(256+16L*ReviewerAssemblyRetainedReaderFixture.Foundation.MaximumEncodedBytes+4096+ReviewerAssemblyRetainedReaderFixture.Foundation.MaximumEncodedBytes+l.MaximumRegisteredBuffersPerSession+1L);
    [Fact]
    public void Construction_exact_live_bound_and_one_below_are_configuration_boundaries()
    {
        var m=ReviewerAssemblyFoundationFixture.Create();var encoded=ReviewerAssemblyRetainedReaderFixture.Encode(m);var l=ReviewerAssemblyRetainedReaderFixture.Caps;
        long k=ReviewerAssemblyRetainedReaderFixture.Foundation.MaximumEncodedBytes;
        long fixedContext=256+128+96L*l.MaximumSessions,peak=checked(fixedContext+encoded.Length+3*k+2*(16*k+4096));
        using var c=ReviewerAssemblyRetainedReaders.FromEncoded(encoded,ReviewerAssemblyRetainedReaderFixture.Foundation,l with{MaximumLiveUnits=peak});
        Assert.Equal(fixedContext+k,c.Accounting.Live);Assert.True(c.Accounting.Spent>0);
        Assert.Throws<ArgumentOutOfRangeException>(()=>ReviewerAssemblyRetainedReaders.FromEncoded(encoded,ReviewerAssemblyRetainedReaderFixture.Foundation,l with{MaximumLiveUnits=peak-1}));
        Assert.Equal(ReviewerAssemblyFoundationFixture.GoldenSha256,ReviewerAssemblyCaptureRepresentation.Hash(encoded));
    }
    [Fact]
    public void Session_member_envelope_is_live_capacity_and_is_not_a_refundable_work_balance()
    {
        var l=ReviewerAssemblyRetainedReaderFixture.Caps;using var c=ReviewerAssemblyRetainedReaderFixture.Context();long retained=c.Accounting.Live,factoryWork=c.Accounting.Spent;
        var s=c.Acquire();Assert.Equal(SessionLive(l),s.Accounting.Live);Assert.Equal(retained+SessionLive(l),c.Accounting.Live);
        long spent=c.Accounting.Spent;Assert.True(spent>factoryWork);s.Dispose();Assert.Equal(retained,c.Accounting.Live);Assert.Equal(spent,c.Accounting.Spent);
        using var replacement=c.Acquire();Assert.True(c.Accounting.Spent>spent);Assert.Equal(SessionLive(l),replacement.Accounting.Live);
    }
    [Fact]
    public async Task Source_exact_output_live_and_one_over_reject_before_allocation()
    {
        var m=ReviewerAssemblyFoundationFixture.Create();long cost=64L+m.Sources[0].Content.Length;var l=ReviewerAssemblyRetainedReaderFixture.Caps;
        using(var c=ReviewerAssemblyRetainedReaderFixture.Context(m,l with{MaximumOutputLiveUnitsPerQuery=cost}))
        {
            using var s=c.Acquire();long baseline=s.Accounting.Live;s.BufferAllocated=_=>Assert.Equal(baseline+cost,s.Accounting.Live);
            Assert.Equal(m.Sources[0].Content,await ((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));Assert.Equal(baseline+cost,s.Accounting.Live);
        }
        using(var c=ReviewerAssemblyRetainedReaderFixture.Context(m,l with{MaximumOutputLiveUnitsPerQuery=cost-1}))
        {using var s=c.Acquire();bool allocated=false;s.BufferAllocated=_=>allocated=true;long baseline=s.Accounting.Live;await Assert.ThrowsAsync<InvalidDataException>(()=>((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));Assert.False(allocated);Assert.Equal(baseline,s.Accounting.Live);Assert.Equal(0,s.Accounting.Buffers);}
    }
    [Fact]
    public async Task Valid_session_cap_rejects_runtime_output_one_unit_above_remaining_capacity()
    {
        var l=ReviewerAssemblyRetainedReaderFixture.Caps;long baseline=SessionLive(l),cost=74;
        foreach(bool exact in new[]{true,false})
        {
            var caps=l with{MaximumSessionLiveUnits=baseline+cost-(exact?0:1),MaximumOutputLiveUnitsPerQuery=cost};
            using var c=ReviewerAssemblyRetainedReaderFixture.Context(caps:caps);using var s=c.Acquire();
            if(exact)Assert.NotNull(await ((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
            else{var error=await Assert.ThrowsAsync<InvalidDataException>(()=>((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));Assert.Equal("SessionLiveCapacity",error.Message);}
        }
        Assert.Throws<ArgumentOutOfRangeException>(()=>ReviewerAssemblyRetainedReaderFixture.Context(caps:l with{MaximumSessionLiveUnits=baseline-1,MaximumOutputLiveUnitsPerQuery=74}));
    }
    [Fact]
    public async Task Empty_source_queries_have_fresh_owned_arrays_and_print_has_one_alias_registration()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context(ReviewerAssemblyRetainedReaderFixture.Text(""));using var s=c.Acquire();
        var first=await ((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);var second=await ((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        Assert.NotSame(first,second);Assert.Empty(first!);Assert.Empty(second!);Assert.Equal(2,s.Accounting.Buffers);
        byte[]? owned=null;s.BufferAllocated=b=>owned=b;var page=Assert.Single(await ((IArtifactPrintRenderer)s).RenderAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
        Assert.True(MemoryMarshal.TryGetArray(page.Content,out var segment));Assert.Same(owned,segment.Array);Assert.Equal(3,s.Accounting.Buffers);
    }
    [Fact]
    public async Task Repeated_outputs_remain_charged_and_buffer_registry_exhaustion_does_not_reset()
    {
        var l=ReviewerAssemblyRetainedReaderFixture.Caps with{MaximumRegisteredBuffersPerSession=2,MaximumBuffersPerQuery=2};
        using var c=ReviewerAssemblyRetainedReaderFixture.Context(caps:l);using var s=c.Acquire();long baseline=s.Accounting.Live;
        await ((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);await ((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        Assert.Equal(baseline+148,s.Accounting.Live);Assert.Equal(2,s.Accounting.Buffers);long spent=s.Accounting.Spent;
        var ex=await Assert.ThrowsAsync<InvalidDataException>(()=>((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));Assert.Equal("BufferCapacity",ex.Message);
        Assert.Equal(baseline+148,s.Accounting.Live);Assert.True(s.Accounting.Spent>spent);
    }
    [Fact]
    public async Task Constructor_dictionary_and_clock_work_are_in_the_empty_metadata_artifact_ledger()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context(ReviewerAssemblyRetainedReaderFixture.Bare());using var s=c.Acquire();long live=s.Accounting.Live,spent=s.Accounting.Spent;
        var artifact=await ((IEvidenceRepository)s).GetArtifactAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        Assert.Equal(512,s.Accounting.Live-live); // Artifact128+Fingerprint128+discarded initializer128+root map128
        Assert.Equal(207,s.Accounting.Spent-spent); // Q159+P12+W36; W includes one initializer clock unit
        Assert.Equal(new DateTimeOffset(2020,1,2,3,4,5,TimeSpan.Zero),artifact!.CreatedUtc);
    }
    [Fact]
    public async Task Nested_metadata_formula_and_exact_one_below_limits_are_independent_of_heap_size()
    {
        var m=ReviewerAssemblyRetainedReaderFixture.Bare();var map=m.Artifacts[0].Rows[0].Metadata;
        map["a"]=ReviewerAssemblyRetainedReaderFixture.Null with{Kind=ReviewerAssemblyValueKind.Array,Items=new[]{ReviewerAssemblyRetainedReaderFixture.Null,ReviewerAssemblyRetainedReaderFixture.Null with{Kind=ReviewerAssemblyValueKind.Int64,Integer=7},ReviewerAssemblyRetainedReaderFixture.Null with{Kind=ReviewerAssemblyValueKind.Binary,Binary=new byte[]{1,2,3}}}};
        map["b"]=ReviewerAssemblyValue.Map(new(){["c"]=ReviewerAssemblyRetainedReaderFixture.Null with{Kind=ReviewerAssemblyValueKind.Boolean,Boolean=true}});
        var l=ReviewerAssemblyRetainedReaderFixture.Caps with{MaximumOutputLiveUnitsPerQuery=1363,MaximumMetadataNodes=7,MaximumMetadataDepth=3};
        using(var c=ReviewerAssemblyRetainedReaderFixture.Context(m,l))
        {using var s=c.Acquire();long live=s.Accounting.Live,spent=s.Accounting.Spent;await ((IEvidenceRepository)s).GetArtifactAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);Assert.Equal(1363,s.Accounting.Live-live);Assert.Equal(243,s.Accounting.Spent-spent);Assert.Equal(1,s.Accounting.Buffers);}
        using(var c=ReviewerAssemblyRetainedReaderFixture.Context(m,l with{MaximumOutputLiveUnitsPerQuery=1362}))
        {using var s=c.Acquire();await Assert.ThrowsAsync<InvalidDataException>(()=>((IEvidenceRepository)s).GetArtifactAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));Assert.Equal(0,s.Accounting.Buffers);}
        using(var c=ReviewerAssemblyRetainedReaderFixture.Context(m,l with{MaximumMetadataNodes=6}))Assert.Throws<InvalidDataException>(()=>c.Acquire());
        using(var c=ReviewerAssemblyRetainedReaderFixture.Context(m,l with{MaximumMetadataDepth=2}))Assert.Throws<InvalidDataException>(()=>c.Acquire());
    }
    [Fact]
    public async Task Work_peak_uses_query_allowance_and_spent_units_are_never_refunded()
    {
        // Bare fixture admission: fixed/staging34 +member6 +hash10 +six receipt scans84 +source19
        // +UTF8 bytes10 +artifact map3 +provenance fields6/map3 +classification5 +condition4 =184.
        var m=ReviewerAssemblyRetainedReaderFixture.Bare();var l=ReviewerAssemblyRetainedReaderFixture.Caps;
        const long admission=184,q=159,p=2,w=22,u=96;
        long ws=SessionWork(l);
        foreach(bool exact in new[]{true,false})
        {
            var caps=l with{MaximumSessionAdmissionWork=admission,MaximumPrecountWorkPerQuery=32,MaximumProjectionWorkPerQuery=64,MaximumWorkPerQuery=u,
                MaximumSessionWorkUnits=ws+admission+q+u-(exact?0:1)};
            using var c=ReviewerAssemblyRetainedReaderFixture.Context(m,caps);using var s=c.Acquire();Assert.Equal(ws+admission,s.Accounting.Spent);long spent=s.Accounting.Spent,live=s.Accounting.Live;
            if(exact){await ((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);Assert.Equal(spent+q+p+w,s.Accounting.Spent);Assert.Equal(0,s.Accounting.Reserved);}
            else{var error=await Assert.ThrowsAsync<InvalidDataException>(()=>((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));Assert.Equal("WorkCapacity",error.Message);Assert.Equal(spent+q,s.Accounting.Spent);Assert.Equal(live,s.Accounting.Live);}
        }
    }
    [Fact]
    public async Task Strict_text_source_and_utf16_bounds_have_exact_and_one_above_fixtures()
    {
        var m=ReviewerAssemblyRetainedReaderFixture.Text("AB😀");var l=ReviewerAssemblyRetainedReaderFixture.Caps;
        using(var c=ReviewerAssemblyRetainedReaderFixture.Context(m,l with{MaximumSourceBytesPerQuery=6,MaximumTextUtf16Chars=4,MaximumOutputLiveUnitsPerQuery=96}))
        {using var s=c.Acquire();long spent=s.Accounting.Spent;Assert.Equal("AB😀",await ((IArtifactTextExtractor)s).ExtractTextAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));Assert.Equal(179,s.Accounting.Spent-spent);}
        using(var c=ReviewerAssemblyRetainedReaderFixture.Context(m,l with{MaximumTextUtf16Chars=3}))
        {using var s=c.Acquire();await Assert.ThrowsAsync<InvalidDataException>(()=>((IArtifactTextExtractor)s).ExtractTextAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));}
        using(var c=ReviewerAssemblyRetainedReaderFixture.Context(m,l with{MaximumSourceBytesPerQuery=5}))
        {using var s=c.Acquire();await Assert.ThrowsAsync<InvalidDataException>(()=>((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));}
    }
    [Fact]
    public async Task Aggregate_metadata_limit_rejects_before_a_second_root_visit()
    {
        var m=ReviewerAssemblyRetainedReaderFixture.Bare();m=m with{Provenance=new[]{m.Provenance[0] with{Rows=new[]{m.Provenance[0].Rows[0],m.Provenance[0].Rows[0]}}}};
        using var c=ReviewerAssemblyRetainedReaderFixture.Context(m,ReviewerAssemblyRetainedReaderFixture.Caps with{MaximumMetadataNodes=1});using var s=c.Acquire();long spent=s.Accounting.Spent;
        await Assert.ThrowsAsync<InvalidDataException>(()=>((IEvidenceRepository)s).GetProvenanceAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
        Assert.Equal(159+16,s.Accounting.Spent-spent);Assert.Equal(0,s.Accounting.Reserved);Assert.Equal(0,s.Accounting.Buffers);
    }
    [Fact]
    public void Checked_overflow_rejects_before_counter_commit_or_owned_construction()
    {
        var l=ReviewerAssemblyRetainedReaderFixture.Caps;var encoded=ReviewerAssemblyRetainedReaderFixture.Encode(ReviewerAssemblyFoundationFixture.Create());
        Assert.Throws<ArgumentOutOfRangeException>(()=>ReviewerAssemblyRetainedReaders.FromEncoded(encoded,ReviewerAssemblyRetainedReaderFixture.Foundation,l with{MaximumPrecountWorkPerQuery=long.MaxValue,MaximumProjectionWorkPerQuery=1,MaximumWorkPerQuery=long.MaxValue}));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ReviewerAssemblyRetainedReaders.FromEncoded(encoded,ReviewerAssemblyRetainedReaderFixture.Foundation with{MaximumMembers=int.MaxValue},l));
        var budget=new ReviewerAssemblyRetainedReaderBudget(l with{MaximumLiveUnits=long.MaxValue,MaximumWorkUnits=long.MaxValue},long.MaxValue,long.MaxValue);
        lock(budget.Gate)
        {
            Assert.Equal("ArithmeticOverflow",Assert.Throws<InvalidDataException>(()=>budget.ReserveLive(null,1)).Message);
            Assert.Equal("ArithmeticOverflow",Assert.Throws<InvalidDataException>(()=>budget.Spend(null,1)).Message);
            Assert.Equal(long.MaxValue,budget.Live);Assert.Equal(long.MaxValue,budget.Spent);
        }
    }
}

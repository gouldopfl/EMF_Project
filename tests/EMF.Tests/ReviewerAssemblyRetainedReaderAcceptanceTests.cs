using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Security.Cryptography;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Orchestration;
using Xunit;

namespace EMF.Tests;

internal static class ReviewerAssemblyRetainedReaderFixture
{
    internal static ReviewerAssemblyFoundationLimits Foundation => ReviewerAssemblyFoundationFixture.Limits;
    // Synthetic test caps only; the implementation has no default limits.
    internal static ReviewerAssemblyRetainedReaderLimits Caps => new(
        MaximumLiveUnits:32_000_000,MaximumWorkUnits:100_000_000,MaximumSessions:2,
        MaximumSessionLiveUnits:8_000_000,MaximumSessionWorkUnits:10_000_000,MaximumSessionAdmissionWork:1_000_000,
        MaximumRegisteredBuffersPerSession:64,MaximumOutputLiveUnitsPerQuery:4_000_000,
        MaximumPrecountWorkPerQuery:100_000,MaximumProjectionWorkPerQuery:100_000,MaximumWorkPerQuery:200_000,
        MaximumOutputRowsPerQuery:1000,MaximumOutputNodesPerQuery:100_000,MaximumCollectionEntries:1000,
        MaximumMetadataNodes:10_000,MaximumMetadataDepth:16,MaximumMetadataKeyChars:4096,
        MaximumSourceBytesPerQuery:8192,MaximumTextUtf16Chars:8192,MaximumBuffersPerQuery:32,
        MaximumQueuedWorkUnitsPerSession:200_000,MaximumQueuedWorkUnitsContext:400_000,
        MaximumActiveProjectionsPerSession:1,MaximumWaitingProjectionsPerSession:1);
    internal static byte[] Encode(ReviewerAssemblyCaptureManifest m) =>
        ReviewerAssemblyCaptureRepresentation.Encode(m,ReviewerAssemblyDraftIdentity.Foundation,Foundation);
    internal static ReviewerAssemblyRetainedReaders Context(ReviewerAssemblyCaptureManifest? m=null,
        ReviewerAssemblyRetainedReaderLimits? caps=null) => ReviewerAssemblyRetainedReaders.FromEncoded(
            Encode(m??ReviewerAssemblyFoundationFixture.Create()),Foundation,caps??Caps);
    internal static ReviewerAssemblyCaptureManifest Text(string text)=>ReviewerAssemblyFoundationFixture.WithContent(
        ReviewerAssemblyFoundationFixture.Create(),Encoding.UTF8.GetBytes(text));
    internal static ReviewerAssemblyValue Null => new(ReviewerAssemblyValueKind.Null,null,null,null,null,null,null,null);
    internal static ArtifactId Artifact => new("artifact-1");
    internal static EvidencePackageId Package => new("package-1");
    internal static TaskCompletionSource Barrier()=>new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal static IDisposable ReleaseOnDispose(TaskCompletionSource barrier)=>new BarrierRelease(barrier);
    private sealed class BarrierRelease(TaskCompletionSource barrier):IDisposable
    {public void Dispose()=>barrier.TrySetResult();}

    internal static async Task Bounded(Task task)=>await task.WaitAsync(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    internal static ReviewerAssemblyCaptureManifest Bare()
    {var m=ReviewerAssemblyFoundationFixture.Create();m.Artifacts[0].Rows[0].Metadata.Clear();return m;}
}

public sealed class ReviewerAssemblyRetainedReaderAcceptanceTests
{
    [Fact]
    public async Task Every_supported_binding_projects_captured_fields_and_receipt_semantics()
    {
        var m=ReviewerAssemblyFoundationFixture.Create(true);
        m=m with{Members=m.Members with{Rows=m.Members.Rows.Reverse().ToArray()}};
        using var context=ReviewerAssemblyRetainedReaderFixture.Context(m);using var s=context.Acquire();
        var packages=(IEvidencePackageRepository)s;
        var p=await packages.GetEvidencePackageAsync(ReviewerAssemblyRetainedReaderFixture.Package);
        Assert.NotNull(p);Assert.Equal(m.Package.Rows[0].Id,p!.Id.Value);Assert.Equal(m.Package.Rows[0].IssueId,p.ClaimIssueId.Value);
        Assert.Equal(m.Package.Rows[0].Purpose,p.Purpose);Assert.Equal(m.Package.Rows[0].ReviewerRole,p.ReviewerRole);Assert.Null(p.ServiceConnectionBasisId);
        var pending=await packages.ReadReviewerSnapshotAsync(ReviewerAssemblyRetainedReaderFixture.Package);
        Assert.False(pending.IsLegacy);Assert.Null(pending.Snapshot);
        Assert.Null(await packages.GetReviewerSnapshotAsync(ReviewerAssemblyRetainedReaderFixture.Package));
        var members=await packages.GetEvidencePackageArtifactsAsync(ReviewerAssemblyRetainedReaderFixture.Package);
        Assert.Equal(m.Members.Rows.Select(x=>x.ArtifactId),members.Select(x=>x.ArtifactId.Value));
        Assert.All(members,x=>Assert.Equal("UnderlyingEvidence",x.ContentRole));Assert.All(members,x=>Assert.Null(x.ReviewerPageSelection));
        var issue=await ((IClaimIssueRepository)s).GetClaimIssueAsync(new("issue-1"));Assert.Equal("synthetic-issue",issue!.ClaimIssueType);Assert.Equal("claim-1",issue.ClaimId.Value);
        var claim=await ((IClaimRepository)s).GetClaimAsync(new("claim-1"));Assert.Equal("veteran-1",claim!.VeteranId.Value);
        var evidence=(IEvidenceRepository)s;
        var artifact=await evidence.GetArtifactAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        Assert.Equal(m.Artifacts[0].Rows[0].CreatedUtc,artifact!.CreatedUtc);Assert.Equal("synthetic.txt",artifact.Name);
        Assert.Equal("text/plain",artifact.ArtifactType);Assert.Equal("SHA-256",artifact.Fingerprint!.Algorithm);Assert.Equal(m.Sources[0].Fingerprint,artifact.Fingerprint.Value);
        var provenance=await evidence.GetProvenanceAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        Assert.Single(provenance);Assert.Equal(m.Provenance[0].Rows[0].RecordedUtc,provenance[0].RecordedUtc);Assert.Equal("synthetic-source",provenance[0].Source);Assert.Equal("synthetic-author",provenance[0].RecordedBy);
        var classes=await ((IEvidenceClassificationRepository)s).GetEvidenceClassificationsAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        Assert.Single(classes);Assert.Equal("classification-domain-1",classes[0].Id.Value);Assert.Equal("issue-1",classes[0].ClaimIssueId!.Value.Value);Assert.Equal("medical-evidence",classes[0].Classification);
        var conditions=await ((IConditionRepository)s).GetClaimedConditionsAsync(new("issue-1"));Assert.Single(conditions);Assert.Equal("condition-1",conditions[0].Id.Value);Assert.Equal("Synthetic condition",conditions[0].Name);
        Assert.Empty(await evidence.GetRelationshipsAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
        Assert.Empty(await ((IMedicalLiteratureRepository)s).GetMedicalLiteratureSourceIdsAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
        Assert.Empty(await ((IMedicationRepository)s).GetMedicationLedgersAsync(new("veteran-1")));
        Assert.Empty(await ((ISourceClarificationRepository)s).GetAsync(new("issue-1")));
        Assert.Empty(await ((IClinicalProgressionRepository)s).GetAsync(new("issue-1")));
        Assert.Equal(m.Sources[0].Content,await ((IArtifactContentStore)s).ReadAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
        Assert.Equal("synthetic\n",await ((IArtifactTextExtractor)s).ExtractTextAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
        Assert.Single(await ((IArtifactPrintRenderer)s).RenderAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
    }
    [Fact]
    public async Task Full_metadata_algebra_preserves_null_keys_types_order_and_width()
    {
        var m=ReviewerAssemblyFoundationFixture.Create();var map=m.Artifacts[0].Rows[0].Metadata;
        map["null"]=ReviewerAssemblyRetainedReaderFixture.Null;
        map["wide"]=ReviewerAssemblyRetainedReaderFixture.Null with{Kind=ReviewerAssemblyValueKind.Int64,Integer=9007199254740993};
        map["bool"]=ReviewerAssemblyRetainedReaderFixture.Null with{Kind=ReviewerAssemblyValueKind.Boolean,Boolean=true};
        map["decimal"]=ReviewerAssemblyRetainedReaderFixture.Null with{Kind=ReviewerAssemblyValueKind.Decimal,Decimal=1.2500m};
        m.Provenance[0].Rows[0].Properties["null"]=ReviewerAssemblyRetainedReaderFixture.Null;
        using var context=ReviewerAssemblyRetainedReaderFixture.Context(m);using var s=context.Acquire();
        var a=await ((IEvidenceRepository)s).GetArtifactAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);
        Assert.True(a!.Metadata.ContainsKey("null"));Assert.Null(a.Metadata["null"]);
        Assert.Equal(9007199254740993,Assert.IsType<long>(a.Metadata["wide"]));Assert.True(Assert.IsType<bool>(a.Metadata["bool"]));
        Assert.Equal(1.25m,Assert.IsType<decimal>(a.Metadata["decimal"]));
        var nested=Assert.IsType<Dictionary<string,object>>(a.Metadata["z"]);var values=Assert.IsType<object[]>(nested["values"]);
        Assert.Equal(42L,values[0]);Assert.Null(values[1]);Assert.Equal(1.25m,values[2]);Assert.Equal(new byte[]{7,8,9},Assert.IsType<byte[]>(nested["payload"]));
        var prov=await ((IEvidenceRepository)s).GetProvenanceAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);Assert.True(prov[0].Properties.ContainsKey("null"));Assert.Null(prov[0].Properties["null"]);
    }
    [Theory]
    [InlineData(ReviewerAssemblyValueKind.Null)] [InlineData(ReviewerAssemblyValueKind.Boolean)]
    [InlineData(ReviewerAssemblyValueKind.Int64)] [InlineData(ReviewerAssemblyValueKind.Decimal)]
    [InlineData(ReviewerAssemblyValueKind.Binary)] [InlineData(ReviewerAssemblyValueKind.Array)] [InlineData(ReviewerAssemblyValueKind.Map)]
    public void Semantic_keys_reject_every_nonstring_tag(ReviewerAssemblyValueKind kind)
    {
        var value=kind switch
        {
            ReviewerAssemblyValueKind.Boolean=>ReviewerAssemblyRetainedReaderFixture.Null with{Kind=kind,Boolean=true},
            ReviewerAssemblyValueKind.Int64=>ReviewerAssemblyRetainedReaderFixture.Null with{Kind=kind,Integer=7},
            ReviewerAssemblyValueKind.Decimal=>ReviewerAssemblyRetainedReaderFixture.Null with{Kind=kind,Decimal=1m},
            ReviewerAssemblyValueKind.Binary=>ReviewerAssemblyRetainedReaderFixture.Null with{Kind=kind,Binary=new byte[]{1}},
            ReviewerAssemblyValueKind.Array=>ReviewerAssemblyRetainedReaderFixture.Null with{Kind=kind,Items=Array.Empty<ReviewerAssemblyValue>()},
            ReviewerAssemblyValueKind.Map=>ReviewerAssemblyValue.Map(new()),_=>ReviewerAssemblyRetainedReaderFixture.Null
        };
        foreach(string key in new[]{"sourceType","evidenceTitle","noteTitle","evidenceDate","noteDate","literatureAuthors","literaturePublication","literatureDoi","literaturePmid","sourceArtifactId","summary"})
        {
            var m=ReviewerAssemblyFoundationFixture.Create();m.Artifacts[0].Rows[0].Metadata[key]=value;
            using var c=ReviewerAssemblyRetainedReaderFixture.Context(m);Assert.Throws<InvalidDataException>(()=>c.Acquire());
        }
    }
    [Theory]
    [InlineData("sourceStartPage")] [InlineData("sourceEndPage")] [InlineData("sourceStartText")] [InlineData("sourceEndText")]
    public void Source_boundary_declarations_reject_even_string_values(string key)
    {var m=ReviewerAssemblyFoundationFixture.Create();m.Artifacts[0].Rows[0].Metadata[key]=ReviewerAssemblyValue.String("1");using var c=ReviewerAssemblyRetainedReaderFixture.Context(m);Assert.Throws<InvalidDataException>(()=>c.Acquire());}
    [Fact]
    public void Clinical_source_type_rejects()
    {var m=ReviewerAssemblyFoundationFixture.Create();m.Artifacts[0].Rows[0].Metadata["sourceType"]=ReviewerAssemblyValue.String("veterans-clinical-note");using var c=ReviewerAssemblyRetainedReaderFixture.Context(m);Assert.Throws<InvalidDataException>(()=>c.Acquire());}
    [Theory]
    [InlineData("")] [InlineData("é")] [InlineData("😀")] [InlineData("\uFEFFA")] [InlineData("A\rB\nC\r\nD")]
    public async Task Strict_text_and_every_print_page_field_are_preserved(string text)
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context(ReviewerAssemblyRetainedReaderFixture.Text(text));using var s=c.Acquire();
        Assert.Equal(text,await ((IArtifactTextExtractor)s).ExtractTextAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
        var pages=await ((IArtifactPrintRenderer)s).RenderAsync(ReviewerAssemblyRetainedReaderFixture.Artifact);var page=Assert.Single(pages);
        Assert.Equal(1,page.PageNumber);Assert.Equal("text/plain",page.ContentType);Assert.Equal(Encoding.UTF8.GetBytes(text),page.Content.ToArray());Assert.Equal(0,page.SuggestedClockwiseRotation);Assert.Null(page.TextGeometry);
    }
    [Theory]
    [InlineData("C0AF")] [InlineData("80")] [InlineData("E282")] [InlineData("EDA080")] [InlineData("F4908080")]
    public void Encoded_invalid_utf8_cannot_publish_a_context(string hex)
    {
        byte[] content=Convert.FromHexString(hex);string hash=Convert.ToHexString(SHA256.HashData(content));
        var node=JsonNode.Parse(ReviewerAssemblyFoundationFixture.Golden)!;
        node["sources"]![0]!["content"]=Convert.ToBase64String(content);node["sources"]![0]!["fingerprint"]=hash;node["sources"]![0]!["sha256"]=hash;node["artifacts"]![0]!["rows"]![0]!["fingerprint"]=hash;
        Assert.Throws<InvalidDataException>(()=>ReviewerAssemblyRetainedReaders.FromEncoded(Encoding.UTF8.GetBytes(node.ToJsonString()),ReviewerAssemblyRetainedReaderFixture.Foundation,ReviewerAssemblyRetainedReaderFixture.Caps));
    }
    [Fact]
    public async Task Foreign_default_and_case_changed_keys_never_become_null_or_empty()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context();using var s=c.Acquire();
        foreach(var key in new[]{default(ArtifactId),new ArtifactId("foreign"),new ArtifactId("Artifact-1")})
        {
            await Assert.ThrowsAsync<InvalidDataException>(()=>((IEvidenceRepository)s).GetRelationshipsAsync(key));
            await Assert.ThrowsAsync<InvalidDataException>(()=>((IArtifactContentStore)s).ReadAsync(key));
        }
        await Assert.ThrowsAsync<InvalidDataException>(()=>((IEvidencePackageRepository)s).GetReviewerSnapshotAsync(new("foreign")));
        await Assert.ThrowsAsync<InvalidDataException>(()=>((IMedicationRepository)s).GetMedicationLedgersAsync(new("foreign")));
    }
    [Fact]
    public async Task All_131_unsupported_methods_are_explicit_deterministic_rejections()
    {
        using var c=ReviewerAssemblyRetainedReaderFixture.Context();using var s=c.Acquire();
        static MethodInfo Supported<TInterface>(string name,Type keyType) =>
            typeof(TInterface).GetMethod(name,new[]{keyType,typeof(CancellationToken)})
            ?? throw new InvalidOperationException($"Supported method not found: {typeof(TInterface).Name}.{name}");
        var supported=new HashSet<MethodInfo>
        {
            Supported<IEvidencePackageRepository>(nameof(IEvidencePackageRepository.ReadReviewerSnapshotAsync),typeof(EvidencePackageId)),
            Supported<IEvidencePackageRepository>(nameof(IEvidencePackageRepository.GetReviewerSnapshotAsync),typeof(EvidencePackageId)),
            Supported<IEvidencePackageRepository>(nameof(IEvidencePackageRepository.GetEvidencePackageAsync),typeof(EvidencePackageId)),
            Supported<IEvidencePackageRepository>(nameof(IEvidencePackageRepository.GetEvidencePackageArtifactsAsync),typeof(EvidencePackageId)),
            Supported<IClaimIssueRepository>(nameof(IClaimIssueRepository.GetClaimIssueAsync),typeof(ClaimIssueId)),
            Supported<IClaimRepository>(nameof(IClaimRepository.GetClaimAsync),typeof(ClaimId)),
            Supported<IEvidenceRepository>(nameof(IEvidenceRepository.GetArtifactAsync),typeof(ArtifactId)),
            Supported<IEvidenceRepository>(nameof(IEvidenceRepository.GetRelationshipsAsync),typeof(ArtifactId)),
            Supported<IEvidenceRepository>(nameof(IEvidenceRepository.GetProvenanceAsync),typeof(ArtifactId)),
            Supported<IEvidenceClassificationRepository>(nameof(IEvidenceClassificationRepository.GetEvidenceClassificationsAsync),typeof(ArtifactId)),
            Supported<IMedicalLiteratureRepository>(nameof(IMedicalLiteratureRepository.GetMedicalLiteratureSourceIdsAsync),typeof(ArtifactId)),
            Supported<IConditionRepository>(nameof(IConditionRepository.GetClaimedConditionsAsync),typeof(ClaimIssueId)),
            Supported<IMedicationRepository>(nameof(IMedicationRepository.GetMedicationLedgersAsync),typeof(VeteranId)),
            Supported<ISourceClarificationRepository>(nameof(ISourceClarificationRepository.GetAsync),typeof(ClaimIssueId)),
            Supported<IClinicalProgressionRepository>(nameof(IClinicalProgressionRepository.GetAsync),typeof(ClaimIssueId)),
            Supported<IArtifactContentStore>(nameof(IArtifactContentStore.ReadAsync),typeof(ArtifactId)),
            Supported<IArtifactTextExtractor>(nameof(IArtifactTextExtractor.ExtractTextAsync),typeof(ArtifactId)),
            Supported<IArtifactPrintRenderer>(nameof(IArtifactPrintRenderer.RenderAsync),typeof(ArtifactId))
        };
        Assert.Equal(18,supported.Count);
        int rejected=0,declared=0,interfaces=0;
        foreach(var type in s.GetType().GetInterfaces().Where(t=>t!=typeof(IDisposable)&&t!=typeof(IAsyncDisposable)))
        {
            interfaces++;Assert.DoesNotContain("Versioned",type.Name);Assert.DoesNotContain("PageRange",type.Name);
            var binding=s.GetType().GetInterfaceMap(type);
            foreach(var method in type.GetMethods())
            {
                if(method.IsSpecialName){Assert.False((bool)method.Invoke(s,null)!);continue;}
                declared++;if(supported.Contains(method))continue;
                int index=Array.IndexOf(binding.InterfaceMethods,method);Assert.Equal(s.GetType(),binding.TargetMethods[index].DeclaringType);
                object?[] args=method.GetParameters().Select(p=>p.ParameterType.IsValueType?Activator.CreateInstance(p.ParameterType):null).ToArray();
                Exception? error=null;
                try{var result=method.Invoke(s,args);if(result is Task task)await task;}
                catch(TargetInvocationException ex){error=ex.InnerException;}catch(Exception ex){error=ex;}
                Assert.IsType<NotSupportedException>(error);rejected++;
            }
        }
        Assert.Equal(16,interfaces);Assert.Equal(149,declared);Assert.Equal(131,rejected);
    }
    [Fact]
    public async Task Opaque_profiles_and_caller_mutation_do_not_change_retained_output()
    {
        var m=ReviewerAssemblyFoundationFixture.Create();m=m with{Preparation=m.Preparation with{ExtractionProfile="other",PrintProfile="other",RendererProfile="other",RegulatoryProfile="other"}};
        byte[] encoded=ReviewerAssemblyRetainedReaderFixture.Encode(m);using var c=ReviewerAssemblyRetainedReaders.FromEncoded(encoded,ReviewerAssemblyRetainedReaderFixture.Foundation,ReviewerAssemblyRetainedReaderFixture.Caps);
        Array.Fill(encoded,(byte)0);Array.Fill(m.Sources[0].Content,(byte)99);using var s=c.Acquire();
        Assert.Equal("synthetic\n",await ((IArtifactTextExtractor)s).ExtractTextAsync(ReviewerAssemblyRetainedReaderFixture.Artifact));
    }
}

using System.Runtime.InteropServices;
using System.Text;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Claims;
using EMF.Extensions.VeteransClaims.Models.Conditions;
using EMF.Extensions.VeteransClaims.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Medications;
using EMF.Extensions.VeteransClaims.Models.Clinical;
using static EMF.Extensions.VeteransClaims.Orchestration.ReviewerAssemblyRetainedReaderBudget;

namespace EMF.Extensions.VeteransClaims.Orchestration;

internal enum RetainedQuery { Package, Pending, Snapshot, Members, Issue, Claim, Artifact, Provenance, Classifications, Conditions,
    Relationships, Literature, Ledgers, Clarifications, Progression, Source, Text, Print }

internal static class ReviewerAssemblyRetainedReaderAdmission
{
    internal static readonly UTF8Encoding Utf8 = new(false, true);
    private static bool Semantic(string key) => key is "contentType" or "fileExtension" or "sourceType" or "sourceStartPage" or
        "sourceEndPage" or "sourceStartText" or "sourceEndText" or "evidenceTitle" or "noteTitle" or "evidenceDate" or "noteDate" or
        "literatureAuthors" or "literaturePublication" or "literatureDoi" or "literaturePmid" or "sourceArtifactId" or "summary";
    internal sealed record Entry(ReviewerAssemblyArtifact Artifact, ReviewerAssemblyProvenance[] Provenance,
        ReviewerAssemblyClassification[] Classifications, ReviewerAssemblySource Source);
    // One explicit stack reused for pre-count and projection; no recursion/temporary tagged root.
    internal struct Frame
    {
        internal Dictionary<string, ReviewerAssemblyValue>.Enumerator Enumerator;
        internal ReviewerAssemblyValue[]? Items;
        internal object? Target;
        internal int Index, Depth, Count, MaximumKeyLength;
        internal long SumKeyLengths;
        internal bool IsMap;
    }
    internal static void ValidateMetadata(Dictionary<string, ReviewerAssemblyValue> map, Frame[] scratch,
        ReviewerAssemblyRetainedReaderLimits limits, Meter meter, CancellationToken ct)
    {
        // Semantic restrictions apply to interpreted top-level keys only.
        meter.Tick();
        if (map.Count > limits.MaximumCollectionEntries) throw Error("MetadataCapacity");
        foreach (var pair in map)
        {
            // Conservatively purchase all fixed recognizer/boundary comparisons and a source-type scan.
            meter.Tick(checked(1L + 24L * pair.Key.Length + (pair.Value.Text?.Length ?? 0)));
            if (Semantic(pair.Key) && pair.Value.Kind != ReviewerAssemblyValueKind.String) throw Error("SemanticMetadata");
            if (pair.Key is "sourceStartPage" or "sourceEndPage" or "sourceStartText" or "sourceEndText") throw Error("SourceBoundary");
            if (pair.Key == "sourceType" && string.Equals(pair.Value.Text, "veterans-clinical-note", StringComparison.OrdinalIgnoreCase)) throw Error("ClinicalSource");
        }
        _ = Metadata(map, scratch, limits, meter, ct, limits.MaximumMetadataNodes, 2L*limits.MaximumMetadataNodes);
    }
    internal static Entry[] BuildIndex(ReviewerAssemblyCaptureManifest m, Dictionary<string, int> lookup,
        Frame[] scratch, ReviewerAssemblyFoundationLimits f, ReviewerAssemblyRetainedReaderLimits l, Meter meter, CancellationToken ct)
    {
        // Foundation already validated all receipt kinds/cardinalities/identities during the single ReadCopy.
        meter.Tick(30); // fixed root, identities, preparation and admission facts
        if (m.Package.Rows[0].SelectedBasisId is not null || m.Snapshot.Kind != ReviewerAssemblyReceiptKind.Absent ||
            m.Ledgers.Rows.Length != 0 || m.Clarifications.Rows.Length != 0 || m.Progression.Rows.Length != 0)
            throw Error("UnsupportedAdmission");
        meter.Tick(2);
        var entries = new Entry[f.MaximumMembers]; // array and dictionary construction
        for (int i = 0; i < m.Members.Rows.Length; i=checked(i+1))
        {
            ct.ThrowIfCancellationRequested(); meter.Tick(6);
            var member = m.Members.Rows[i];
            meter.Tick(checked(member.ArtifactId.Length + 128L * i));
            lookup.Add(member.ArtifactId, i);
            var artifact = Find(m.Artifacts, member.ArtifactId, meter).Rows[0];
            var provenance = Find(m.Provenance, member.ArtifactId, meter).Rows;
            var classifications = Find(m.Classifications, member.ArtifactId, meter).Rows;
            _ = Find(m.Protection, member.ArtifactId, meter);
            if (Find(m.Relationships, member.ArtifactId, meter).Rows.Length != 0 || Find(m.Literature, member.ArtifactId, meter).Rows.Length != 0)
                throw Error("UnsupportedAdmission");
            ReviewerAssemblySource? source = null;
            foreach (var row in m.Sources)
            { meter.Tick(checked(9L + row.ArtifactId.Length)); if (row.ArtifactId == member.ArtifactId) { source = row; break; } }
            if (source is null) throw Error("MissingSource");
            meter.Tick(source.Content.Length);
            try { _ = Utf8.GetCharCount(source.Content); } catch (DecoderFallbackException ex) { throw new InvalidDataException("SourceEncoding", ex); }
            ValidateMetadata(artifact.Metadata, scratch, l, meter, ct);
            foreach (var row in provenance)
            { meter.Tick(6); ValidateMetadata(row.Properties, scratch, l, meter, ct); }
            foreach (var row in classifications) meter.Tick(5);
            entries[i] = new(artifact, provenance, classifications, source); // covered by I(M)
        }
        foreach (var row in m.Conditions.Rows) meter.Tick(4);
        return entries;
    }
    private static ReviewerAssemblyReceipt<T> Find<T>(ReviewerAssemblyReceipt<T>[] rows, string id, Meter meter)
    {
        foreach (var row in rows)
        { meter.Tick(checked(4L + row.Argument.Value!.Length)); if (string.Equals(row.Argument.Value, id, StringComparison.Ordinal)) return row; }
        throw Error("MissingReceipt");
    }
    private struct MetadataCost { internal long Live, V, LengthChecks, O, Extra; internal int Nodes, Buffers; }
    private static void MetaNode(ref MetadataCost cost, int nodeLimit, long visibleLimit, Meter meter)
    {
        if (cost.Nodes >= nodeLimit) throw Error("MetadataCapacity");
        if (cost.V >= visibleLimit) throw Error("OutputCapacity");
        meter.Tick(); cost.V = checked(cost.V + 1); cost.Nodes = checked(cost.Nodes + 1);
    }
    private static Frame MetaMap(Dictionary<string, ReviewerAssemblyValue> values, int depth, ref MetadataCost cost,
        ReviewerAssemblyRetainedReaderLimits limits, Meter meter, bool countNode, int nodeLimit, long visibleLimit)
    {
        if (depth > limits.MaximumMetadataDepth) throw Error("MetadataCapacity");
        if (countNode) MetaNode(ref cost, nodeLimit, visibleLimit, meter);
        meter.Tick(); cost.LengthChecks = checked(cost.LengthChecks + 1);
        int count = values.Count;
        if (count > limits.MaximumCollectionEntries) throw Error("MetadataCapacity");
        cost.Live = checked(cost.Live + H(count)); cost.O = checked(cost.O + 1);
        return new() { IsMap = true, Enumerator = values.GetEnumerator(), Depth = depth, Count = count };
    }
    private static MetadataCost Metadata(Dictionary<string, ReviewerAssemblyValue> map, Frame[] stack,
        ReviewerAssemblyRetainedReaderLimits limits, Meter meter, CancellationToken ct, int nodeLimit, long visibleLimit)
    {
        MetadataCost cost = default;
        int top = 0; stack[0] = MetaMap(map, 1, ref cost, limits, meter, true, nodeLimit, visibleLimit);
        try
        {
            while (top >= 0)
            {
                ct.ThrowIfCancellationRequested(); ref var frame = ref stack[top]; ReviewerAssemblyValue value;
                if (frame.Index == frame.Count)
                {
                    if (frame.IsMap) cost.Extra = checked(cost.Extra + frame.SumKeyLengths +
                        frame.MaximumKeyLength * ((long)frame.Count * (frame.Count - 1) / 2));
                    stack[top] = default; top=checked(top-1); continue;
                }
                if (frame.IsMap)
                {
                    if (cost.V >= visibleLimit) throw Error("OutputCapacity");
                    meter.Tick(); // the key visit funds MoveNext, Current and bounded Length access
                    if (!frame.Enumerator.MoveNext()) throw Error("MetadataShape");
                    frame.Index=checked(frame.Index+1); var pair = frame.Enumerator.Current;
                    cost.V = checked(cost.V + 1);
                    if (pair.Key.Length > limits.MaximumMetadataKeyChars) throw Error("MetadataCapacity");
                    frame.SumKeyLengths = checked(frame.SumKeyLengths + pair.Key.Length);
                    frame.MaximumKeyLength = Math.Max(frame.MaximumKeyLength, pair.Key.Length);
                    value = pair.Value;
                }
                else { int arrayIndex=frame.Index; frame.Index=checked(frame.Index+1); value=frame.Items![arrayIndex]; }
                int depth = checked(frame.Depth + 1);
                if (depth > limits.MaximumMetadataDepth) throw Error("MetadataCapacity");
                MetaNode(ref cost, nodeLimit, visibleLimit, meter);
                switch (value.Kind)
                {
                    case ReviewerAssemblyValueKind.Null: case ReviewerAssemblyValueKind.String: break;
                    case ReviewerAssemblyValueKind.Boolean: case ReviewerAssemblyValueKind.Int64: case ReviewerAssemblyValueKind.Decimal:
                        cost.Live = checked(cost.Live + 128); cost.O = checked(cost.O + 1); break;
                    case ReviewerAssemblyValueKind.Binary:
                        meter.Tick(); cost.LengthChecks = checked(cost.LengthChecks + 1); int size = value.Binary!.Length;
                        cost.Live = checked(cost.Live + 64L + size); cost.O = checked(cost.O + 1);
                        cost.Buffers = checked(cost.Buffers + 1); cost.Extra = checked(cost.Extra + 2L * size); break;
                    case ReviewerAssemblyValueKind.Array:
                        meter.Tick(); cost.LengthChecks = checked(cost.LengthChecks + 1); int count = value.Items!.Length;
                        if (count > limits.MaximumCollectionEntries) throw Error("MetadataCapacity");
                        cost.Live = checked(cost.Live + A(count)); cost.O = checked(cost.O + 1);
                        stack[top=checked(top+1)] = new() { Items = value.Items, Depth = depth, Count = count }; break;
                    case ReviewerAssemblyValueKind.Map:
                        stack[top=checked(top+1)] = MetaMap(value.Properties!, depth, ref cost, limits, meter, false, nodeLimit, visibleLimit); break;
                    default: throw Error("MetadataKind");
                }
            }
            return cost;
        }
        // Stack lifecycle is part of each funded node; clear only occupied frames.
        finally { while (top >= 0) { stack[top] = default; top=checked(top-1); } }
    }
    private static void Nodes(long count, ReviewerAssemblyRetainedReaderLimits limits)
    { if(count>limits.MaximumOutputNodesPerQuery)throw Error("OutputCapacity"); }
    private static void Capacity(int count, ReviewerAssemblyRetainedReaderLimits limits)
    { if (count > limits.MaximumCollectionEntries || count > limits.MaximumOutputRowsPerQuery) throw Error("OutputCapacity"); }
    private static int SourceSize(Entry? e, ReviewerAssemblyRetainedReaderLimits limits)
    { int size = e!.Source.Content.Length; if (size > limits.MaximumSourceBytesPerQuery) throw Error("OutputCapacity"); return size; }
    private static void AddMeta(ref Ledger x, Dictionary<string, ReviewerAssemblyValue> values, Frame[] scratch,
        ReviewerAssemblyRetainedReaderLimits limits, Meter meter, CancellationToken ct)
    {
        var z = Metadata(values, scratch, limits, meter, ct, checked(limits.MaximumMetadataNodes-x.MetadataNodes), checked(limits.MaximumOutputNodesPerQuery-x.V));
        x.Live = checked(x.Live + z.Live); x.V = checked(x.V + z.V); x.P = checked(x.P + z.V + z.LengthChecks);
        x.O = checked(x.O + z.O); x.Extra = checked(x.Extra + z.Extra);
        x.Buffers = checked(x.Buffers + z.Buffers); x.MetadataNodes = checked(x.MetadataNodes + z.Nodes);
        if (x.MetadataNodes > limits.MaximumMetadataNodes) throw Error("MetadataCapacity");
    }
    internal static Ledger Count(RetainedQuery query, ReviewerAssemblyCaptureManifest m, Entry? e, Frame[] scratch,
        ReviewerAssemblyRetainedReaderLimits l, Meter meter, CancellationToken ct)
    {
        Ledger x = default;
        // All fixed inspections are funded before accessing any projected fields.
        switch (query)
        {
            case RetainedQuery.Package: Nodes(6,l); meter.Tick(6); x.Live=128; x.V=x.P=6; x.O=1; x.Rows=1; x.Extra=Ids(m.Package.Rows[0].Id,m.Package.Rows[0].IssueId); break;
            case RetainedQuery.Pending: Nodes(3,l); meter.Tick(3); x.Live=128; x.V=x.P=3; x.O=1; x.Rows=1; break;
            case RetainedQuery.Snapshot: meter.Tick(); x.V=x.P=1; break;
            case RetainedQuery.Issue: Nodes(4,l); meter.Tick(4); x.Live=128; x.V=x.P=4; x.O=1;x.Rows=1; x.Extra=Ids(m.Issue.Rows[0].Id,m.Issue.Rows[0].ClaimId); break;
            case RetainedQuery.Claim: Nodes(3,l); meter.Tick(3);x.Live=128;x.V=x.P=3;x.O=1;x.Rows=1;x.Extra=Ids(m.Claim.Rows[0].Id,m.Claim.Rows[0].VeteranId);break;
            case RetainedQuery.Members:
                meter.Tick(2); int n=m.Members.Rows.Length; Capacity(n,l);Nodes(checked(1+5L*n),l); x.Live=checked(A(n)+128L*n);x.V=checked(1+5L*n);x.P=x.V+1;x.O=checked(1L+n);x.Rows=n;
                foreach(var row in m.Members.Rows){meter.Tick(5);x.Extra=checked(x.Extra+Ids(row.PackageId,row.ArtifactId));} break;
            case RetainedQuery.Artifact:
                Nodes(10,l);meter.Tick(10);x.Live=384;x.V=10;x.P=10;x.O=3;x.Rows=1;x.Extra=checked(Ids(e!.Artifact.Id)+1); AddMeta(ref x,e.Artifact.Metadata,scratch,l,meter,ct);break;
            case RetainedQuery.Provenance:
                meter.Tick(2);int p=e!.Provenance.Length;Capacity(p,l);x.Live=checked(A(p)+256L*p);x.V=1;x.P=2;x.O=checked(1+2L*p);x.Rows=p;x.Extra=p;
                foreach(var row in e.Provenance){Nodes(checked(x.V+6),l);meter.Tick(6);x.V=checked(x.V+6);x.P=checked(x.P+6);x.Extra=checked(x.Extra+Ids(row.ArtifactId));AddMeta(ref x,row.Properties,scratch,l,meter,ct);}break;
            case RetainedQuery.Classifications:
                meter.Tick(2);int c=e!.Classifications.Length;Capacity(c,l);Nodes(checked(1+5L*c),l);x.Live=checked(A(c)+128L*c);x.V=1+5L*c;x.P=x.V+1;x.O=checked(1L+c);x.Rows=c;
                foreach(var row in e.Classifications){meter.Tick(5);x.Extra=checked(x.Extra+Ids(row.Id,row.ArtifactId)+(row.IssueId is null?0:Ids(row.IssueId)));}break;
            case RetainedQuery.Conditions:
                meter.Tick(2);int t=m.Conditions.Rows.Length;Capacity(t,l);Nodes(checked(1+4L*t),l);x.Live=checked(A(t)+128L*t);x.V=1+4L*t;x.P=x.V+1;x.O=checked(1L+t);x.Rows=t;
                foreach(var row in m.Conditions.Rows){meter.Tick(4);x.Extra=checked(x.Extra+Ids(row.Id,row.IssueId));}break;
            case RetainedQuery.Source:
                meter.Tick(2);int bytes=SourceSize(e,l);x.Live=checked(64L+bytes);x.V=1;x.P=2;x.O=1;x.Buffers=1;x.Extra=2L*bytes;break;
            case RetainedQuery.Text:
                meter.Tick(2);int length=SourceSize(e,l);meter.Tick(length);int chars=Utf8.GetCharCount(e!.Source.Content);
                if(chars>l.MaximumTextUtf16Chars)throw Error("OutputCapacity");x.Live=checked(64+8L*chars);x.V=1;x.P=2L+length;x.O=1;x.Extra=checked((long)length+chars);break;
            case RetainedQuery.Print:
                Nodes(8,l);meter.Tick(10);int size=SourceSize(e,l);Capacity(1,l);x.Live=checked(A(1)+128+64+size);x.V=8;x.P=10;x.O=3;x.Rows=1;x.Buffers=1;x.Extra=2L*size;break;
            case RetainedQuery.Relationships: case RetainedQuery.Literature: case RetainedQuery.Ledgers:
            case RetainedQuery.Clarifications: case RetainedQuery.Progression:
                meter.Tick(2);x.Live=A(0);x.V=1;x.P=2;x.O=1;break;
            default:throw new NotSupportedException();
        }
        if(x.MetadataNodes>l.MaximumMetadataNodes)throw Error("MetadataCapacity");
        return x;
    }
    private static long Ids(string one,string? two=null)=>checked(2L*(one.Length+(two?.Length??0)));

    internal static object? Project(RetainedQuery query,ReviewerAssemblyCaptureManifest m,Entry? e,Frame[] scratch,
        Func<byte[],byte[]> copy,CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        switch(query)
        {
            case RetainedQuery.Package: var p=m.Package.Rows[0];return new EvidencePackage{Id=new(p.Id),ClaimIssueId=new(p.IssueId),Purpose=p.Purpose,ReviewerRole=p.ReviewerRole,ServiceConnectionBasisId=null};
            case RetainedQuery.Pending:return new ReviewerPackageSnapshotRead(false,null);
            case RetainedQuery.Snapshot:return null;
            case RetainedQuery.Members:
                var members=new EvidencePackageArtifact[m.Members.Rows.Length];
                for(int i=0;i<members.Length;i=checked(i+1)){ct.ThrowIfCancellationRequested();var r=m.Members.Rows[i];members[i]=new(){EvidencePackageId=new(r.PackageId),ArtifactId=new(r.ArtifactId),ContentRole=r.ContentRole,ReviewerPageSelection=r.PageSelection};}return members;
            case RetainedQuery.Issue:var issue=m.Issue.Rows[0];return new ClaimIssue{Id=new(issue.Id),ClaimId=new(issue.ClaimId),ClaimIssueType=issue.ClaimIssueType};
            case RetainedQuery.Claim:var claim=m.Claim.Rows[0];return new Claim{Id=new(claim.Id),VeteranId=new(claim.VeteranId)};
            case RetainedQuery.Artifact:var a=e!.Artifact;return new Artifact{Id=new(a.Id),Name=a.Name,ArtifactType=a.ArtifactType,Fingerprint=new(){Algorithm=a.FingerprintAlgorithm,Value=a.Fingerprint},CreatedUtc=a.CreatedUtc,Metadata=Clone(a.Metadata,scratch,copy,ct)};
            case RetainedQuery.Provenance:
                var provenance=new Provenance[e!.Provenance.Length];for(int i=0;i<provenance.Length;i=checked(i+1)){ct.ThrowIfCancellationRequested();var r=e.Provenance[i];provenance[i]=new(){ArtifactId=new(r.ArtifactId),Source=r.Source,RecordedUtc=r.RecordedUtc,RecordedBy=r.RecordedBy,Properties=Clone(r.Properties,scratch,copy,ct)};}return provenance;
            case RetainedQuery.Classifications:
                var classifications=new EvidenceClassification[e!.Classifications.Length];for(int i=0;i<classifications.Length;i=checked(i+1)){ct.ThrowIfCancellationRequested();var r=e.Classifications[i];classifications[i]=new(){Id=new(r.Id),ArtifactId=new(r.ArtifactId),ClaimIssueId=r.IssueId is null?null:new ClaimIssueId(r.IssueId),Classification=r.Classification};}return classifications;
            case RetainedQuery.Conditions:
                var conditions=new ClaimedCondition[m.Conditions.Rows.Length];for(int i=0;i<conditions.Length;i=checked(i+1)){ct.ThrowIfCancellationRequested();var r=m.Conditions.Rows[i];conditions[i]=new(){Id=new(r.Id),ClaimIssueId=new(r.IssueId),Name=r.Name};}return conditions;
            case RetainedQuery.Relationships:return Array.Empty<Relationship>();
            case RetainedQuery.Literature:return Array.Empty<MedicalLiteratureSourceId>();
            case RetainedQuery.Ledgers:return Array.Empty<MedicationLedger>();
            case RetainedQuery.Clarifications:return Array.Empty<SourceClarification>();
            case RetainedQuery.Progression:return Array.Empty<ClinicalProgressionEvent>();
            case RetainedQuery.Source:return copy(e!.Source.Content);
            case RetainedQuery.Text:return Utf8.GetString(e!.Source.Content);
            case RetainedQuery.Print:return new PrintableArtifactPage[]{new(){PageNumber=1,ContentType="text/plain",Content=copy(e!.Source.Content),SuggestedClockwiseRotation=0,TextGeometry=null}};
            default:throw new NotSupportedException();
        }
    }
    private static Dictionary<string,object> Clone(Dictionary<string,ReviewerAssemblyValue> source,Frame[] frames,
        Func<byte[],byte[]> copy,CancellationToken ct)
    {
        var result=new Dictionary<string,object>(source.Count,StringComparer.Ordinal);
        frames[0]=new(){IsMap=true,Enumerator=source.GetEnumerator(),Target=result};int top=0;
        try
        {
        while(top>=0)
        {
            ct.ThrowIfCancellationRequested();ref var frame=ref frames[top];ReviewerAssemblyValue value;string? key=null;int index=-1;
            if(frame.IsMap){if(!frame.Enumerator.MoveNext()){frames[top]=default;top=checked(top-1);continue;}var pair=frame.Enumerator.Current;key=pair.Key;value=pair.Value;}
            else{if(frame.Index==frame.Items!.Length){frames[top]=default;top=checked(top-1);continue;}index=frame.Index;frame.Index=checked(frame.Index+1);value=frame.Items[index];}
            object? projected=value.Kind switch
            {
                ReviewerAssemblyValueKind.Null=>null,ReviewerAssemblyValueKind.String=>value.Text,
                ReviewerAssemblyValueKind.Boolean=>value.Boolean!.Value,ReviewerAssemblyValueKind.Int64=>value.Integer!.Value,
                ReviewerAssemblyValueKind.Decimal=>value.Decimal!.Value,ReviewerAssemblyValueKind.Binary=>copy(value.Binary!),
                ReviewerAssemblyValueKind.Array=>new object?[value.Items!.Length],
                ReviewerAssemblyValueKind.Map=>new Dictionary<string,object?>(value.Properties!.Count,StringComparer.Ordinal),
                _=>throw Error("MetadataKind")
            };
            if(key is not null)
            {
                // Decode before obtaining the nullable ref; no growth/recursion/await while holding it.
                var dictionary=(Dictionary<string,object>)frame.Target!;
                ref object? slot=ref CollectionsMarshal.GetValueRefOrAddDefault(dictionary,key,out _);slot=projected;
            }
            else ((object?[])frame.Target!)[index]=projected;
            if(value.Kind==ReviewerAssemblyValueKind.Array)frames[top=checked(top+1)]=new(){Items=value.Items,Target=projected};
            else if(value.Kind==ReviewerAssemblyValueKind.Map)frames[top=checked(top+1)]=new(){IsMap=true,Enumerator=value.Properties!.GetEnumerator(),Target=projected};
        }
        }
        finally { while(top>=0) { frames[top]=default;top=checked(top-1); } }
        return result;
    }
}

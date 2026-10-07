using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;
using EMF.Core.Contracts.Storage;
using EMF.Extensions.VeteransClaims.Models.Adjudication;

namespace EMF.Extensions.VeteransClaims.Orchestration;

// Fixed foundation schema only; not wired into any existing retained dispatcher.
public static class ReviewerAssemblyCaptureRepresentation
{
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly HashSet<string> LookupArrays = new(StringComparer.Ordinal)
        { "artifacts", "protection", "provenance", "relationships", "classifications", "literature", "deferred", "sources" };

    // Explicit wire field/type map. Every field is required, including nullable
    // ones. [] preserves order; {} denotes a key-sorted, unique string-key map.
    private static readonly Dictionary<string, Dictionary<string, string>> Shapes = new(StringComparer.Ordinal)
    {
        ["Root"] = Fields("identity:Identity snapshotId:s operationId:s package:PackageReceipt pending:PendingReceipt snapshot:SnapshotReceipt members:MemberReceipt issue:IssueReceipt claim:ClaimReceipt artifacts:[ArtifactReceipt] protection:[ProtectionReceipt] provenance:[ProvenanceReceipt] relationships:[RelationshipReceipt] classifications:[ClassificationReceipt] literature:[LiteratureReceipt] conditions:ConditionReceipt ledgers:LedgerReceipt clarifications:ClarificationReceipt progression:ProgressionReceipt deferred:[DeferredReceipt] sources:[Source] preparation:Preparation"),
        ["Identity"] = Fields("schema:s representationVersion:i validationVersion:i"),
        ["Argument"] = Fields("kind:s value:s?"),
        ["Package"] = Fields("id:s issueId:s purpose:s reviewerRole:s selectedBasisId:s?"),
        ["Pending"] = Fields("isLegacy:b sourceSnapshotVersion:i isSealed:b snapshot:s?"),
        ["Snapshot"] = Fields("packageId:s sha256:s"),
        ["Member"] = Fields("packageId:s artifactId:s contentRole:s pageSelection:s?"),
        ["Issue"] = Fields("id:s claimId:s claimIssueType:s"),
        ["Claim"] = Fields("id:s veteranId:s"),
        ["Artifact"] = Fields("id:s name:s artifactType:s fingerprintAlgorithm:s fingerprint:s createdUtc:s metadata:{Value}"),
        ["Protection"] = Fields("artifactId:s classificationId:s classificationRevision:s isAdopted:b"),
        ["Provenance"] = Fields("artifactId:s source:s recordedUtc:s recordedBy:s properties:{Value}"),
        ["Relationship"] = Fields("sourceArtifactId:s targetArtifactId:s relationshipType:s createdUtc:s properties:{Value}"),
        ["Classification"] = Fields("id:s artifactId:s issueId:s? classification:s"),
        ["Condition"] = Fields("id:s issueId:s name:s"),
        ["Ledger"] = Fields("id:s veteranId:s sourceArtifactId:s reportDate:s sourceStartPage:i sourceEndPage:i reportedEntryCount:i? parsedEntryCount:i isComplete:b"),
        ["Clarification"] = Fields("id:s issueId:s sourceArtifactId:s evidenceDate:s sourceStartPage:i sourceEndPage:i recordTitle:s category:s originalText:s clarification:s reviewerMatchText:s? reviewerReplacementText:s?"),
        ["Progression"] = Fields("id:s issueId:s sourceArtifactId:s eventDate:s sourceStartPage:i? sourceEndPage:i? recordTitle:s eventType:s summary:s"),
        ["DeferredReceipt"] = Fields("queryId:s argument:Argument kind:s predicate:s"),
        ["Source"] = Fields("artifactId:s physicalRevision:s contentArtifactId:s contentPhysicalRevision:s fingerprintAlgorithm:s fingerprint:s sha256:s content:sourceBytes"),
        ["Preparation"] = Fields("veteranDisplayName:s? preparer:s? preparedDate:s reviewDate:s outputFormat:s extractionProfile:s printProfile:s rendererProfile:s converterProfile:s? regulatoryProfile:s requiredCitations:[s] regulatoryMap:{s} selectedPageSequence:[i]"),
        ["Value"] = Fields("kind:s text:s? boolean:b? integer:i? decimal:d? binary:bytes? items:[Value]? properties:{Value}?")
    };

    static ReviewerAssemblyCaptureRepresentation()
    {
        foreach (var row in new[] { "Package", "Pending", "Snapshot", "Member", "Issue", "Claim", "Artifact", "Protection", "Provenance", "Relationship", "Classification", "Literature", "Condition", "Ledger", "Clarification", "Progression" })
            Shapes.Add(row + "Receipt", Fields("queryId:s argument:Argument kind:s rows:[" + (row == "Literature" ? "s" : row) + "]"));
    }

    private static Dictionary<string, string> Fields(string definition) => definition.Split(' ')
        .ToDictionary(x => x[..x.IndexOf(':')], x => x[(x.IndexOf(':') + 1)..], StringComparer.Ordinal);

    public static string Hash(ReadOnlySpan<byte> bytes) => Convert.ToHexString(SHA256.HashData(bytes));

    internal static long LogicalDecodeBytes(long encodedLength) => checked(encodedLength * 16 + 4096);
    // Preflight + bounded semantic validation, then encoding/canonical copies.
    internal static long ConstructionWork(long capacity) => checked(LogicalDecodeBytes(capacity) + 3 * capacity);

    private static void Expected(ReviewerAssemblyDraftIdentity expected)
    {
        if (expected != ReviewerAssemblyDraftIdentity.Foundation)
            throw new InvalidDataException("Unsupported expected foundation schema/representation/validation tuple.");
    }

    private static JsonSerializerOptions Options(int maximumNumericTokenBytes, OwnedBuffers? owned = null, CancellationToken ct = default)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
            MaxDepth = 128
        };
        options.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
        options.Converters.Add(new ExactDecimalConverter(maximumNumericTokenBytes));
        if (owned is not null) options.Converters.Add(new OwnedBinaryConverter(owned, ct));
        return options;
    }

    // Fixed-model preflight: no growing sets/dictionaries and no content hashing.
    // Length/count charges precede traversal. The only path storage is fixed at
    // the supported depth bound. Every occurrence (including aliases) is charged.
    private sealed class ConstructionPreflight(ReviewerAssemblyFoundationLimits limits, CancellationToken ct,
        Action<string, byte[]>? checkpoint = null)
    {
        private readonly object?[] path = new object?[128];
        private long encodedMinimum, logical = 4096;
        private void Charge(long bytes, long units)
        {
            encodedMinimum = checked(encodedMinimum + bytes);
            logical = checked(logical + units);
            Require(encodedMinimum <= limits.MaximumEncodedBytes && logical <= LogicalDecodeBytes(limits.MaximumEncodedBytes),
                "Construction preflight budget exceeded before semantic validation.");
        }
        internal void Visit(object? value, int depth = 1)
        {
            ct.ThrowIfCancellationRequested();
            checkpoint?.Invoke("PreflightVisit", Array.Empty<byte>());
            Require(depth <= limits.MaximumDepth, "Construction preflight depth exceeded.");
            if (value is null) { Charge(4, 0); return; }
            if (value is string text)
            {
                Charge(2 + (long)text.Length, checked(8L * text.Length)); // before UTF-8 scan
                Charge(Utf8.GetByteCount(text) - text.Length, 0); return;
            }
            if (value is byte[] bytes)
            { Charge(checked(2 + (bytes.LongLength + 2) / 3 * 4), checked(8 * bytes.LongLength)); return; }
            if (value is bool or int or long or decimal or DateOnly or DateTimeOffset or
                ReviewerAssemblyReceiptKind or ReviewerAssemblyArgumentKind or ReviewerAssemblyValueKind)
            { Charge(1, 16); return; }
            for (int i = 0; i < depth - 1; i++) Require(!ReferenceEquals(path[i], value), "Cyclic construction input.");
            path[depth - 1] = value;
            try
            {
                switch (value)
                {
                    case Array array:
                        // One token and one separator per slot is a lower bound.
                        Require(checked(encodedMinimum + 2L * array.LongLength + 1) <= limits.MaximumEncodedBytes,
                            "Construction array rejected before traversal.");
                        Charge(2 + Math.Max(0, array.LongLength - 1), checked(16 * array.LongLength));
                        foreach (var item in array) Visit(item, depth + 1);
                        break;
                    case Dictionary<string, ReviewerAssemblyValue> map:
                        Charge(2 + Math.Max(0, map.Count - 1), checked(96L * map.Count));
                        foreach (var entry in map) { Visit(entry.Key, depth + 1); Charge(1, 0); Visit(entry.Value, depth + 1); }
                        break;
                    case Dictionary<string, string> map:
                        Charge(2 + Math.Max(0, map.Count - 1), checked(96L * map.Count));
                        foreach (var entry in map) { Visit(entry.Key, depth + 1); Charge(1, 0); Visit(entry.Value, depth + 1); }
                        break;
                    case ReviewerAssemblyDraftIdentity row:
                        Charge(57, 128);
                        Visit(row.Schema, depth + 1);
                        Visit(row.RepresentationVersion, depth + 1);
                        Visit(row.ValidationVersion, depth + 1);
                        break;
                    case ReviewerAssemblyQueryArgument row:
                        Charge(18, 128);
                        Visit(row.Kind, depth + 1);
                        Visit(row.Value, depth + 1);
                        break;
                    case ReviewerAssemblyPackage row:
                        Charge(64, 128);
                        Visit(row.Id, depth + 1);
                        Visit(row.IssueId, depth + 1);
                        Visit(row.Purpose, depth + 1);
                        Visit(row.ReviewerRole, depth + 1);
                        Visit(row.SelectedBasisId, depth + 1);
                        break;
                    case ReviewerAssemblyPending row:
                        Charge(62, 128);
                        Visit(row.IsLegacy, depth + 1);
                        Visit(row.SourceSnapshotVersion, depth + 1);
                        Visit(row.IsSealed, depth + 1);
                        Visit(row.Snapshot, depth + 1);
                        break;
                    case ReviewerAssemblySnapshot row:
                        Charge(24, 128);
                        Visit(row.PackageId, depth + 1);
                        Visit(row.Sha256, depth + 1);
                        break;
                    case ReviewerAssemblyMember row:
                        Charge(60, 128);
                        Visit(row.PackageId, depth + 1);
                        Visit(row.ArtifactId, depth + 1);
                        Visit(row.ContentRole, depth + 1);
                        Visit(row.PageSelection, depth + 1);
                        break;
                    case ReviewerAssemblyIssue row:
                        Charge(36, 128);
                        Visit(row.Id, depth + 1);
                        Visit(row.ClaimId, depth + 1);
                        Visit(row.ClaimIssueType, depth + 1);
                        break;
                    case ReviewerAssemblyClaim row:
                        Charge(20, 128);
                        Visit(row.Id, depth + 1);
                        Visit(row.VeteranId, depth + 1);
                        break;
                    case ReviewerAssemblyArtifact row:
                        Charge(96, 128);
                        Visit(row.Id, depth + 1);
                        Visit(row.Name, depth + 1);
                        Visit(row.ArtifactType, depth + 1);
                        Visit(row.FingerprintAlgorithm, depth + 1);
                        Visit(row.Fingerprint, depth + 1);
                        Visit(row.CreatedUtc, depth + 1);
                        Visit(row.Metadata, depth + 1);
                        break;
                    case ReviewerAssemblyProtection row:
                        Charge(74, 128);
                        Visit(row.ArtifactId, depth + 1);
                        Visit(row.ClassificationId, depth + 1);
                        Visit(row.ClassificationRevision, depth + 1);
                        Visit(row.IsAdopted, depth + 1);
                        break;
                    case ReviewerAssemblyProvenance row:
                        Charge(68, 128);
                        Visit(row.ArtifactId, depth + 1);
                        Visit(row.Source, depth + 1);
                        Visit(row.RecordedUtc, depth + 1);
                        Visit(row.RecordedBy, depth + 1);
                        Visit(row.Properties, depth + 1);
                        break;
                    case ReviewerAssemblyRelationship row:
                        Charge(89, 128);
                        Visit(row.SourceArtifactId, depth + 1);
                        Visit(row.TargetArtifactId, depth + 1);
                        Visit(row.RelationshipType, depth + 1);
                        Visit(row.CreatedUtc, depth + 1);
                        Visit(row.Properties, depth + 1);
                        break;
                    case ReviewerAssemblyClassification row:
                        Charge(50, 128);
                        Visit(row.Id, depth + 1);
                        Visit(row.ArtifactId, depth + 1);
                        Visit(row.IssueId, depth + 1);
                        Visit(row.Classification, depth + 1);
                        break;
                    case ReviewerAssemblyCondition row:
                        Charge(26, 128);
                        Visit(row.Id, depth + 1);
                        Visit(row.IssueId, depth + 1);
                        Visit(row.Name, depth + 1);
                        break;
                    case ReviewerAssemblyLedger row:
                        Charge(146, 128);
                        Visit(row.Id, depth + 1);
                        Visit(row.VeteranId, depth + 1);
                        Visit(row.SourceArtifactId, depth + 1);
                        Visit(row.ReportDate, depth + 1);
                        Visit(row.SourceStartPage, depth + 1);
                        Visit(row.SourceEndPage, depth + 1);
                        Visit(row.ReportedEntryCount, depth + 1);
                        Visit(row.ParsedEntryCount, depth + 1);
                        Visit(row.IsComplete, depth + 1);
                        break;
                    case ReviewerAssemblyClarification row:
                        Charge(198, 128);
                        Visit(row.Id, depth + 1);
                        Visit(row.IssueId, depth + 1);
                        Visit(row.SourceArtifactId, depth + 1);
                        Visit(row.EvidenceDate, depth + 1);
                        Visit(row.SourceStartPage, depth + 1);
                        Visit(row.SourceEndPage, depth + 1);
                        Visit(row.RecordTitle, depth + 1);
                        Visit(row.Category, depth + 1);
                        Visit(row.OriginalText, depth + 1);
                        Visit(row.Clarification, depth + 1);
                        Visit(row.ReviewerMatchText, depth + 1);
                        Visit(row.ReviewerReplacementText, depth + 1);
                        break;
                    case ReviewerAssemblyProgression row:
                        Charge(126, 128);
                        Visit(row.Id, depth + 1);
                        Visit(row.IssueId, depth + 1);
                        Visit(row.SourceArtifactId, depth + 1);
                        Visit(row.EventDate, depth + 1);
                        Visit(row.SourceStartPage, depth + 1);
                        Visit(row.SourceEndPage, depth + 1);
                        Visit(row.RecordTitle, depth + 1);
                        Visit(row.EventType, depth + 1);
                        Visit(row.Summary, depth + 1);
                        break;
                    case ReviewerAssemblyDeferredReceipt row:
                        Charge(45, 128);
                        Visit(row.QueryId, depth + 1);
                        Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1);
                        Visit(row.Predicate, depth + 1);
                        break;
                    case ReviewerAssemblySource row:
                        Charge(143, 128);
                        Visit(row.ArtifactId, depth + 1);
                        Visit(row.PhysicalRevision, depth + 1);
                        Visit(row.ContentArtifactId, depth + 1);
                        Visit(row.ContentPhysicalRevision, depth + 1);
                        Visit(row.FingerprintAlgorithm, depth + 1);
                        Visit(row.Fingerprint, depth + 1);
                        Visit(row.Sha256, depth + 1);
                        Visit(row.Content, depth + 1);
                        break;
                    case ReviewerAssemblyPreparation row:
                        Charge(240, 128);
                        Visit(row.VeteranDisplayName, depth + 1);
                        Visit(row.Preparer, depth + 1);
                        Visit(row.PreparedDate, depth + 1);
                        Visit(row.ReviewDate, depth + 1);
                        Visit(row.OutputFormat, depth + 1);
                        Visit(row.ExtractionProfile, depth + 1);
                        Visit(row.PrintProfile, depth + 1);
                        Visit(row.RendererProfile, depth + 1);
                        Visit(row.ConverterProfile, depth + 1);
                        Visit(row.RegulatoryProfile, depth + 1);
                        Visit(row.RequiredCitations, depth + 1);
                        Visit(row.RegulatoryMap, depth + 1);
                        Visit(row.SelectedPageSequence, depth + 1);
                        break;
                    case ReviewerAssemblyValue row:
                        Charge(83, 128);
                        Visit(row.Kind, depth + 1);
                        Visit(row.Text, depth + 1);
                        Visit(row.Boolean, depth + 1);
                        Visit(row.Integer, depth + 1);
                        Visit(row.Decimal, depth + 1);
                        Visit(row.Binary, depth + 1);
                        Visit(row.Items, depth + 1);
                        Visit(row.Properties, depth + 1);
                        break;
                    case ReviewerAssemblyCaptureManifest row:
                        Charge(292, 128);
                        Visit(row.Identity, depth + 1);
                        Visit(row.SnapshotId, depth + 1);
                        Visit(row.OperationId, depth + 1);
                        Visit(row.Package, depth + 1);
                        Visit(row.Pending, depth + 1);
                        Visit(row.Snapshot, depth + 1);
                        Visit(row.Members, depth + 1);
                        Visit(row.Issue, depth + 1);
                        Visit(row.Claim, depth + 1);
                        Visit(row.Artifacts, depth + 1);
                        Visit(row.Protection, depth + 1);
                        Visit(row.Provenance, depth + 1);
                        Visit(row.Relationships, depth + 1);
                        Visit(row.Classifications, depth + 1);
                        Visit(row.Literature, depth + 1);
                        Visit(row.Conditions, depth + 1);
                        Visit(row.Ledgers, depth + 1);
                        Visit(row.Clarifications, depth + 1);
                        Visit(row.Progression, depth + 1);
                        Visit(row.Deferred, depth + 1);
                        Visit(row.Sources, depth + 1);
                        Visit(row.Preparation, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyPackage> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyPending> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblySnapshot> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyMember> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyIssue> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyClaim> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyArtifact> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyProtection> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyProvenance> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyRelationship> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyClassification> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<string> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyCondition> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyLedger> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyClarification> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    case ReviewerAssemblyReceipt<ReviewerAssemblyProgression> row:
                        Charge(40, 128);
                        Visit(row.QueryId, depth + 1); Visit(row.Argument, depth + 1);
                        Visit(row.Kind, depth + 1); Visit(row.Rows, depth + 1);
                        break;
                    default: throw new InvalidDataException("Unsupported construction model type.");
                }
            }
            finally { path[depth - 1] = null; }
        }
    }

    public static byte[] Encode(ReviewerAssemblyCaptureManifest input, ReviewerAssemblyDraftIdentity expected,
        ReviewerAssemblyFoundationLimits limits, CancellationToken ct = default)
        => EncodeCore(input, expected, limits, ct, null);

    internal static byte[] EncodeCore(ReviewerAssemblyCaptureManifest input, ReviewerAssemblyDraftIdentity expected,
        ReviewerAssemblyFoundationLimits limits, CancellationToken ct, Action<string, byte[]>? checkpoint)
    {
        Expected(expected); limits.Validate(); ct.ThrowIfCancellationRequested();
        if (LogicalDecodeBytes(limits.MaximumEncodedBytes) > limits.MaximumPrivateBytes ||
            ConstructionWork(limits.MaximumEncodedBytes) > limits.MaximumCopyWork)
            throw new InvalidDataException("Foundation construction budget exceeded.");
        checkpoint?.Invoke("PreflightStarted", Array.Empty<byte>());
        new ConstructionPreflight(limits, ct, checkpoint).Visit(input);
        checkpoint?.Invoke("SemanticValidationStarted", Array.Empty<byte>());
        ValidateModel(input, limits, ct);
        ct.ThrowIfCancellationRequested();
        using var raw = new BoundedBuffer(limits.MaximumEncodedBytes);
        JsonSerializer.Serialize(raw, input, Options(limits.MaximumValueBytes));
        ct.ThrowIfCancellationRequested();
        using var doc = Parse(raw.Memory, limits);
        CheckShape(doc.RootElement, "Root", limits, 1, ct, new ShapeBudget());
        using var canonical = new BoundedBuffer(limits.MaximumEncodedBytes);
        using (var writer = new Utf8JsonWriter(canonical, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) WriteCanonical(writer, doc.RootElement, null, ct, limits.MaximumValueBytes);
        return canonical.Copy(ct);
    }

    public static void ValidateEncoded(ReadOnlyMemory<byte> encoded, ReviewerAssemblyDraftIdentity expected,
        ReviewerAssemblyFoundationLimits limits, CancellationToken ct = default)
    {
        using var decoded = DecodeOwned(encoded, expected, limits, ct);
    }

    internal sealed class Decoded(ReviewerAssemblyCaptureManifest value, OwnedBuffers buffers) : IDisposable
    {
        internal ReviewerAssemblyCaptureManifest Value { get; } = value;
        internal OwnedBuffers Buffers { get; } = buffers;
        public void Dispose() => Buffers.Dispose();
    }

    internal static Decoded DecodeOwned(ReadOnlyMemory<byte> encoded, ReviewerAssemblyDraftIdentity expected,
        ReviewerAssemblyFoundationLimits limits, CancellationToken ct, Action<string, byte[]>? checkpoint = null)
    {
        Expected(expected); limits.Validate(); ct.ThrowIfCancellationRequested();
        if (encoded.Length == 0 || encoded.Length > limits.MaximumEncodedBytes ||
            LogicalDecodeBytes(encoded.Length) > limits.MaximumPrivateBytes || LogicalDecodeBytes(encoded.Length) > limits.MaximumCopyWork)
            throw new InvalidDataException("Foundation encoded/decode bound exceeded.");
        var owned = new OwnedBuffers(checkpoint);
        try
        {
            using var doc = Parse(encoded, limits);
            CheckShape(doc.RootElement, "Root", limits, 1, ct, new ShapeBudget());
            long returned = 0;
            foreach (var source in doc.RootElement.GetProperty("sources").EnumerateArray())
            {
                var encodedSource = source.GetProperty("content").GetString()!;
                var padding = encodedSource.EndsWith("==", StringComparison.Ordinal) ? 2 : encodedSource.EndsWith('=') ? 1 : 0;
                var length = checked((long)encodedSource.Length / 4 * 3 - padding);
                returned = checked(returned + length);
                Require(length >= 0 && length <= limits.MaximumSourceBytes && returned <= limits.MaximumAggregateSourceBytes, "Source size exceeded before decode.");
            }
            var value = JsonSerializer.Deserialize<ReviewerAssemblyCaptureManifest>(encoded.Span, Options(limits.MaximumValueBytes, owned, ct))
                ?? throw new InvalidDataException("Missing foundation root.");
            ValidateModel(value, limits, ct);
            ct.ThrowIfCancellationRequested();
            return new(value, owned);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or OverflowException or FormatException)
        {
            owned.Dispose();
            throw new InvalidDataException("Invalid foundation encoding/field/value.", ex);
        }
        catch { owned.Dispose(); throw; }
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> encoded, ReviewerAssemblyFoundationLimits limits)
    {
        try
        {
            _ = Utf8.GetCharCount(encoded.Span);
            return JsonDocument.Parse(encoded, new JsonDocumentOptions { MaxDepth = limits.MaximumDepth });
        }
        catch (Exception ex) when (ex is JsonException or DecoderFallbackException)
        { throw new InvalidDataException("Invalid UTF-8 JSON or depth.", ex); }
    }

    private sealed class ShapeBudget { internal int Receipts, Rows, Values; }

    // JSON syntax is checked by the reader first. This scanner uses fixed-width
    // arithmetic only: no powers of ten, BigInteger or coefficient-sized storage.
    internal static decimal ExactDecimal(ReadOnlySpan<byte> token, int maximumTokenBytes)
    {
        Require(token.Length > 0 && token.Length <= maximumTokenBytes, "Decimal token bound exceeded.");
        int index = 0, digits = 0, fractional = 0, first = -1, last = -1;
        bool negative = token.Length > 0 && token[0] == '-';
        if (negative) index++;
        bool fraction = false;
        int coefficientEnd = index;
        for (; index < token.Length && token[index] != 'e' && token[index] != 'E'; index++)
        {
            byte c = token[index];
            if (c == '.') { fraction = true; continue; }
            Require(c >= '0' && c <= '9', "Invalid decimal coefficient.");
            if (c != '0') { if (first < 0) first = digits; last = digits; }
            digits++; if (fraction) fractional++;
        }
        coefficientEnd = index;
        long exponent = 0;
        if (index < token.Length)
        {
            index++;
            bool exponentNegative = index < token.Length && token[index] == '-';
            if (index < token.Length && (token[index] == '+' || token[index] == '-')) index++;
            long saturation = (long)token.Length + 64;
            for (; index < token.Length; index++)
            {
                Require(token[index] >= '0' && token[index] <= '9', "Invalid decimal exponent.");
                exponent = Math.Min(saturation, exponent * 10 + token[index] - '0');
            }
            if (exponentNegative) exponent = -exponent;
        }
        if (first < 0) return 0m; // Every exact zero spelling, including signed zero.
        long scale = fractional - exponent - (digits - last - 1L);
        int significant = last - first + 1;
        Require(scale <= 28 && significant <= 29 && significant + Math.Max(0, -scale) <= 29,
            "Decimal value is not exactly representable.");
        UInt128 coefficient = 0;
        int ordinal = 0;
        for (index = negative ? 1 : 0; index < coefficientEnd; index++)
        {
            byte c = token[index]; if (c == '.') continue;
            if (ordinal >= first && ordinal <= last) coefficient = coefficient * 10 + (uint)(c - '0');
            ordinal++;
        }
        for (; scale < 0; scale++) coefficient *= 10; // At most 28 iterations.
        Require(coefficient <= (((UInt128)1 << 96) - 1), "Decimal coefficient overflow.");
        return new decimal(unchecked((int)(uint)coefficient), unchecked((int)(uint)(coefficient >> 32)),
            unchecked((int)(uint)(coefficient >> 64)), negative, (byte)scale);
    }

    private sealed class ExactDecimalConverter(int maximumTokenBytes) : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            Require(reader.TokenType == JsonTokenType.Number && !reader.HasValueSequence, "Expected contiguous decimal token.");
            return ExactDecimal(reader.ValueSpan, maximumTokenBytes);
        }
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteRawValue(value == 0 ? "0" : value.ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
    }

    private static void CheckShape(JsonElement value, string shape, ReviewerAssemblyFoundationLimits limits, int depth, CancellationToken ct, ShapeBudget budget)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > limits.MaximumDepth) throw new InvalidDataException("Foundation depth exceeded.");
        if (shape.EndsWith('?'))
        {
            if (value.ValueKind == JsonValueKind.Null) return;
            shape = shape[..^1];
        }
        if (shape is "s" or "bytes" or "sourceBytes")
        {
            var maximum = shape == "sourceBytes" ? limits.MaximumEncodedBytes :
                shape == "bytes" ? checked(((long)limits.MaximumValueBytes + 2) / 3 * 4) : limits.MaximumValueBytes;
            if (value.ValueKind != JsonValueKind.String || value.GetRawText().Length > checked(maximum * 6 + 2))
                throw new InvalidDataException("Invalid or excessive scalar.");
            var text = value.GetString()!;
            if (Utf8.GetByteCount(text) > maximum) throw new InvalidDataException("Scalar size exceeded.");
            if (shape == "bytes")
            {
                var padding = text.EndsWith("==", StringComparison.Ordinal) ? 2 : text.EndsWith('=') ? 1 : 0;
                Require(text.Length % 4 == 0 && checked((long)text.Length / 4 * 3 - padding) <= limits.MaximumValueBytes,
                    "Metadata binary size exceeded before allocation.");
            }
            return;
        }
        if (shape == "b") { if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False)) throw new InvalidDataException("Expected Boolean."); return; }
        if (shape is "i" or "d")
        {
            if (value.ValueKind != JsonValueKind.Number || (shape == "i" && !value.TryGetInt64(out _)))
                throw new InvalidDataException("Invalid numeric scalar.");
            var token = value.GetRawText();
            Require(token.Length <= limits.MaximumValueBytes, "Numeric token bound exceeded.");
            if (shape == "d") _ = ExactDecimal(Encoding.UTF8.GetBytes(token), limits.MaximumValueBytes);
            return;
        }
        if (shape.StartsWith('['))
        {
            if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() > limits.MaximumRows)
                throw new InvalidDataException("Invalid/excessive array.");
            foreach (var item in value.EnumerateArray()) CheckShape(item, shape[1..^1], limits, depth + 1, ct, budget);
            return;
        }
        if (value.ValueKind != JsonValueKind.Object) throw new InvalidDataException("Expected explicit object.");
        if (shape.EndsWith("Receipt", StringComparison.Ordinal))
        {
            budget.Receipts = checked(budget.Receipts + 1);
            if (shape != "DeferredReceipt" && value.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
                budget.Rows = checked(budget.Rows + rows.GetArrayLength());
            Require(budget.Receipts <= limits.MaximumReceipts && budget.Rows <= limits.MaximumRows, "Aggregate receipt/row count exceeded before decode.");
            if (shape == "MemberReceipt" && value.TryGetProperty("rows", out var members) && members.ValueKind == JsonValueKind.Array)
                Require(members.GetArrayLength() <= limits.MaximumMembers, "Member count exceeded before decode.");
        }
        if (shape == "Value") { budget.Values = checked(budget.Values + 1); Require(budget.Values <= limits.MaximumRows, "Metadata value count exceeded before decode."); }
        var names = new HashSet<string>(StringComparer.Ordinal);
        var map = shape.StartsWith('{') ? null : Shapes[shape];
        foreach (var property in value.EnumerateObject())
        {
            if (!names.Add(property.Name) || Utf8.GetByteCount(property.Name) > limits.MaximumValueBytes || (map is null && names.Count > limits.MaximumRows))
                throw new InvalidDataException("Duplicate/excessive map or field.");
            if (map is not null && !map.ContainsKey(property.Name)) throw new InvalidDataException("Unknown field.");
            CheckShape(property.Value, map is null ? shape[1..^1] : map[property.Name], limits, depth + 1, ct, budget);
        }
        if (map is not null && names.Count != map.Count) throw new InvalidDataException("Missing required field, including explicit null.");
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value, string? field, CancellationToken ct, int maximumNumericTokenBytes)
    {
        ct.ThrowIfCancellationRequested();
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var p in value.EnumerateObject().OrderBy(x => x.Name, StringComparer.Ordinal))
                { writer.WritePropertyName(p.Name); WriteCanonical(writer, p.Value, p.Name, ct, maximumNumericTokenBytes); }
                writer.WriteEndObject(); break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                IEnumerable<JsonElement> items = value.EnumerateArray();
                if (field is not null && LookupArrays.Contains(field))
                    items = items.OrderBy(x => x.TryGetProperty("artifactId", out var id) ? id.GetString() :
                        x.GetProperty("queryId").GetString() + ":" + x.GetProperty("argument").GetProperty("value").GetString(), StringComparer.Ordinal);
                foreach (var item in items) WriteCanonical(writer, item, null, ct, maximumNumericTokenBytes);
                writer.WriteEndArray(); break;
            case JsonValueKind.Number:
                if (value.TryGetInt64(out var integer)) writer.WriteNumberValue(integer);
                else writer.WriteRawValue(ExactDecimal(Encoding.UTF8.GetBytes(value.GetRawText()), maximumNumericTokenBytes).ToString("G29", System.Globalization.CultureInfo.InvariantCulture));
                break;
            case JsonValueKind.String: writer.WriteStringValue(value.GetString()); break;
            default: value.WriteTo(writer); break;
        }
    }

    private static void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition, string message)
    { if (!condition) throw new InvalidDataException(message); }

    private static void Id(string id) { try { ArtifactContentIdentity.Validate(id); } catch (ArgumentException ex) { throw new InvalidDataException("Invalid identity.", ex); } }

    private static void Text(string? text, ReviewerAssemblyFoundationLimits limits, bool optional = false)
    {
        Require(optional || !string.IsNullOrWhiteSpace(text), "Required text missing.");
        if (text is not null) Require(Utf8.GetByteCount(text) <= Math.Min(limits.MaximumValueBytes, limits.MaximumEncodedBytes), "Text bound exceeded.");
    }

    private sealed class ModelBudget(ReviewerAssemblyFoundationLimits limits, CancellationToken ct)
    {
        internal int Receipts, Rows, Values;
        private readonly HashSet<object> path = new(ReferenceEqualityComparer.Instance);
        internal void Count(int rows)
        {
            ct.ThrowIfCancellationRequested();
            Receipts = checked(Receipts + 1); Rows = checked(Rows + rows);
            Require(Receipts <= limits.MaximumReceipts && Rows <= limits.MaximumRows, "Receipt/row count exceeded.");
        }
        internal void Map(Dictionary<string, ReviewerAssemblyValue> map, int depth = 1)
        {
            Require(map is not null && map.Count <= limits.MaximumRows, "Missing/excessive metadata map.");
            Require(path.Add(map), "Cyclic metadata.");
            try { foreach (var (key, value) in map) { Text(key, limits); Value(value, depth); } }
            finally { path.Remove(map); }
        }
        private void Value(ReviewerAssemblyValue v, int depth)
        {
            ct.ThrowIfCancellationRequested();
            Values = checked(Values + 1);
            Require(v is not null && depth <= limits.MaximumDepth && Values <= limits.MaximumRows, "Metadata depth/value count exceeded.");
            int populated = (v.Text is null ? 0 : 1) + (v.Boolean is null ? 0 : 1) + (v.Integer is null ? 0 : 1) +
                (v.Decimal is null ? 0 : 1) + (v.Binary is null ? 0 : 1) + (v.Items is null ? 0 : 1) + (v.Properties is null ? 0 : 1);
            Require(populated == (v.Kind == ReviewerAssemblyValueKind.Null ? 0 : 1), "Contradictory typed metadata.");
            switch (v.Kind)
            {
                case ReviewerAssemblyValueKind.Null: break;
                case ReviewerAssemblyValueKind.String: Require(v.Text is not null, "String kind mismatch."); Text(v.Text, limits, true); break;
                case ReviewerAssemblyValueKind.Boolean: Require(v.Boolean.HasValue, "Boolean kind mismatch."); break;
                case ReviewerAssemblyValueKind.Int64: Require(v.Integer.HasValue, "Integer kind mismatch."); break;
                case ReviewerAssemblyValueKind.Decimal: Require(v.Decimal.HasValue, "Decimal kind mismatch."); break;
                case ReviewerAssemblyValueKind.Binary: Require(v.Binary is not null && v.Binary.Length <= Math.Min(limits.MaximumValueBytes, limits.MaximumEncodedBytes / 4 * 3), "Binary bound/kind mismatch."); break;
                case ReviewerAssemblyValueKind.Array:
                    Require(v.Items is not null && v.Items.Length <= limits.MaximumRows && path.Add(v.Items), "Array kind/count/cycle mismatch.");
                    try { foreach (var item in v.Items) Value(item, depth + 1); } finally { path.Remove(v.Items); }
                    break;
                case ReviewerAssemblyValueKind.Map: Require(v.Properties is not null, "Map kind mismatch."); Map(v.Properties, depth + 1); break;
                default: throw new InvalidDataException("Unsupported metadata kind.");
            }
        }
    }

    private static T[] Receipt<T>(ReviewerAssemblyReceipt<T> r, string query, ReviewerAssemblyArgumentKind kind,
        string? id, ModelBudget budget, bool single = false, bool absent = false, bool emptyOnly = false)
    {
        Require(r is not null && r.Argument is not null && r.Rows is not null && r.QueryId == query &&
            r.Argument.Kind == kind && r.Argument.Value == id, "Missing receipt, unknown query, wrong typed argument.");
        budget.Count(r.Rows.Length);
        Require(!r.Rows.Any(x => x is null), "Null row.");
        Require(absent ? r.Kind == ReviewerAssemblyReceiptKind.Absent && r.Rows.Length == 0 :
            single ? r.Kind == ReviewerAssemblyReceiptKind.Present && r.Rows.Length == 1 :
            r.Rows.Length == 0 ? r.Kind == ReviewerAssemblyReceiptKind.Empty : r.Kind == ReviewerAssemblyReceiptKind.Present,
            "Receipt kind/cardinality mismatch.");
        Require(!emptyOnly || r.Rows.Length == 0, "Unsupported nonempty branch.");
        return r.Rows;
    }

    private static Dictionary<string, ReviewerAssemblyReceipt<T>> MemberReceipts<T>(ReviewerAssemblyReceipt<T>[] receipts,
        string query, HashSet<string> ids, ModelBudget budget, bool single = false, bool emptyOnly = false)
    {
        Require(receipts is not null && receipts.Length == ids.Count, "Missing member query closure.");
        var result = new Dictionary<string, ReviewerAssemblyReceipt<T>>(StringComparer.Ordinal);
        foreach (var r in receipts)
        {
            Require(r is not null && r.Argument is not null && r.Argument.Value is not null && ids.Contains(r.Argument.Value), "Unknown member argument.");
            Require(result.TryAdd(r.Argument.Value, r), "Duplicate query.");
            Receipt(r, query, ReviewerAssemblyArgumentKind.Artifact, r.Argument.Value, budget, single: single, emptyOnly: emptyOnly);
        }
        return result;
    }

    internal static void ValidateModel(ReviewerAssemblyCaptureManifest m, ReviewerAssemblyFoundationLimits limits, CancellationToken ct)
    {
        Require(m is not null && m.Identity == ReviewerAssemblyDraftIdentity.Foundation, "Draft tuple mismatch.");
        Id(m.SnapshotId); Id(m.OperationId);
        var budget = new ModelBudget(limits, ct);
        Require(m.Package is not null && m.Package.Argument is not null && m.Package.Argument.Value is not null, "Missing package.");
        var package = Receipt(m.Package, "package", ReviewerAssemblyArgumentKind.Package, m.Package.Argument.Value, budget, single: true)[0];
        Id(package.Id); Id(package.IssueId); Text(package.Purpose, limits); Text(package.ReviewerRole, limits);
        Require(package.Id == m.Package.Argument.Value && package.SelectedBasisId is null, "Package/basis binding mismatch.");
        var pending = Receipt(m.Pending, "pending", ReviewerAssemblyArgumentKind.Package, package.Id, budget, single: true)[0];
        Require(!pending.IsLegacy && !pending.IsSealed && pending.SourceSnapshotVersion == 1 && pending.Snapshot is null, "Only explicit pending eligibility admitted.");
        Receipt(m.Snapshot, "snapshot", ReviewerAssemblyArgumentKind.Package, package.Id, budget, absent: true);
        var members = Receipt(m.Members, "members", ReviewerAssemblyArgumentKind.Package, package.Id, budget);
        Require(members.Length > 0 && members.Length <= limits.MaximumMembers, "Member bound exceeded or empty package.");
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var member in members)
        {
            Id(member.ArtifactId);
            Require(member.PackageId == package.Id && ids.Add(member.ArtifactId) &&
                member.ContentRole is EvidencePackageContentRoles.UnderlyingEvidence or EvidencePackageContentRoles.GeneratedOrganizationalMaterial,
                "Member identity/role mismatch.");
            Require(member.PageSelection is null || (member.ContentRole == EvidencePackageContentRoles.UnderlyingEvidence && member.PageSelection == "1"), "Unsupported page selection.");
        }
        var issue = Receipt(m.Issue, "issue", ReviewerAssemblyArgumentKind.Issue, package.IssueId, budget, single: true)[0];
        Require(issue.Id == package.IssueId, "Issue binding mismatch."); Id(issue.Id); Id(issue.ClaimId); Text(issue.ClaimIssueType, limits);
        var claim = Receipt(m.Claim, "claim", ReviewerAssemblyArgumentKind.Claim, issue.ClaimId, budget, single: true)[0];
        Require(claim.Id == issue.ClaimId, "Claim binding mismatch."); Id(claim.Id); Id(claim.VeteranId);
        var artifacts = MemberReceipts(m.Artifacts, "artifact", ids, budget, single: true);
        var protection = MemberReceipts(m.Protection, "protection", ids, budget, single: true);
        var provenance = MemberReceipts(m.Provenance, "provenance", ids, budget);
        MemberReceipts(m.Relationships, "relationships", ids, budget, emptyOnly: true);
        var classifications = MemberReceipts(m.Classifications, "classifications", ids, budget);
        MemberReceipts(m.Literature, "literature", ids, budget, emptyOnly: true);
        Require(m.Sources is not null && m.Sources.Length == ids.Count, "Missing source closure.");
        long sourceBytes = 0, encodedSourceBytes = 0;
        var sourceIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in m.Sources)
        {
            ct.ThrowIfCancellationRequested();
            Require(source is not null && ids.Contains(source.ArtifactId) && sourceIds.Add(source.ArtifactId) && source.Content is not null, "Source identity/duplicate mismatch.");
            Id(source.PhysicalRevision); Id(source.ContentPhysicalRevision);
            sourceBytes = checked(sourceBytes + source.Content.LongLength);
            encodedSourceBytes = checked(encodedSourceBytes + (source.Content.LongLength + 2) / 3 * 4);
            Require(source.Content.Length <= limits.MaximumSourceBytes && sourceBytes <= limits.MaximumAggregateSourceBytes, "Source byte bound exceeded.");
            Require(encodedSourceBytes <= limits.MaximumEncodedBytes, "Source encoding bound exceeded before serialization.");
            Require(source.ContentArtifactId == source.ArtifactId && source.ContentPhysicalRevision == source.PhysicalRevision &&
                source.FingerprintAlgorithm == "SHA-256" && source.Fingerprint == source.Sha256 && Hash(source.Content) == source.Sha256,
                "Source physical revision/fingerprint/hash association mismatch.");
            try { _ = Utf8.GetCharCount(source.Content); } catch (DecoderFallbackException ex) { throw new InvalidDataException("Unsupported source encoding.", ex); }
            var artifact = artifacts[source.ArtifactId].Rows[0];
            Require(artifact.Id == source.ArtifactId && artifact.ArtifactType == "text/plain" && artifact.FingerprintAlgorithm == "SHA-256" &&
                artifact.Fingerprint == source.Sha256 && artifact.CreatedUtc != default && artifact.Name.EndsWith(".txt", StringComparison.OrdinalIgnoreCase), "Unsupported Artifact/source binding.");
            Text(artifact.Name, limits); budget.Map(artifact.Metadata);
            if (artifact.Metadata.TryGetValue(EMF.Core.Models.ArtifactMetadataKeys.ContentType, out var media))
                Require(media.Kind == ReviewerAssemblyValueKind.String && media.Text == "text/plain", "Unsupported metadata media.");
            if (artifact.Metadata.TryGetValue(EMF.Core.Models.ArtifactMetadataKeys.FileExtension, out var extension))
                Require(extension.Kind == ReviewerAssemblyValueKind.String && extension.Text == ".txt", "Unsupported metadata extension.");
            var auth = protection[source.ArtifactId].Rows[0];
            Require(auth.ArtifactId == source.ArtifactId && auth.IsAdopted, "Protection/adoption binding mismatch."); Id(auth.ClassificationId); Id(auth.ClassificationRevision);
            foreach (var row in provenance[source.ArtifactId].Rows)
            { Require(row.ArtifactId == source.ArtifactId && row.RecordedUtc != default, "Provenance binding mismatch."); Text(row.Source, limits); Text(row.RecordedBy, limits); budget.Map(row.Properties); }
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in classifications[source.ArtifactId].Rows)
            {
                Id(row.Id); Text(row.Classification, limits);
                Require(seen.Add(row.Id) && row.ArtifactId == source.ArtifactId && (row.IssueId is null || row.IssueId == issue.Id) &&
                    !row.Classification.Contains("literature", StringComparison.OrdinalIgnoreCase), "Classification binding/unsupported literature.");
            }
        }
        var conditions = Receipt(m.Conditions, "conditions", ReviewerAssemblyArgumentKind.Issue, issue.Id, budget);
        var conditionIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var condition in conditions)
        { Id(condition.Id); Text(condition.Name, limits); Require(condition.IssueId == issue.Id && conditionIds.Add(condition.Id), "Condition binding mismatch."); }
        Receipt(m.Ledgers, "ledgers", ReviewerAssemblyArgumentKind.Veteran, claim.VeteranId, budget, emptyOnly: true);
        Receipt(m.Clarifications, "clarifications", ReviewerAssemblyArgumentKind.Issue, issue.Id, budget, emptyOnly: true);
        Receipt(m.Progression, "progression", ReviewerAssemblyArgumentKind.Issue, issue.Id, budget, emptyOnly: true);
        var deferred = ReviewerAssemblyDraftFields.Queries.Where(x => x.Deferred).ToArray();
        Require(m.Deferred is not null && m.Deferred.Length == deferred.Length, "Missing deferred predicates.");
        var deferredIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var r in m.Deferred)
        {
            Require(r is not null && deferredIds.Add(r.QueryId), "Duplicate deferred query.");
            var field = deferred.SingleOrDefault(x => x.Id == r.QueryId);
            Require(field is not null && r.Argument is not null && r.Argument.Kind == ReviewerAssemblyArgumentKind.None && r.Argument.Value is null &&
                r.Kind == ReviewerAssemblyReceiptKind.NotInvoked && r.Predicate == field.Result, "Invalid NotInvoked query/predicate.");
            budget.Count(0);
        }
        var p = m.Preparation;
        Require(p is not null && p.PreparedDate != default && p.ReviewDate != default && p.OutputFormat == "Docx" && p.ConverterProfile is null &&
            p.RequiredCitations is not null && p.RequiredCitations.Length == 0 && p.RegulatoryMap is not null && p.RegulatoryMap.Count == 0 &&
            p.SelectedPageSequence is not null && (p.SelectedPageSequence.Length == 0 || p.SelectedPageSequence.SequenceEqual(new[] { 1 })), "Unsupported/missing preparation facts.");
        Text(p.VeteranDisplayName, limits, true); Text(p.Preparer, limits, true);
        Text(p.ExtractionProfile, limits); Text(p.PrintProfile, limits); Text(p.RendererProfile, limits); Text(p.RegulatoryProfile, limits);
    }

    // Foundation-only existence gate, not a repository replay API. NotInvoked is
    // never a captured result; no method here knows how to query a live provider.
    public static void RequireCapturedResult(ReviewerAssemblyCaptureManifest m, string queryId,
        ReviewerAssemblyQueryArgument argument, ReviewerAssemblyFoundationLimits limits, CancellationToken ct = default)
    {
        limits.Validate(); ct.ThrowIfCancellationRequested();
        Require(LogicalDecodeBytes(limits.MaximumEncodedBytes) <= limits.MaximumPrivateBytes &&
            ConstructionWork(limits.MaximumEncodedBytes) <= limits.MaximumCopyWork, "Construction budget exceeded.");
        new ConstructionPreflight(limits, ct).Visit(m);
        ValidateModel(m, limits, ct);
        var field = ReviewerAssemblyDraftFields.Queries.SingleOrDefault(x => x.Id == queryId);
        Require(field is not null && !field.Deferred && argument is not null && argument.Kind == field.ArgumentKind,
            "Unknown/NotInvoked query has no captured result.");
        var expected = argument.Kind switch
        {
            ReviewerAssemblyArgumentKind.Package => m.Package.Rows[0].Id,
            ReviewerAssemblyArgumentKind.Issue => m.Issue.Rows[0].Id,
            ReviewerAssemblyArgumentKind.Claim => m.Claim.Rows[0].Id,
            ReviewerAssemblyArgumentKind.Veteran => m.Claim.Rows[0].VeteranId,
            ReviewerAssemblyArgumentKind.Artifact => argument.Value is not null && m.Members.Rows.Any(x => x.ArtifactId == argument.Value) ? argument.Value : null,
            _ => null
        };
        Require(expected is not null && argument.Value == expected, "Uncaptured typed query argument.");
    }

    internal sealed class OwnedBuffers(Action<string, byte[]>? checkpoint = null) : IDisposable
    {
        private readonly HashSet<byte[]> buffers = new(ReferenceEqualityComparer.Instance);
        internal void Add(byte[] bytes)
        {
            buffers.Add(bytes); // Register before observers/cancellation/decode.
            checkpoint?.Invoke("Allocated", bytes);
        }
        public void Dispose() { foreach (var buffer in buffers) CryptographicOperations.ZeroMemory(buffer); buffers.Clear(); }
    }

    private sealed class OwnedBinaryConverter(OwnedBuffers owner, CancellationToken ct) : JsonConverter<byte[]>
    {
        public override byte[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            ct.ThrowIfCancellationRequested();
            var text = reader.GetString() ?? throw new InvalidDataException("Missing binary.");
            Require(text.Length % 4 == 0, "Invalid base64.");
            var padding = text.EndsWith("==", StringComparison.Ordinal) ? 2 : text.EndsWith('=') ? 1 : 0;
            var bytes = new byte[checked(text.Length / 4 * 3 - padding)];
            owner.Add(bytes); ct.ThrowIfCancellationRequested();
            Require(Convert.TryFromBase64String(text, bytes, out var count) && count == bytes.Length, "Invalid base64.");
            return bytes;
        }
        public override void Write(Utf8JsonWriter writer, byte[] value, JsonSerializerOptions options) => writer.WriteBase64StringValue(value);
    }

    private sealed class BoundedBuffer(int capacity) : Stream
    {
        private readonly byte[] buffer = new byte[capacity];
        private int length;
        internal ReadOnlyMemory<byte> Memory => buffer.AsMemory(0, length);
        internal byte[] Copy(CancellationToken ct) { ct.ThrowIfCancellationRequested(); return Memory.ToArray(); }
        public override void Write(byte[] source, int offset, int count) => Write(source.AsSpan(offset, count));
        public override void Write(ReadOnlySpan<byte> source)
        {
            if (source.Length > buffer.Length - length) throw new InvalidDataException("Encoded size exceeded.");
            source.CopyTo(buffer.AsSpan(length)); length += source.Length;
        }
        protected override void Dispose(bool disposing) { CryptographicOperations.ZeroMemory(buffer); base.Dispose(disposing); }
        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => length;
        public override long Position { get => length; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override int Read(byte[] b, int o, int c) => throw new NotSupportedException();
        public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
    }
}

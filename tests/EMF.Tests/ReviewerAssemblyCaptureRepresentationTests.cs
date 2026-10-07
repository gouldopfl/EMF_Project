using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using Xunit;

namespace EMF.Tests;

public sealed class ReviewerAssemblyCaptureRepresentationTests
{
    [Fact]
    public void Construction_preflight_rejects_large_legal_row_and_node_counts_before_validation()
    {
        int size = Encoding.UTF8.GetByteCount(ReviewerAssemblyFoundationFixture.Golden);
        var limits = ReviewerAssemblyFoundationFixture.Limits with
        {
            MaximumEncodedBytes = size, MaximumPrivateBytes = 93872,
            MaximumCopyWork = 110705, MaximumRows = 100100
        };
        var input = ReviewerAssemblyFoundationFixture.Create();
        var caller = input.Sources[0].Content; var original = caller.ToArray();
        var rows = Enumerable.Range(0, 100000).Select(i => new ReviewerAssemblyCondition("condition-" + i, "issue-1", "Condition")).ToArray();
        input = input with { Conditions = input.Conditions with { Rows = rows } };
        var phases = new List<string>(); int visits = 0;
        void Observe(string phase, byte[] _)
        {
            if (phase == "PreflightVisit") visits++; else phases.Add(phase);
        }
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyCaptureRepresentation.EncodeCore(input,
            ReviewerAssemblyDraftIdentity.Foundation, limits, default, Observe));
        Assert.Equal(new[] { "PreflightStarted" }, phases);
        Assert.InRange(visits, 1, 1000); // 100,000 legal rows were never traversed.
        phases.Clear();
        // The original review budget now rejects even before construction begins.
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyInputOwner.CreateObserved(input, limits, default,
            Observe));
        Assert.Empty(phases);
        limits = limits with { MaximumCopyWork = ReviewerAssemblyCaptureRepresentation.ConstructionWork(size) + ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(size) };
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyInputOwner.CreateObserved(input, limits, default,
            Observe));
        Assert.Equal(new[] { "PreflightStarted" }, phases);
        var nodes = ReviewerAssemblyFoundationFixture.Create();
        nodes.Artifacts[0].Rows[0].Metadata.Clear();
        foreach (int i in Enumerable.Range(0, 10000)) nodes.Artifacts[0].Rows[0].Metadata.Add("key-" + i, ReviewerAssemblyValue.String("value"));
        phases.Clear(); visits = 0;
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyCaptureRepresentation.EncodeCore(nodes,
            ReviewerAssemblyDraftIdentity.Foundation, limits, default, Observe));
        Assert.Equal(new[] { "PreflightStarted" }, phases);
        Assert.InRange(visits, 1, 1000); // Map count is charged before visiting its entries.
        Assert.Equal(original, caller);
    }

    [Fact]
    public void Construction_capacity_and_work_boundaries_are_explicit()
    {
        int size = Encoding.UTF8.GetByteCount(ReviewerAssemblyFoundationFixture.Golden);
        long reserve = ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(size);
        long work = ReviewerAssemblyCaptureRepresentation.ConstructionWork(size);
        var limits = ReviewerAssemblyFoundationFixture.Limits with { MaximumEncodedBytes = size, MaximumPrivateBytes = reserve, MaximumCopyWork = work };
        Assert.Equal(ReviewerAssemblyFoundationFixture.Golden, Encoding.UTF8.GetString(Encode(ReviewerAssemblyFoundationFixture.Create(), limits)));
        Assert.Throws<InvalidDataException>(() => Encode(ReviewerAssemblyFoundationFixture.Create(), limits with { MaximumCopyWork = work - 1 }));
        Assert.Throws<InvalidDataException>(() => Encode(ReviewerAssemblyFoundationFixture.Create(), limits with { MaximumPrivateBytes = reserve - 1 }));
        using var owner = ReviewerAssemblyInputOwner.Create(ReviewerAssemblyFoundationFixture.Create(), ReviewerAssemblyDraftIdentity.Foundation,
            limits with { MaximumCopyWork = work + reserve });
        Assert.Equal(work + reserve, owner.Accounting.CopyWork);
        Assert.Equal(ReviewerAssemblyFoundationFixture.GoldenSha256, owner.CanonicalSha256);
    }

    [Theory]
    [InlineData("1e-100")]
    [InlineData("-1e-100")]
    [InlineData("1.00000000000000000000000000001")]
    [InlineData("0.123456789012345678901234567891")]
    [InlineData("79228162514264337593543950336")]
    [InlineData("1e1000000000000000000000000")]
    public void Encoded_decimal_rejects_underflow_precision_loss_and_overflow(string token)
    {
        var bytes = Encoding.UTF8.GetBytes(ReviewerAssemblyFoundationFixture.Golden.Replace("\"decimal\":1.25", "\"decimal\":" + token, StringComparison.Ordinal));
        var before = bytes.ToArray();
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyCaptureRepresentation.ValidateEncoded(bytes, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits));
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyInputOwner.FromEncoded(bytes, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits));
        Assert.Equal(before, bytes);
    }

    [Fact]
    public void Decimal_token_processing_obeys_the_scalar_limit_even_for_exact_zero()
    {
        var limits = ReviewerAssemblyFoundationFixture.Limits with { MaximumValueBytes = 64 };
        var bytes = Encoding.UTF8.GetBytes(ReviewerAssemblyFoundationFixture.Golden.Replace("\"decimal\":1.25",
            "\"decimal\":0e" + new string('0', 63), StringComparison.Ordinal));
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyCaptureRepresentation.ValidateEncoded(bytes, ReviewerAssemblyDraftIdentity.Foundation, limits));
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyInputOwner.FromEncoded(bytes, ReviewerAssemblyDraftIdentity.Foundation, limits));
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyCaptureRepresentation.ExactDecimal(Encoding.UTF8.GetBytes("0e" + new string('0', 63)), 64));
    }

    [Theory]
    [InlineData("1.25", "1.25")]
    [InlineData("1.2500", "1.25")]
    [InlineData("125e-2", "1.25")]
    [InlineData("79228162514264337593543950335", "79228162514264337593543950335")]
    [InlineData("-79228162514264337593543950335", "-79228162514264337593543950335")]
    [InlineData("0.0000000000000000000000000001", "1E-28")]
    [InlineData("0", "0")]
    [InlineData("-0", "0")]
    [InlineData("-0.000e+100000000000000000000", "0")]
    public void Exact_decimal_spellings_keep_the_value_and_declared_canonical_form(string token, string canonical)
    {
        var bytes = Encoding.UTF8.GetBytes(ReviewerAssemblyFoundationFixture.Golden.Replace("\"decimal\":1.25", "\"decimal\":" + token, StringComparison.Ordinal));
        ReviewerAssemblyCaptureRepresentation.ValidateEncoded(bytes, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits);
        using var owner = ReviewerAssemblyInputOwner.FromEncoded(bytes, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits);
        using var child = owner.Acquire(); var copy = child.ReadCopy();
        var value = copy.Artifacts[0].Rows[0].Metadata["z"].Properties!["values"].Items![2];
        Assert.Equal(ReviewerAssemblyValueKind.Decimal, value.Kind);
        Assert.Equal(decimal.Parse(canonical, NumberStyles.Float, CultureInfo.InvariantCulture), value.Decimal);
        Assert.Equal(ReviewerAssemblyFoundationFixture.Golden.Replace("\"decimal\":1.25", "\"decimal\":" + canonical, StringComparison.Ordinal), Encoding.UTF8.GetString(Encode(copy)));
    }

    private static byte[] Encode(ReviewerAssemblyCaptureManifest m, ReviewerAssemblyFoundationLimits? limits = null) =>
        ReviewerAssemblyCaptureRepresentation.Encode(m, ReviewerAssemblyDraftIdentity.Foundation, limits ?? ReviewerAssemblyFoundationFixture.Limits);

    [Fact]
    public void Golden_round_trip_pins_complete_draft_bytes_and_hash()
    {
        var bytes = Encode(ReviewerAssemblyFoundationFixture.Create());
        Assert.Equal(ReviewerAssemblyFoundationFixture.Golden, Encoding.UTF8.GetString(bytes));
        Assert.Equal(ReviewerAssemblyFoundationFixture.GoldenSha256, ReviewerAssemblyCaptureRepresentation.Hash(bytes));
        using var owner = ReviewerAssemblyInputOwner.FromEncoded(bytes, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits);
        using var child = owner.Acquire();
        Assert.Equal(bytes, Encode(child.ReadCopy()));
        Assert.Equal(ReviewerAssemblyFoundationFixture.SourceHash, child.ReadCopy().Sources[0].Sha256);
    }

    [Theory]
    [InlineData("en-US")]
    [InlineData("fr-FR")]
    [InlineData("tr-TR")]
    public void Map_and_lookup_order_are_canonical_while_culture_is_not_input(string culture)
    {
        var m = ReviewerAssemblyFoundationFixture.Create(twoMembers: true);
        var baseline = Encode(m);
        var old = CultureInfo.CurrentCulture; var oldUi = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(culture);
            foreach (var r in m.Artifacts)
            {
                var metadata = r.Rows[0].Metadata;
                var reverse = metadata.Reverse().ToArray(); metadata.Clear();
                foreach (var p in reverse) metadata.Add(p.Key, p.Value);
                var nested = metadata["z"].Properties!;
                var inner = nested.Reverse().ToArray(); nested.Clear(); foreach (var p in inner) nested.Add(p.Key, p.Value);
            }
            m = m with { Artifacts = m.Artifacts.Reverse().ToArray(), Sources = m.Sources.Reverse().ToArray(), Deferred = m.Deferred.Reverse().ToArray() };
            Assert.Equal(baseline, Encode(m));
        }
        finally { CultureInfo.CurrentCulture = old; CultureInfo.CurrentUICulture = oldUi; }
    }

    [Fact]
    public void Significant_member_result_metadata_array_and_page_sequences_are_not_sorted()
    {
        var m = ReviewerAssemblyFoundationFixture.Create(twoMembers: true);
        var original = Encode(m);
        Assert.NotEqual(ReviewerAssemblyCaptureRepresentation.Hash(original), ReviewerAssemblyCaptureRepresentation.Hash(Encode(m with { Members = m.Members with { Rows = m.Members.Rows.Reverse().ToArray() } })));
        var c = new ReviewerAssemblyCondition("condition-2", "issue-1", "Second condition");
        var ordered = m with { Conditions = m.Conditions with { Rows = [m.Conditions.Rows[0], c] } };
        Assert.NotEqual(ReviewerAssemblyCaptureRepresentation.Hash(Encode(ordered)), ReviewerAssemblyCaptureRepresentation.Hash(Encode(ordered with { Conditions = ordered.Conditions with { Rows = ordered.Conditions.Rows.Reverse().ToArray() } })));
        Assert.NotEqual(ReviewerAssemblyCaptureRepresentation.Hash(original), ReviewerAssemblyCaptureRepresentation.Hash(Encode(m with { Preparation = m.Preparation with { SelectedPageSequence = [] } })));
        var items = m.Artifacts[0].Rows[0].Metadata["z"].Properties!["values"].Items!;
        Array.Reverse(items);
        Assert.NotEqual(ReviewerAssemblyCaptureRepresentation.Hash(original), ReviewerAssemblyCaptureRepresentation.Hash(Encode(m)));
    }

    [Fact]
    public void Pending_absent_empty_explicit_null_and_notinvoked_have_distinct_meanings()
    {
        var m = ReviewerAssemblyFoundationFixture.Create();
        Assert.False(m.Pending.Rows[0].IsLegacy); Assert.Null(m.Pending.Rows[0].Snapshot);
        Assert.Equal(ReviewerAssemblyReceiptKind.Absent, m.Snapshot.Kind);
        Assert.Equal(ReviewerAssemblyReceiptKind.Empty, m.Ledgers.Kind);
        Assert.Null(m.Package.Rows[0].SelectedBasisId); Assert.Null(m.Preparation.VeteranDisplayName);
        Assert.All(m.Deferred, r => Assert.Equal(ReviewerAssemblyReceiptKind.NotInvoked, r.Kind));
        ReviewerAssemblyCaptureRepresentation.RequireCapturedResult(m, "ledgers", new(ReviewerAssemblyArgumentKind.Veteran, "veteran-1"), ReviewerAssemblyFoundationFixture.Limits);
        ReviewerAssemblyCaptureRepresentation.RequireCapturedResult(m, "snapshot", new(ReviewerAssemblyArgumentKind.Package, "package-1"), ReviewerAssemblyFoundationFixture.Limits);
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyCaptureRepresentation.RequireCapturedResult(m, "currentUse", new(ReviewerAssemblyArgumentKind.None, null), ReviewerAssemblyFoundationFixture.Limits));
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyCaptureRepresentation.RequireCapturedResult(m, "ledgers", new(ReviewerAssemblyArgumentKind.Veteran, "new-veteran"), ReviewerAssemblyFoundationFixture.Limits));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("duplicate")]
    [InlineData("mapDuplicate")]
    [InlineData("wrongType")]
    [InlineData("version")]
    [InlineData("validationVersion")]
    [InlineData("schema")]
    [InlineData("integerEnum")]
    [InlineData("unknownEnum")]
    [InlineData("base64")]
    [InlineData("invalidUtf8")]
    [InlineData("selfRequired")]
    public void Decoder_rejects_structural_and_expected_version_errors(string change)
    {
        var node = JsonNode.Parse(ReviewerAssemblyFoundationFixture.Golden)!.AsObject();
        byte[] bytes;
        switch (change)
        {
            case "missing": node["preparation"]!.AsObject().Remove("veteranDisplayName"); break;
            case "unknown": node["newField"] = 1; break;
            case "wrongType": node["members"]!["rows"] = "not-an-array"; break;
            case "version": node["identity"]!["representationVersion"] = 2; break;
            case "validationVersion": node["identity"]!["validationVersion"] = 2; break;
            case "schema": node["identity"]!["schema"] = "Reviewer.AdoptedUtf8.ContractProof.v1"; break;
            case "integerEnum": node["ledgers"]!["kind"] = 1; break;
            case "unknownEnum": node["artifacts"]![0]!["rows"]![0]!["metadata"]!["a"]!["kind"] = "UnknownScalar"; break;
            case "base64": node["sources"]![0]!["content"] = "!!!!"; break;
            case "selfRequired": node.Remove("ledgers"); node["requiredQueries"] = new JsonArray("package"); break;
        }
        var json = node.ToJsonString();
        if (change == "duplicate") json = json.Replace("\"snapshotId\":\"snapshot-1\"", "\"snapshotId\":\"snapshot-1\",\"snapshotId\":\"snapshot-1\"", StringComparison.Ordinal);
        if (change == "mapDuplicate") json = json.Replace("\"a\":{", "\"a\":null,\"a\":{", StringComparison.Ordinal);
        bytes = Encoding.UTF8.GetBytes(json);
        if (change == "invalidUtf8") bytes = [0xff, .. bytes];
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyCaptureRepresentation.ValidateEncoded(bytes, ReviewerAssemblyDraftIdentity.Foundation, ReviewerAssemblyFoundationFixture.Limits));
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyCaptureRepresentation.ValidateEncoded(Encoding.UTF8.GetBytes(ReviewerAssemblyFoundationFixture.Golden), new("unknown", 1, 1), ReviewerAssemblyFoundationFixture.Limits));
    }

    [Theory]
    [InlineData("missingLedger")]
    [InlineData("absentPackage")]
    [InlineData("emptyPackageLookup")]
    [InlineData("absentEnumeration")]
    [InlineData("emptyDeferred")]
    [InlineData("duplicateDeferred")]
    [InlineData("wrongQuery")]
    [InlineData("wrongArgument")]
    [InlineData("duplicateMemberQuery")]
    [InlineData("nullScalarMismatch")]
    public void Receipt_completeness_kind_arguments_and_cardinality_are_schema_owned(string change)
    {
        var m = ReviewerAssemblyFoundationFixture.Create(twoMembers: change == "duplicateMemberQuery");
        m = change switch
        {
            "missingLedger" => m with { Ledgers = null! },
            "absentPackage" => m with { Package = m.Package with { Kind = ReviewerAssemblyReceiptKind.Absent, Rows = [] } },
            "emptyPackageLookup" => m with { Package = m.Package with { Kind = ReviewerAssemblyReceiptKind.Empty, Rows = [] } },
            "absentEnumeration" => m with { Ledgers = m.Ledgers with { Kind = ReviewerAssemblyReceiptKind.Absent } },
            "emptyDeferred" => m with { Deferred = m.Deferred.Select((x, i) => i == 0 ? x with { Kind = ReviewerAssemblyReceiptKind.Empty } : x).ToArray() },
            "duplicateDeferred" => m with { Deferred = [m.Deferred[0], m.Deferred[0], .. m.Deferred.Skip(2)] },
            "wrongQuery" => m with { Ledgers = m.Ledgers with { QueryId = "GetAnySql" } },
            "wrongArgument" => m with { Ledgers = m.Ledgers with { Argument = new(ReviewerAssemblyArgumentKind.Claim, "veteran-1") } },
            "duplicateMemberQuery" => m with { Artifacts = [m.Artifacts[0], m.Artifacts[0]] },
            _ => m
        };
        if (change == "nullScalarMismatch") m.Artifacts[0].Rows[0].Metadata["invalid"] = new(ReviewerAssemblyValueKind.Null, "present", null, null, null, null, null, null);
        Assert.Throws<InvalidDataException>(() => Encode(m));
    }

    [Theory]
    [InlineData("package")]
    [InlineData("issue")]
    [InlineData("claim")]
    [InlineData("member")]
    [InlineData("classification")]
    [InlineData("protection")]
    [InlineData("revision")]
    [InlineData("sourceId")]
    [InlineData("fingerprint")]
    [InlineData("bytes")]
    [InlineData("sourceUtf8")]
    public void Identity_source_revision_and_fingerprint_bindings_fail_closed(string change)
    {
        var m = ReviewerAssemblyFoundationFixture.Create();
        m = change switch
        {
            "package" => m with { Package = m.Package with { Rows = [m.Package.Rows[0] with { Id = "other" }] } },
            "issue" => m with { Issue = m.Issue with { Rows = [m.Issue.Rows[0] with { Id = "other" }] } },
            "claim" => m with { Claim = m.Claim with { Rows = [m.Claim.Rows[0] with { Id = "other" }] } },
            "member" => m with { Members = m.Members with { Rows = [m.Members.Rows[0] with { PackageId = "other" }] } },
            "classification" => m with { Classifications = [m.Classifications[0] with { Rows = [m.Classifications[0].Rows[0] with { ArtifactId = "other" }] }] },
            "protection" => m with { Protection = [m.Protection[0] with { Rows = [m.Protection[0].Rows[0] with { ArtifactId = "other" }] }] },
            "revision" => m with { Sources = [m.Sources[0] with { ContentPhysicalRevision = "other-generation" }] },
            "sourceId" => m with { Sources = [m.Sources[0] with { ContentArtifactId = "other" }] },
            "fingerprint" => m with { Sources = [m.Sources[0] with { Fingerprint = "bad" }] },
            "bytes" => m with { Sources = [m.Sources[0] with { Content = [1, 2, 3] }] },
            "sourceUtf8" => ReviewerAssemblyFoundationFixture.WithContent(m, [0xff]),
            _ => m
        };
        Assert.Throws<InvalidDataException>(() => Encode(m));
    }

    [Fact]
    public void Equal_source_bytes_do_not_erase_a_distinct_physical_revision_binding()
    {
        var m = ReviewerAssemblyFoundationFixture.Create();
        var later = m with { Sources = [m.Sources[0] with { PhysicalRevision = "physical-2", ContentPhysicalRevision = "physical-2" }] };
        Assert.Equal(m.Sources[0].Sha256, later.Sources[0].Sha256);
        Assert.NotEqual(ReviewerAssemblyCaptureRepresentation.Hash(Encode(m)), ReviewerAssemblyCaptureRepresentation.Hash(Encode(later)));
    }

    [Theory]
    [InlineData("selectedBasis")]
    [InlineData("legacy")]
    [InlineData("sealed")]
    [InlineData("ledger")]
    [InlineData("clarification")]
    [InlineData("progression")]
    [InlineData("relationship")]
    [InlineData("literature")]
    [InlineData("media")]
    [InlineData("metadataMedia")]
    [InlineData("format")]
    [InlineData("regulation")]
    [InlineData("converter")]
    public void Every_unadmitted_minimal_branch_rejects(string change)
    {
        var m = ReviewerAssemblyFoundationFixture.Create();
        var date = new DateOnly(2020, 1, 2);
        m = change switch
        {
            "selectedBasis" => m with { Package = m.Package with { Rows = [m.Package.Rows[0] with { SelectedBasisId = "basis-1" }] } },
            "legacy" => m with { Pending = m.Pending with { Rows = [m.Pending.Rows[0] with { IsLegacy = true }] } },
            "sealed" => m with { Pending = m.Pending with { Rows = [m.Pending.Rows[0] with { IsSealed = true }] } },
            "ledger" => m with { Ledgers = m.Ledgers with { Kind = ReviewerAssemblyReceiptKind.Present, Rows = [new("ledger-1", "veteran-1", "artifact-1", date, 1, 1, 0, 0, true)] } },
            "clarification" => m with { Clarifications = m.Clarifications with { Kind = ReviewerAssemblyReceiptKind.Present, Rows = [new("note-1", "issue-1", "artifact-1", date, 1, 1, "title", "category", "original", "note", null, null)] } },
            "progression" => m with { Progression = m.Progression with { Kind = ReviewerAssemblyReceiptKind.Present, Rows = [new("event-1", "issue-1", "artifact-1", date, 1, 1, "title", "type", "summary")] } },
            "relationship" => m with { Relationships = [m.Relationships[0] with { Kind = ReviewerAssemblyReceiptKind.Present, Rows = [new("artifact-1", "artifact-1", "DerivedFrom", DateTimeOffset.Parse("2020-01-02T03:04:05+00:00", CultureInfo.InvariantCulture), [])] }] },
            "literature" => m with { Literature = [m.Literature[0] with { Kind = ReviewerAssemblyReceiptKind.Present, Rows = ["literature-1"] }] },
            "media" => m with { Artifacts = [m.Artifacts[0] with { Rows = [m.Artifacts[0].Rows[0] with { ArtifactType = "application/pdf" }] }] },
            "format" => m with { Preparation = m.Preparation with { OutputFormat = "Pdf" } },
            "regulation" => m with { Preparation = m.Preparation with { RequiredCitations = ["synthetic-citation"] } },
            "converter" => m with { Preparation = m.Preparation with { ConverterProfile = "converter" } },
            _ => m
        };
        if (change == "metadataMedia") m.Artifacts[0].Rows[0].Metadata["contentType"] = ReviewerAssemblyValue.String("application/pdf");
        Assert.Throws<InvalidDataException>(() => Encode(m));
    }

    [Fact]
    public void Metadata_types_are_closed_and_nested_cycles_and_unsupported_tags_reject()
    {
        var m = ReviewerAssemblyFoundationFixture.Create();
        m.Artifacts[0].Rows[0].Metadata["unknown"] = new((ReviewerAssemblyValueKind)999, null, null, null, null, null, null, null);
        Assert.Throws<InvalidDataException>(() => Encode(m));
        m = ReviewerAssemblyFoundationFixture.Create();
        m.Artifacts[0].Rows[0].Metadata["cycle"] = ReviewerAssemblyValue.Map(m.Artifacts[0].Rows[0].Metadata);
        Assert.Throws<InvalidDataException>(() => Encode(m));
    }

    [Fact]
    public void Encoded_depth_value_member_receipt_row_and_source_bounds_are_enforced()
    {
        var m = ReviewerAssemblyFoundationFixture.Create(); var limits = ReviewerAssemblyFoundationFixture.Limits;
        var bytes = Encode(m);
        Assert.Equal(bytes, Encode(m, limits with { MaximumEncodedBytes = bytes.Length }));
        Assert.Throws<InvalidDataException>(() => Encode(m, limits with { MaximumEncodedBytes = bytes.Length - 1 }));
        Assert.Equal(bytes, Encode(m, limits with { MaximumSourceBytes = m.Sources[0].Content.Length, MaximumAggregateSourceBytes = m.Sources[0].Content.Length }));
        Assert.Throws<InvalidDataException>(() => Encode(m, limits with { MaximumSourceBytes = m.Sources[0].Content.Length - 1 }));
        Assert.Throws<InvalidDataException>(() => Encode(m, limits with { MaximumAggregateSourceBytes = m.Sources[0].Content.Length - 1 }));
        Assert.Throws<InvalidDataException>(() => Encode(ReviewerAssemblyFoundationFixture.Create(true), limits with { MaximumMembers = 1 }));
        Assert.Throws<InvalidDataException>(() => Encode(m, limits with { MaximumReceipts = 1 }));
        Assert.Throws<InvalidDataException>(() => Encode(m, limits with { MaximumRows = 1 }));
        Assert.Throws<InvalidDataException>(() => Encode(m, limits with { MaximumValueBytes = 1 }));
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyCaptureRepresentation.ValidateEncoded(bytes, ReviewerAssemblyDraftIdentity.Foundation, limits with { MaximumDepth = 4 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Encode(m, limits with { MaximumLeases = 0 }));
        Assert.Throws<OverflowException>(() => ReviewerAssemblyCaptureRepresentation.LogicalDecodeBytes(long.MaxValue));
        // Source limit is independent of metadata scalar size (and base64 expansion).
        m = ReviewerAssemblyFoundationFixture.WithContent(m, Enumerable.Repeat((byte)'x', 1000).ToArray());
        Assert.NotEmpty(Encode(m, limits with { MaximumValueBytes = 128, MaximumSourceBytes = 1000, MaximumAggregateSourceBytes = 1000 }));
    }

    [Fact]
    public void Exact_structural_limits_and_metadata_binary_boundaries_are_pinned()
    {
        var m = ReviewerAssemblyFoundationFixture.Create();
        var limits = ReviewerAssemblyFoundationFixture.Limits with { MaximumReceipts = 22, MaximumRows = 10, MaximumMembers = 1, MaximumDepth = 12, MaximumValueBytes = 64 };
        var bytes = Encode(m, limits);
        ReviewerAssemblyCaptureRepresentation.ValidateEncoded(bytes, ReviewerAssemblyDraftIdentity.Foundation, limits);
        Assert.Equal(bytes, Encode(m, limits with { MaximumReceipts = 23, MaximumRows = 11, MaximumMembers = 2, MaximumDepth = 13, MaximumValueBytes = 65 }));
        Assert.Throws<InvalidDataException>(() => Encode(m, limits with { MaximumReceipts = 21 }));
        Assert.Throws<InvalidDataException>(() => Encode(m, limits with { MaximumRows = 9 }));
        Assert.Throws<InvalidDataException>(() => Encode(m, limits with { MaximumDepth = 11 }));
        Assert.Throws<InvalidDataException>(() => Encode(m, limits with { MaximumValueBytes = 63 }));
        var binary = new byte[64];
        m.Artifacts[0].Rows[0].Metadata["z"].Properties!["payload"] = new(ReviewerAssemblyValueKind.Binary, null, null, null, null, binary, null, null);
        Assert.NotEmpty(Encode(m, limits));
        m.Artifacts[0].Rows[0].Metadata["z"].Properties!["payload"] = new(ReviewerAssemblyValueKind.Binary, null, null, null, null, new byte[65], null, null);
        Assert.Throws<InvalidDataException>(() => Encode(m, limits));
        var node = JsonNode.Parse(ReviewerAssemblyFoundationFixture.Golden)!;
        node["artifacts"]![0]!["rows"]![0]!["metadata"]!["z"]!["properties"]!["payload"]!["binary"] = Convert.ToBase64String(new byte[65]);
        Assert.Throws<InvalidDataException>(() => ReviewerAssemblyCaptureRepresentation.ValidateEncoded(Encoding.UTF8.GetBytes(node.ToJsonString()), ReviewerAssemblyDraftIdentity.Foundation, limits));
    }

    [Fact]
    public void Foundation_remains_separate_from_proof_and_does_not_return_ready_or_security_grants()
    {
        Assert.Equal("Reviewer.AdoptedUtf8.ContractProof.v1", ReviewerRetainedValidator.Profile);
        Assert.NotEqual(ReviewerRetainedValidator.Profile, ReviewerAssemblyDraftIdentity.Foundation.Schema);
        Assert.DoesNotContain(typeof(ReviewerAssemblyInputOwner).GetMethods(), m => m.Name.Contains("Ready", StringComparison.Ordinal) || m.Name.Contains("Authorize", StringComparison.Ordinal) || m.Name.Contains("Seal", StringComparison.Ordinal));
    }
}

internal static class ReviewerAssemblyFoundationFixture
{
    internal static ReviewerAssemblyFoundationLimits Limits => new(65536, 48, 4096, 100, 1000, 8, 8192, 32768, 2097152, 8388608, 100000000, 4);
    internal const string SourceHash = "18C4F85A180CF29DDC1F8E3A3143FF54D643D1C306DBE610F4F70482632071D4";
    internal const string GoldenSha256 = "BE1B75D7E5B3309D3E4F0AAFB3567AD17201F0957B2DEB7A737B9244973EE8F9";
    internal const string Golden = """{"artifacts":[{"argument":{"kind":"Artifact","value":"artifact-1"},"kind":"Present","queryId":"artifact","rows":[{"artifactType":"text/plain","createdUtc":"2020-01-02T03:04:05+00:00","fingerprint":"18C4F85A180CF29DDC1F8E3A3143FF54D643D1C306DBE610F4F70482632071D4","fingerprintAlgorithm":"SHA-256","id":"artifact-1","metadata":{"a":{"binary":null,"boolean":null,"decimal":null,"integer":null,"items":null,"kind":"String","properties":null,"text":"frozen"},"contentType":{"binary":null,"boolean":null,"decimal":null,"integer":null,"items":null,"kind":"String","properties":null,"text":"text/plain"},"z":{"binary":null,"boolean":null,"decimal":null,"integer":null,"items":null,"kind":"Map","properties":{"payload":{"binary":"BwgJ","boolean":null,"decimal":null,"integer":null,"items":null,"kind":"Binary","properties":null,"text":null},"values":{"binary":null,"boolean":null,"decimal":null,"integer":null,"items":[{"binary":null,"boolean":null,"decimal":null,"integer":42,"items":null,"kind":"Int64","properties":null,"text":null},{"binary":null,"boolean":null,"decimal":null,"integer":null,"items":null,"kind":"Null","properties":null,"text":null},{"binary":null,"boolean":null,"decimal":1.25,"integer":null,"items":null,"kind":"Decimal","properties":null,"text":null}],"kind":"Array","properties":null,"text":null}},"text":null}},"name":"synthetic.txt"}]}],"claim":{"argument":{"kind":"Claim","value":"claim-1"},"kind":"Present","queryId":"claim","rows":[{"id":"claim-1","veteranId":"veteran-1"}]},"clarifications":{"argument":{"kind":"Issue","value":"issue-1"},"kind":"Empty","queryId":"clarifications","rows":[]},"classifications":[{"argument":{"kind":"Artifact","value":"artifact-1"},"kind":"Present","queryId":"classifications","rows":[{"artifactId":"artifact-1","classification":"medical-evidence","id":"classification-domain-1","issueId":"issue-1"}]}],"conditions":{"argument":{"kind":"Issue","value":"issue-1"},"kind":"Present","queryId":"conditions","rows":[{"id":"condition-1","issueId":"issue-1","name":"Synthetic condition"}]},"deferred":[{"argument":{"kind":"None","value":null},"kind":"NotInvoked","predicate":"NotInvoked:no literature/basis","queryId":"adjudication"},{"argument":{"kind":"None","value":null},"kind":"NotInvoked","predicate":"NotInvoked:null selected basis","queryId":"basis"},{"argument":{"kind":"None","value":null},"kind":"NotInvoked","predicate":"NotInvoked:no progressions","queryId":"clinicalContext"},{"argument":{"kind":"None","value":null},"kind":"NotInvoked","predicate":"NotInvoked:no ledger","queryId":"currentUse"},{"argument":{"kind":"None","value":null},"kind":"NotInvoked","predicate":"NotInvoked:no ledger","queryId":"ledgerEntries"},{"argument":{"kind":"None","value":null},"kind":"NotInvoked","predicate":"NotInvoked:no opinion/citations","queryId":"regulatory"}],"identity":{"representationVersion":1,"schema":"Reviewer.Capture.FoundationDraft","validationVersion":1},"issue":{"argument":{"kind":"Issue","value":"issue-1"},"kind":"Present","queryId":"issue","rows":[{"claimId":"claim-1","claimIssueType":"synthetic-issue","id":"issue-1"}]},"ledgers":{"argument":{"kind":"Veteran","value":"veteran-1"},"kind":"Empty","queryId":"ledgers","rows":[]},"literature":[{"argument":{"kind":"Artifact","value":"artifact-1"},"kind":"Empty","queryId":"literature","rows":[]}],"members":{"argument":{"kind":"Package","value":"package-1"},"kind":"Present","queryId":"members","rows":[{"artifactId":"artifact-1","contentRole":"UnderlyingEvidence","packageId":"package-1","pageSelection":null}]},"operationId":"operation-1","package":{"argument":{"kind":"Package","value":"package-1"},"kind":"Present","queryId":"package","rows":[{"id":"package-1","issueId":"issue-1","purpose":"synthetic-review","reviewerRole":"synthetic-role","selectedBasisId":null}]},"pending":{"argument":{"kind":"Package","value":"package-1"},"kind":"Present","queryId":"pending","rows":[{"isLegacy":false,"isSealed":false,"snapshot":null,"sourceSnapshotVersion":1}]},"preparation":{"converterProfile":null,"extractionProfile":"synthetic-utf8-v1","outputFormat":"Docx","preparedDate":"2020-01-02","preparer":"Synthetic preparer","printProfile":"synthetic-text-v1","regulatoryMap":{},"regulatoryProfile":"synthetic-empty-v1","rendererProfile":"synthetic-render-v1","requiredCitations":[],"reviewDate":"2020-01-02","selectedPageSequence":[1],"veteranDisplayName":null},"progression":{"argument":{"kind":"Issue","value":"issue-1"},"kind":"Empty","queryId":"progression","rows":[]},"protection":[{"argument":{"kind":"Artifact","value":"artifact-1"},"kind":"Present","queryId":"protection","rows":[{"artifactId":"artifact-1","classificationId":"Confidential","classificationRevision":"classification-1","isAdopted":true}]}],"provenance":[{"argument":{"kind":"Artifact","value":"artifact-1"},"kind":"Present","queryId":"provenance","rows":[{"artifactId":"artifact-1","properties":{},"recordedBy":"synthetic-author","recordedUtc":"2020-01-02T03:04:05+00:00","source":"synthetic-source"}]}],"relationships":[{"argument":{"kind":"Artifact","value":"artifact-1"},"kind":"Empty","queryId":"relationships","rows":[]}],"snapshot":{"argument":{"kind":"Package","value":"package-1"},"kind":"Absent","queryId":"snapshot","rows":[]},"snapshotId":"snapshot-1","sources":[{"artifactId":"artifact-1","content":"c3ludGhldGljCg==","contentArtifactId":"artifact-1","contentPhysicalRevision":"physical-1","fingerprint":"18C4F85A180CF29DDC1F8E3A3143FF54D643D1C306DBE610F4F70482632071D4","fingerprintAlgorithm":"SHA-256","physicalRevision":"physical-1","sha256":"18C4F85A180CF29DDC1F8E3A3143FF54D643D1C306DBE610F4F70482632071D4"}]}""";

    private static ReviewerAssemblyReceipt<T> R<T>(string query, ReviewerAssemblyArgumentKind kind, string value, T[] rows,
        ReviewerAssemblyReceiptKind? state = null) => new(query, new(kind, value), state ?? (rows.Length == 0 ? ReviewerAssemblyReceiptKind.Empty : ReviewerAssemblyReceiptKind.Present), rows);

    internal static ReviewerAssemblyCaptureManifest Create(bool twoMembers = false)
    {
        const string artifact = "artifact-1", package = "package-1", issue = "issue-1", claim = "claim-1", veteran = "veteran-1";
        var time = DateTimeOffset.Parse("2020-01-02T03:04:05+00:00", CultureInfo.InvariantCulture);
        var metadata = new Dictionary<string, ReviewerAssemblyValue>
        {
            ["z"] = ReviewerAssemblyValue.Map(new()
            {
                ["payload"] = new(ReviewerAssemblyValueKind.Binary, null, null, null, null, [7, 8, 9], null, null),
                ["values"] = new(ReviewerAssemblyValueKind.Array, null, null, null, null, null,
                    [new(ReviewerAssemblyValueKind.Int64, null, null, 42, null, null, null, null),
                     new(ReviewerAssemblyValueKind.Null, null, null, null, null, null, null, null),
                     new(ReviewerAssemblyValueKind.Decimal, null, null, null, 1.25m, null, null, null)], null)
            }),
            ["a"] = ReviewerAssemblyValue.String("frozen"),
            ["contentType"] = ReviewerAssemblyValue.String("text/plain")
        };
        var m = new ReviewerAssemblyCaptureManifest(ReviewerAssemblyDraftIdentity.Foundation, "snapshot-1", "operation-1",
            R("package", ReviewerAssemblyArgumentKind.Package, package, new[] { new ReviewerAssemblyPackage(package, issue, "synthetic-review", "synthetic-role", null) }),
            R("pending", ReviewerAssemblyArgumentKind.Package, package, new[] { new ReviewerAssemblyPending(false, 1, false, null) }),
            R("snapshot", ReviewerAssemblyArgumentKind.Package, package, Array.Empty<ReviewerAssemblySnapshot>(), ReviewerAssemblyReceiptKind.Absent),
            R("members", ReviewerAssemblyArgumentKind.Package, package, new[] { new ReviewerAssemblyMember(package, artifact, EvidencePackageContentRoles.UnderlyingEvidence, null) }),
            R("issue", ReviewerAssemblyArgumentKind.Issue, issue, new[] { new ReviewerAssemblyIssue(issue, claim, "synthetic-issue") }),
            R("claim", ReviewerAssemblyArgumentKind.Claim, claim, new[] { new ReviewerAssemblyClaim(claim, veteran) }),
            [R("artifact", ReviewerAssemblyArgumentKind.Artifact, artifact, new[] { new ReviewerAssemblyArtifact(artifact, "synthetic.txt", "text/plain", "SHA-256", SourceHash, time, metadata) })],
            [R("protection", ReviewerAssemblyArgumentKind.Artifact, artifact, new[] { new ReviewerAssemblyProtection(artifact, "Confidential", "classification-1", true) })],
            [R("provenance", ReviewerAssemblyArgumentKind.Artifact, artifact, new[] { new ReviewerAssemblyProvenance(artifact, "synthetic-source", time, "synthetic-author", new()) })],
            [R("relationships", ReviewerAssemblyArgumentKind.Artifact, artifact, Array.Empty<ReviewerAssemblyRelationship>())],
            [R("classifications", ReviewerAssemblyArgumentKind.Artifact, artifact, new[] { new ReviewerAssemblyClassification("classification-domain-1", artifact, issue, "medical-evidence") })],
            [R("literature", ReviewerAssemblyArgumentKind.Artifact, artifact, Array.Empty<string>())],
            R("conditions", ReviewerAssemblyArgumentKind.Issue, issue, new[] { new ReviewerAssemblyCondition("condition-1", issue, "Synthetic condition") }),
            R("ledgers", ReviewerAssemblyArgumentKind.Veteran, veteran, Array.Empty<ReviewerAssemblyLedger>()),
            R("clarifications", ReviewerAssemblyArgumentKind.Issue, issue, Array.Empty<ReviewerAssemblyClarification>()),
            R("progression", ReviewerAssemblyArgumentKind.Issue, issue, Array.Empty<ReviewerAssemblyProgression>()),
            ReviewerAssemblyDraftFields.Queries.Where(x => x.Deferred).Select(x => new ReviewerAssemblyDeferredReceipt(x.Id, new(ReviewerAssemblyArgumentKind.None, null), ReviewerAssemblyReceiptKind.NotInvoked, x.Result)).ToArray(),
            [new(artifact, "physical-1", artifact, "physical-1", "SHA-256", SourceHash, SourceHash, Encoding.UTF8.GetBytes("synthetic\n"))],
            new(null, "Synthetic preparer", new(2020, 1, 2), new(2020, 1, 2), "Docx", "synthetic-utf8-v1", "synthetic-text-v1", "synthetic-render-v1", null, "synthetic-empty-v1", [], new(), [1]));
        if (!twoMembers) return m;
        const string second = "artifact-2";
        return m with
        {
            Members = m.Members with { Rows = [m.Members.Rows[0], m.Members.Rows[0] with { ArtifactId = second }] },
            Artifacts = [m.Artifacts[0], R("artifact", ReviewerAssemblyArgumentKind.Artifact, second, new[] { m.Artifacts[0].Rows[0] with { Id = second } })],
            Protection = [m.Protection[0], R("protection", ReviewerAssemblyArgumentKind.Artifact, second, new[] { m.Protection[0].Rows[0] with { ArtifactId = second } })],
            Provenance = [m.Provenance[0], R("provenance", ReviewerAssemblyArgumentKind.Artifact, second, new[] { m.Provenance[0].Rows[0] with { ArtifactId = second } })],
            Relationships = [m.Relationships[0], R("relationships", ReviewerAssemblyArgumentKind.Artifact, second, Array.Empty<ReviewerAssemblyRelationship>())],
            Classifications = [m.Classifications[0], R("classifications", ReviewerAssemblyArgumentKind.Artifact, second, new[] { m.Classifications[0].Rows[0] with { Id = "classification-domain-2", ArtifactId = second } })],
            Literature = [m.Literature[0], R("literature", ReviewerAssemblyArgumentKind.Artifact, second, Array.Empty<string>())],
            Sources = [m.Sources[0], m.Sources[0] with { ArtifactId = second, ContentArtifactId = second }]
        };
    }

    internal static ReviewerAssemblyCaptureManifest WithContent(ReviewerAssemblyCaptureManifest m, byte[] content)
    {
        var hash = ReviewerAssemblyCaptureRepresentation.Hash(content);
        return m with
        {
            Sources = [m.Sources[0] with { Content = content, Sha256 = hash, Fingerprint = hash }],
            Artifacts = [m.Artifacts[0] with { Rows = [m.Artifacts[0].Rows[0] with { Fingerprint = hash }] }]
        };
    }
}

using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Orchestration;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Services;

namespace EMF.Tests;

public sealed class ReviewerRetainedPlaintextRegressionTests
{
    private sealed class Staging : IArtifactContentStagingStore
    {
        public byte[]? Bytes { get; set; }
        public Task StageAsync(ArtifactContentOperationId id, ReadOnlyMemory<byte> bytes, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); Bytes = bytes.ToArray(); return Task.CompletedTask; }
        public Task<byte[]?> ReadAsync(ArtifactContentOperationId id, CancellationToken ct = default)
        { ct.ThrowIfCancellationRequested(); return Task.FromResult(Bytes?.ToArray()); }
    }
    private static IEnvelopeEncryptionService Protection() => new DevelopmentEnvelopeEncryptionService(
        new InMemoryEncryptionKeyProvider([new EncryptionKey { KeyId = "regression", KeyMaterial = new byte[32] }]));
    private static ReviewerCapturedBundle Bundle()
    {
        var bytes = Encoding.UTF8.GetBytes("owned artifact");
        var hash = ReviewerRetainedValidator.Hash(bytes);
        var representation = JsonSerializer.Serialize(new
        {
            Artifact = JsonSerializer.Serialize(new[] { new { Id = "a", ArtifactType = "text/plain", FingerprintAlgorithm = "SHA-256", FingerprintValue = hash } }),
            Authority = "[{\"IsAdopted\":1,\"ClassificationId\":\"Private\",\"ClassificationRevision\":\"c1\"}]",
            Provenance = "[]", Relationships = "[]"
        });
        return new(new(1, OperationSnapshotId.New(), ReviewerRetainedValidator.Profile, ["a"], ["a:provenance", "a:relationships"],
            [new("a", representation, ReviewerRetainedValidator.Hash(Encoding.UTF8.GetBytes(representation)))],
            [new("a", "physical-1", bytes.Length, hash, "SHA-256", hash, "Private", "c1")]), new() { ["a"] = bytes });
    }
    private static async Task<ReviewerRetainedReference> PutRaw(Staging staging, IEnvelopeEncryptionService protection,
        OperationSnapshotId id, string json)
    {
        var raw = Encoding.UTF8.GetBytes(json);
        var context = Encoding.UTF8.GetBytes("EMF-REVIEWER-CAPTURE-v1\0" + id.Value);
        staging.Bytes = JsonSerializer.SerializeToUtf8Bytes(await protection.EncryptWithContextAsync(raw, context));
        return new(id, ReviewerRetainedValidator.Hash(raw));
    }
    [Theory]
    [InlineData("second-base64")]
    [InlineData("second-type")]
    [InlineData("trailing-schema")]
    [InlineData("truncated-json")]
    public async Task Failed_retained_decode_clears_previously_decoded_and_partial_artifact_buffers(string damage)
    {
        var staging = new Staging(); var protection = Protection(); var bundle = Bundle();
        var first = Convert.ToBase64String(bundle.Content["a"]);
        var suffix = damage switch
        {
            "second-base64" => ",\"b\":\"YWJj!!!!\"},\"Manifest\":" + JsonSerializer.Serialize(bundle.Manifest) + "}",
            "second-type" => ",\"b\":false},\"Manifest\":" + JsonSerializer.Serialize(bundle.Manifest) + "}",
            "trailing-schema" => "},\"Manifest\":{\"Version\":\"wrong\"}}",
            _ => ",\"b\":"
        };
        var reference = await PutRaw(staging, protection, bundle.Manifest.SnapshotId,
            "{\"Content\":{\"a\":\"" + first + "\"" + suffix);
        var store = new ProtectedReviewerRetainedMaterialStore(staging, protection);
        var observed = new List<byte[]>();
        store.OwnedPlaintextCheckpoint = (point, bytes) => { if (point == "Allocated") observed.Add(bytes); };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadValidatedAsync(reference));
        Assert.Equal(damage == "second-base64" ? 2 : 1, observed.Count);
        Assert.All(observed, bytes => Assert.All(bytes, b => Assert.Equal((byte)0, b)));
        Assert.Equal(Encoding.UTF8.GetBytes("owned artifact"), bundle.Content["a"]);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_decode_paths_clear_registered_artifact_buffers_when_checkpoint_throws(bool retainedRead)
    {
        var staging = new Staging(); var protection = Protection(); var bundle = Bundle();
        var store = new ProtectedReviewerRetainedMaterialStore(staging, protection);
        ReviewerRetainedReference? reference = retainedRead ? await store.RetainAsync(bundle) : null;
        var observed = new List<byte[]>();
        store.OwnedPlaintextCheckpoint = (point, bytes) =>
        {
            if (point == "Allocated") observed.Add(bytes);
            if (point == "Decoded") throw new IOException("injected after decoded allocation");
        };
        await Assert.ThrowsAsync<IOException>(() => retainedRead ? store.ReadValidatedAsync(reference!) : (Task)store.RetainAsync(bundle));
        Assert.Single(observed);
        Assert.All(observed[0], b => Assert.Equal((byte)0, b));
        Assert.Equal(Encoding.UTF8.GetBytes("owned artifact"), bundle.Content["a"]);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Both_decode_paths_clear_registered_artifact_buffers_on_cancellation(bool retainedRead)
    {
        var staging = new Staging(); var protection = Protection(); var bundle = Bundle();
        var store = new ProtectedReviewerRetainedMaterialStore(staging, protection);
        ReviewerRetainedReference? reference = retainedRead ? await store.RetainAsync(bundle) : null;
        using var cts = new CancellationTokenSource(); var observed = new List<byte[]>();
        store.OwnedPlaintextCheckpoint = (point, bytes) =>
        {
            if (point == "Allocated") observed.Add(bytes);
            if (point == "Decoded") cts.Cancel();
        };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => retainedRead ? store.ReadValidatedAsync(reference!, cts.Token) : (Task)store.RetainAsync(bundle, cts.Token));
        Assert.Single(observed);
        Assert.All(observed[0], b => Assert.Equal((byte)0, b));
        Assert.Equal(Encoding.UTF8.GetBytes("owned artifact"), bundle.Content["a"]);
    }
    [Fact]
    public async Task Integrity_failure_after_decode_clears_every_registered_artifact_buffer()
    {
        var staging = new Staging(); var protection = Protection(); var bundle = Bundle();
        bundle = bundle with { Manifest = bundle.Manifest with { Members = [bundle.Manifest.Members[0] with { Sha256 = "wrong" }] } };
        var reference = await PutRaw(staging, protection, bundle.Manifest.SnapshotId, JsonSerializer.Serialize(bundle));
        var store = new ProtectedReviewerRetainedMaterialStore(staging, protection); var observed = new List<byte[]>();
        store.OwnedPlaintextCheckpoint = (point, bytes) => { if (point == "Allocated") observed.Add(bytes); };
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ReadValidatedAsync(reference));
        Assert.Single(observed); Assert.All(observed[0], b => Assert.Equal((byte)0, b));
    }
    [Fact]
    public async Task Escaped_base64_decodes_without_artifact_strings_and_success_transfers_independent_buffers()
    {
        var staging = new Staging(); var protection = Protection(); var bundle = Bundle();
        var raw = JsonSerializer.Serialize(bundle);
        var base64 = Convert.ToBase64String(bundle.Content["a"]);
        raw = raw.Replace(base64, "\\u" + ((int)base64[0]).ToString("x4") + base64[1..]);
        var reference = await PutRaw(staging, protection, bundle.Manifest.SnapshotId, raw);
        var store = new ProtectedReviewerRetainedMaterialStore(staging, protection); var observed = new List<byte[]>();
        store.OwnedPlaintextCheckpoint = (point, bytes) => { if (point == "Allocated") observed.Add(bytes); };
        var result = await store.ReadValidatedAsync(reference);
        Assert.Single(observed); Assert.Same(observed[0], result.Content["a"]);
        Assert.NotSame(bundle.Content["a"], result.Content["a"]); Assert.Equal(bundle.Content["a"], result.Content["a"]);
        Assert.Equal(bundle.Manifest.Members[0], result.Manifest.Members[0]);
    }
    [Fact]
    public async Task Replaced_duplicate_content_allocation_is_cleared_before_successful_transfer()
    {
        var staging = new Staging(); var protection = Protection(); var bundle = Bundle();
        var base64 = Convert.ToBase64String(bundle.Content["a"]);
        var raw = "{\"Content\":{\"a\":\"" + base64 + "\",\"a\":\"" + base64 +
            "\"},\"Manifest\":" + JsonSerializer.Serialize(bundle.Manifest) + "}";
        var reference = await PutRaw(staging, protection, bundle.Manifest.SnapshotId, raw);
        var store = new ProtectedReviewerRetainedMaterialStore(staging, protection); var observed = new List<byte[]>();
        store.OwnedPlaintextCheckpoint = (point, bytes) => { if (point == "Allocated") observed.Add(bytes); };
        var retained = await store.ReadValidatedAsync(reference);
        Assert.Equal(2, observed.Count);
        Assert.All(observed[0], b => Assert.Equal((byte)0, b));
        Assert.Same(observed[1], retained.Content["a"]);
        Assert.Equal(bundle.Content["a"], retained.Content["a"]);
    }
    [Fact]
    public async Task Retention_freeze_cleanup_preserves_caller_and_clears_only_internal_validation_copies()
    {
        var staging = new Staging(); var protection = Protection(); var bundle = Bundle();
        var store = new ProtectedReviewerRetainedMaterialStore(staging, protection); var observed = new List<byte[]>();
        store.OwnedPlaintextCheckpoint = (point, bytes) => { if (point == "Allocated") observed.Add(bytes); };
        var reference = await store.RetainAsync(bundle);
        Assert.Equal(2, observed.Count); Assert.All(observed, bytes => Assert.All(bytes, b => Assert.Equal((byte)0, b)));
        Assert.Equal(Encoding.UTF8.GetBytes("owned artifact"), bundle.Content["a"]);
        var retained = await store.ReadValidatedAsync(reference);
        Assert.Equal(bundle.Content["a"], retained.Content["a"]);
        Assert.Equal(bundle.Manifest.Members[0], retained.Manifest.Members[0]);
    }
}

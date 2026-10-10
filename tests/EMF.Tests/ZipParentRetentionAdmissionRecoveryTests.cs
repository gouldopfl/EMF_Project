using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Tests.TestInfrastructure;

namespace EMF.Tests;

public sealed class ZipParentRetentionAdmissionRecoveryTests
{
    private sealed class Crash : Exception;

    [Theory]
    [InlineData("Staged")] [InlineData("CandidateBound")] [InlineData("PhysicalCreated")] [InlineData("Created")]
    public async Task Source_free_recovery_reuses_original_frozen_creation_and_clears_plaintext(string boundary)
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        var f = new ZipDurableRuntimeFixture(evidence); await f.Journal.InitializeAsync();
        var bytes = ZipDurableRuntimeFixture.Zip();
        using (var source = new ArtifactContentReadLease(new("source-parent"), new("source-revision"), bytes.Length, bytes))
        {
            f.ParentRetention.Checkpoint = name => name == boundary ? throw new Crash() : Task.CompletedTask;
            await Assert.ThrowsAsync<Crash>(() => f.ParentRetention.RetainAsync("zip", "parent", new string('B', 64), source));
        }
        Assert.All(bytes, value => Assert.Equal(0, value));
        var original = (await f.Journal.ReadParentRetentionAsync("zip"))!; Assert.Null(await f.Journal.ReadAsync("zip"));
        var encryptions = f.Count("parent-encrypt"); var fresh = new ZipDurableRuntimeFixture(evidence);
        var parent = await fresh.ParentRetention.RecoverCreationAsync(original.Identity);
        var recovered = (await fresh.Journal.ReadParentRetentionAsync("zip"))!;
        Assert.Equal(original.Identity, recovered.Identity); Assert.Equal(original.Identity.ObjectId, parent.Input.ContentId);
        Assert.Equal(recovered.CreateReceipt!.CurrentRevision!.Value.Value, parent.Input.Revision);
        Assert.Equal(encryptions, f.Count("parent-encrypt"));
        Assert.NotNull(fresh.ParentCrypto.LastPlaintext); Assert.All(fresh.ParentCrypto.LastPlaintext!, value => Assert.Equal(0, value));
        Assert.Equal(parent, await fresh.ParentRetention.RecoverCreationAsync(original.Identity));
        Assert.Equal(encryptions, f.Count("parent-encrypt"));
    }

    [Theory]
    [InlineData("PreparationStarted")] [InlineData("CandidateBound")]
    public async Task Missing_candidate_goes_to_review_without_reencryption(string boundary)
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync();
        var f = new ZipDurableRuntimeFixture(evidence); await f.Journal.InitializeAsync();
        using var source = new ArtifactContentReadLease(new("source-parent"), new("source-revision"), ZipDurableRuntimeFixture.Zip().Length, ZipDurableRuntimeFixture.Zip());
        f.ParentRetention.Checkpoint = name => name == boundary ? throw new Crash() : Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(() => f.ParentRetention.RetainAsync("zip", "parent", new string('B', 64), source));
        var original = (await f.Journal.ReadParentRetentionAsync("zip"))!;
        if (boundary == "CandidateBound")
            foreach (var file in Directory.GetFiles(Path.Combine(evidence.Root, "zip-parent-private", "zip-candidates"), "*.candidate")) File.Delete(file);
        var encryptions = f.Count("parent-encrypt"); var fresh = new ZipDurableRuntimeFixture(evidence);
        await Assert.ThrowsAsync<InvalidDataException>(() => fresh.ParentRetention.RecoverCreationAsync(original.Identity));
        Assert.Equal(ZipRetentionState.RequiresReview, (await fresh.Journal.ReadParentRetentionAsync("zip"))!.State);
        Assert.Null(await fresh.Journal.ReadAsync("zip")); Assert.Equal(encryptions, f.Count("parent-encrypt"));
    }

    [Fact]
    public async Task Changed_admitted_identity_is_rejected_without_reviewing_or_replacing_original()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync(); var f = new ZipDurableRuntimeFixture(evidence);
        var parent = await f.CreateParent(); var retained = (await f.Journal.ReadParentRetentionAsync("zip"))!;
        await Assert.ThrowsAsync<InvalidDataException>(() => f.ParentRetention.RecoverCreationAsync(retained.Identity with { ProfileHash = new string('C', 64) }));
        await Assert.ThrowsAsync<InvalidDataException>(() => f.ParentRetention.RecoverCreationAsync(retained.Identity with { NamespaceId = new string('C', 64) }));
        Assert.Equal(retained, await f.Journal.ReadParentRetentionAsync("zip"));
        Assert.Equal(parent.Fence, (await f.Journal.ReadAsync("zip"))!.Fence);
    }

    [Fact]
    public async Task Contradictory_frozen_candidate_goes_to_review_without_regeneration()
    {
        await using var evidence = await ArtifactIngestionFixture.CreateAsync(); var f = new ZipDurableRuntimeFixture(evidence);
        await f.Journal.InitializeAsync(); var bytes = ZipDurableRuntimeFixture.Zip();
        using var source = new ArtifactContentReadLease(new("source-parent"), new("source-revision"), bytes.Length, bytes);
        f.ParentRetention.Checkpoint = name => name == "CandidateBound" ? throw new Crash() : Task.CompletedTask;
        await Assert.ThrowsAsync<Crash>(() => f.ParentRetention.RetainAsync("zip", "parent", new string('B', 64), source));
        var original = (await f.Journal.ReadParentRetentionAsync("zip"))!;
        var candidate = Assert.Single(Directory.GetFiles(Path.Combine(evidence.Root, "zip-parent-private", "zip-candidates"), "*.candidate"));
        using (var file = new FileStream(candidate, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        { var first = file.ReadByte(); file.Position = 0; file.WriteByte((byte)(first ^ 1)); file.Flush(true); }
        var encryptions = f.Count("parent-encrypt"); var fresh = new ZipDurableRuntimeFixture(evidence);
        await Assert.ThrowsAsync<InvalidDataException>(() => fresh.ParentRetention.RecoverCreationAsync(original.Identity));
        Assert.Equal(ZipRetentionState.RequiresReview, (await fresh.Journal.ReadParentRetentionAsync("zip"))!.State);
        Assert.Equal(encryptions, f.Count("parent-encrypt")); Assert.Null(await fresh.Journal.ReadAsync("zip"));
    }
}

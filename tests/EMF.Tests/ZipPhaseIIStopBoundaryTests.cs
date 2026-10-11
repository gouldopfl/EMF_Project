using System.IO.Compression;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;
namespace EMF.Tests;

public sealed class ZipPhaseIIStopBoundaryTests
{
    private static async Task<ZipExecutionAttempt> Claim(ZipOwnershipFixture f, SyntheticZipExecutionGrant grant)
        => await ZipExecutionAttempt.ClaimAsync(new(f.Journal, f.Journal, f.Profile, f.Clock), grant,
            await f.RequestAsync(grant.Purpose), f.Clock);
    private static byte[] Archive(bool directory)
    {
        using var bytes = new MemoryStream();
        using (var archive = new ZipArchive(bytes, ZipArchiveMode.Create, true))
            if (directory) archive.CreateEntry("directory/");
        return bytes.ToArray();
    }
    private static async Task AssertNoLifecycleProgression(ZipOwnershipFixture f, ZipParentSnapshot parent)
    {
        Assert.Equal(-1, parent.Fence.ConfirmedOrdinal);
        Assert.NotEqual(ZipParentState.Completed, parent.State);
        Assert.NotEqual(ZipParentState.Released, parent.State);
        Assert.NotEqual(ZipParentState.RequiresReview, parent.State);
        Assert.Equal(0, parent.Budget.IngestionAttempts);
        Assert.Equal(0, f.Runtime.Count("ingestion-encrypt"));
        Assert.Equal(0, f.Runtime.Count("Ingesting"));
        Assert.Equal(0, f.Runtime.Count("Resuming"));
        var retainedParent = (await f.Journal.ReadParentRetentionAsync(f.Admission.OperationId))!;
        Assert.Equal(ZipRetentionState.Created, retainedParent.State);
        Assert.Null(retainedParent.ReleaseReceipt);
        Assert.Null(retainedParent.CandidateCleanup);
        foreach (var entry in parent.Entries.Where(e => e.Plan.FileOrdinal is not null))
        {
            Assert.Null(entry.Ingestion);
            Assert.NotEqual(ZipEntryState.Acknowledged, entry.State);
            Assert.NotEqual(ZipEntryState.RequiresReview, entry.State);
            Assert.NotEqual(ZipEntryState.Released, entry.State);
            var retention = await f.Journal.ReadRetentionAsync(f.Admission.OperationId, entry.Plan.FileOrdinal!.Value);
            if (retention is not null)
            {
                Assert.Equal(ZipRetentionState.Created, retention.State);
                Assert.Null(retention.ReleaseReceipt);
                Assert.Null(retention.CandidateCleanup);
            }
            Assert.Null(await f.Evidence.Repository.GetArtifactAsync(new(entry.Plan.ProvisionalArtifactId!)));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Empty_or_directory_only_archive_stops_before_completion_release_or_downstream(bool directory)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync(sourceBytes: Archive(directory));
        await using var grant = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, f.Clock);
        await using var attempt = await Claim(f, grant);
        var result = await f.Runtime.Driver.RunAsync(attempt);
        Assert.Equal(ZipPhaseIIStop.AwaitingLaterCompletion, result.Stop);
        Assert.Null(result.FileOrdinal);
        Assert.Equal(ZipParentState.Planned, result.Parent.State);
        Assert.NotNull(result.Parent.Plan);
        Assert.Equal(directory ? 1 : 0, result.Parent.Plan.Entries.Count);
        Assert.All(result.Parent.Plan.Entries, entry => Assert.True(entry.IsDirectory));
        Assert.Equal(0, result.Parent.Budget.ExtractionAttempts);
        Assert.Equal(0, result.Parent.Budget.ScannerAttempts);
        Assert.Equal(0, f.Runtime.Count("scan"));
        Assert.Equal(0, f.Runtime.Count("child-encrypt"));
        Assert.Equal(0, f.Runtime.Count("child-decrypt"));
        await AssertNoLifecycleProgression(f, result.Parent);
        Assert.NotNull(f.Runtime.ParentCrypto.LastPlaintext);
        Assert.All(f.Runtime.ParentCrypto.LastPlaintext, value => Assert.Equal((byte)0, value));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Recovery_of_materialized_or_scanned_child_stops_without_added_payload_or_effect_scopes(bool scanned)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        await using var initial = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.InitialExecution, f.Clock);
        await using var initialAttempt = await Claim(f, initial);
        var first = await f.Runtime.Driver.RunAsync(initialAttempt);
        Assert.Equal(ZipPhaseIIStop.AwaitingScanner, first.Stop);
        var parent = first.Parent;
        // Seed a later-phase fact explicitly through the existing test scanner.
        // This setup is outside the new authorized Phase II driver path.
        if (scanned) parent = await f.Runtime.Scan.ScanNextAsync(parent);
        Assert.Equal(scanned ? ZipEntryState.Scanned : ZipEntryState.Materialized, parent.Entries.Single().State);
        await using var recovery = new SyntheticZipExecutionGrant(f.Admission, ZipOwnershipPurpose.Recovery, f.Clock);
        await using var attempt = await Claim(f, recovery);
        var before = (await f.Journal.ReadAsync(f.Admission.OperationId))!;
        var parentReads = f.Runtime.Count("parent-decrypt");
        var childReads = f.Runtime.Count("child-decrypt");
        var childCreates = f.Runtime.Count("child-encrypt");
        var scans = f.Runtime.Count("scan");
        var scopes = recovery.EffectRequests;
        var retained = await f.Journal.ReadRetentionAsync(f.Admission.OperationId, 0);
        var result = await f.Runtime.Driver.RunAsync(attempt);
        Assert.Equal(scanned ? ZipPhaseIIStop.OutsidePhaseIIBoundary : ZipPhaseIIStop.AwaitingScanner, result.Stop);
        Assert.Equal(0, result.FileOrdinal);
        Assert.Equal(before.Fence, result.Parent.Fence);
        Assert.Equal(before.Budget, result.Parent.Budget);
        Assert.Equal(before.Entries.ToArray(), result.Parent.Entries.ToArray());
        Assert.Equal(retained, await f.Journal.ReadRetentionAsync(f.Admission.OperationId, 0));
        Assert.Equal(parentReads, f.Runtime.Count("parent-decrypt"));
        Assert.Equal(childReads, f.Runtime.Count("child-decrypt"));
        Assert.Equal(childCreates, f.Runtime.Count("child-encrypt"));
        Assert.Equal(scans, f.Runtime.Count("scan"));
        Assert.Equal(scopes, recovery.EffectRequests);
        Assert.Equal(scanned ? 1 : 0, result.Parent.Budget.ScannerAttempts);
        await AssertNoLifecycleProgression(f, result.Parent);
    }
}

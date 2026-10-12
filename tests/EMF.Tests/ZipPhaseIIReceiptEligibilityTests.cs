using System.Security.Cryptography;
using System.Text.Json;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipPhaseIIReceiptEligibilityTests
{
    [Theory]
    [InlineData("renewed")]
    [InlineData("revoked")]
    [InlineData("review")]
    public async Task Physical_receipt_survives_durable_eligibility_expiry_but_progression_requires_current_eligible_admission(string continuation)
    {
        await using var f = await ZipOwnershipFixture.CreateAsync();
        var originalApproval = f.Admission.Approval!;
        f.Admission = await f.Journal.AppendAdmissionEventAsync(f.Admission, new("short-evidence",
            ZipAdmissionEventKind.AuthorityValidated, "synthetic-current-actor", f.Clock.Now,
            originalApproval with { DecisionId = "short-decision", IssuedUtc = f.Clock.Now, ExpiresUtc = f.Clock.Now.AddMinutes(1) }));
        var shortEvidence = f.Admission;
        await using var root = new SyntheticZipExecutionGrant(shortEvidence, ZipOwnershipPurpose.InitialExecution, f.Clock);
        var ownership = new ZipAuthorizedOwnershipService(f.Journal, f.Journal, f.Profile, f.Clock);
        var initialRequest = await f.RequestAsync(ZipOwnershipPurpose.InitialExecution);
        await using var attempt = await ZipExecutionAttempt.ClaimAsync(ownership, root, initialRequest, f.Clock);
        var physicalBoundaryReached = false;
        f.Runtime.ChildRetention.Checkpoint = name =>
        {
            if (name == "PhysicalCreated")
            {
                physicalBoundaryReached = true;
                // The separate physical catalog has committed its receipt. The ZIP
                // transaction still owns its write lock, so clock advancement models
                // independent evidence expiry without attempting a second writer.
                f.Clock.Now = shortEvidence.LatestAuthorityEvidence!.ExpiresUtc;
            }
            return Task.CompletedTask;
        };
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => f.Runtime.Driver.RunAsync(attempt));
        Assert.True(physicalBoundaryReached);
        Assert.False(root.Revoked.IsCancellationRequested); await root.RevalidateAsync();
        Assert.True(f.Clock.Now < root.ValidUntilUtc);
        var interrupted = (await f.Journal.ReadAsync(shortEvidence.OperationId))!;
        Assert.True(f.Clock.Now < interrupted.OwnerUntil);
        Assert.Equal(ZipParentState.Processing, interrupted.State);
        Assert.Equal(ZipEntryState.Planned, interrupted.Entries[0].State);
        var retained = (await f.Journal.ReadRetentionAsync(shortEvidence.OperationId, 0))!;
        Assert.Equal(ZipRetentionState.CandidateBound, retained.State); Assert.Null(retained.CreateReceipt);
        var receipt = await f.Runtime.ChildStorage.GetReceiptAsync(new(retained.Identity.CreateOperationId));
        Assert.NotNull(receipt);
        var candidate = (await f.Runtime.ChildStorage.ReadCandidateAsync(new(retained.Identity.CreateOperationId)))!;
        var candidateHash = Convert.ToHexString(SHA256.HashData(candidate)); CryptographicOperations.ZeroMemory(candidate);
        Assert.Equal(retained.CandidateHash, candidateHash);
        var parentRetention = (await f.Journal.ReadParentRetentionAsync(shortEvidence.OperationId))!;
        var integrity = await IntegrityAsync(f.Evidence.DatabasePath, shortEvidence.OperationId); Assert.NotNull(integrity);
        var parentDecrypts = f.Runtime.Count("parent-decrypt");
        var projection = JsonSerializer.Serialize(interrupted);
        var encrypts = f.Runtime.Count("child-encrypt"); var extractionStarts = f.Runtime.Count("extraction-start");
        Assert.Equal(1, encrypts); Assert.Equal(1, extractionStarts);
        Assert.Equal(0, interrupted.Budget.ScannerAttempts); Assert.Equal(0, interrupted.Budget.IngestionAttempts);
        f.Runtime.ChildRetention.Checkpoint = null;
        await using (var expiredRecovery = new SyntheticZipExecutionGrant(shortEvidence, ZipOwnershipPurpose.Recovery, f.Clock))
        {
            await expiredRecovery.RevalidateAsync();
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ZipExecutionAttempt.ClaimAsync(ownership,
                expiredRecovery, initialRequest with { Purpose = ZipOwnershipPurpose.Recovery,
                    ExpectedFence = interrupted.Fence, ExpectedOwnerUntil = interrupted.OwnerUntil }, f.Clock));
        }
        Assert.Equal(projection, JsonSerializer.Serialize((await f.Journal.ReadAsync(shortEvidence.OperationId))!));
        Assert.Equal(retained, await f.Journal.ReadRetentionAsync(shortEvidence.OperationId, 0));
        Assert.Equal(shortEvidence, await f.Journal.ReadAdmissionAsync(shortEvidence.OperationId));
        // Existing durable revalidation appends current evidence with the original
        // semantics intact. It grants no executable authority; a new root follows.
        var renewed = await f.Journal.AppendAdmissionEventAsync(shortEvidence, new("renewed-evidence",
            ZipAdmissionEventKind.AuthorityValidated, "synthetic-current-actor", f.Clock.Now,
            originalApproval with { DecisionId = "renewed-decision", IssuedUtc = f.Clock.Now, ExpiresUtc = f.Clock.Now.AddMinutes(30) }));
        Assert.Equal(originalApproval, renewed.Approval);
        if (continuation != "renewed")
            renewed = await f.Journal.AppendAdmissionEventAsync(renewed, new("durable-stop",
                continuation == "revoked" ? ZipAdmissionEventKind.Revoked : ZipAdmissionEventKind.RequiresReview,
                "synthetic-current-actor", f.Clock.Now, SafeReason: "CurrentEligibilityStopped"));
        f.Admission = renewed;
        await using var recovery = new SyntheticZipExecutionGrant(renewed, ZipOwnershipPurpose.Recovery, f.Clock);
        await recovery.RevalidateAsync(); Assert.False(recovery.Revoked.IsCancellationRequested);
        var request = await f.RequestAsync(ZipOwnershipPurpose.Recovery); // same owner, exact current projection
        if (continuation == "renewed")
        {
            await using var resumed = await ZipExecutionAttempt.ClaimAsync(ownership, recovery, request, f.Clock);
            var result = await f.Runtime.Driver.RunAsync(resumed);
            Assert.Equal(ZipPhaseIIStop.AwaitingScanner, result.Stop);
            Assert.Equal(ZipEntryState.Materialized, result.Parent.Entries[0].State);
            Assert.Equal(interrupted.Budget, result.Parent.Budget);
            Assert.Equal(interrupted.Plan!.Hash, result.Parent.Plan!.Hash);
            var created = (await f.Journal.ReadRetentionAsync(renewed.OperationId, 0))!;
            Assert.Equal(ZipRetentionState.Created, created.State);
            Assert.Equal(retained.Identity, created.Identity); Assert.Equal(candidateHash, created.CandidateHash);
            Assert.Equal(receipt, created.CreateReceipt);
        }
        else
        {
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => ZipExecutionAttempt.ClaimAsync(ownership, recovery, request, f.Clock));
            Assert.Equal(projection, JsonSerializer.Serialize((await f.Journal.ReadAsync(renewed.OperationId))!));
            Assert.Equal(retained, await f.Journal.ReadRetentionAsync(renewed.OperationId, 0));
            Assert.Equal(renewed, await f.Journal.ReadAdmissionAsync(renewed.OperationId));
        }
        Assert.Equal(parentRetention, await f.Journal.ReadParentRetentionAsync(renewed.OperationId));
        Assert.Equal(receipt, await f.Runtime.ChildStorage.GetReceiptAsync(new(retained.Identity.CreateOperationId)));
        Assert.Equal(integrity, await IntegrityAsync(f.Evidence.DatabasePath, renewed.OperationId));
        Assert.Equal(parentDecrypts, f.Runtime.Count("parent-decrypt"));
        Assert.Null(await f.Runtime.ChildStorage.GetReceiptAsync(new(retained.Identity.ReleaseOperationId)));
        Assert.Equal(encrypts, f.Runtime.Count("child-encrypt")); Assert.Equal(extractionStarts, f.Runtime.Count("extraction-start"));
        Assert.Equal(0, f.Runtime.Count("scan")); Assert.Equal(0, f.Runtime.Count("ingestion-encrypt"));
        var final = (await f.Journal.ReadAsync(renewed.OperationId))!;
        Assert.Equal(-1, final.Fence.ConfirmedOrdinal); Assert.Equal(0, final.Budget.RejectedEntries);
        var finalChild = (await f.Journal.ReadRetentionAsync(renewed.OperationId, 0))!;
        Assert.Null(finalChild.ReleaseReceipt); Assert.Null(finalChild.CandidateCleanup);
    }
    private static async Task<string?> IntegrityAsync(string database, string operation)
    {
        await using var c = new SqliteConnection("Data Source=" + database); await c.OpenAsync();
        using var q = c.CreateCommand();
        q.CommandText = "SELECT VerificationJson FROM ZipExtractionVerifications WHERE ParentOperationId=$op AND FileOrdinal=0";
        q.Parameters.AddWithValue("$op", operation); return await q.ExecuteScalarAsync() as string;
    }

}

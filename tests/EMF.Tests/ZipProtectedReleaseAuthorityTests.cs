using System.Security.Cryptography;
using System.Text;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Tests.TestInfrastructure;
using Xunit;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipProtectedReleaseAuthorityTests
{
    internal static async Task<ZipScannerAuthorityTests.Setup> AcknowledgedAsync(bool two=false,TimeSpan? rootLife=null)
    {
        var setup=await ZipScannerAuthorityTests.Setup.Create(rootLife,two);
        await setup.Scan();
        setup.Root.GuardedChildOpener=(p,e,gate,ct)=>setup.Fixture.Runtime.Host.OpenGuardedAsync(p,e,gate,ct);
        Assert.Equal(ZipChildIngestionStop.Ingested,
            (await setup.Fixture.Runtime.Driver.RunChildIngestionAsync(setup.Attempt)).Stop);
        Assert.Equal(ZipAcknowledgementStop.Acknowledged,
            (await setup.Fixture.Runtime.Driver.RunAcknowledgementAsync(setup.Attempt)).Stop);
        return setup;
    }

    internal static string CandidatePath(ZipOwnershipFixture fixture,ZipRetentionRecord retention)
        =>Path.Combine(fixture.Evidence.Root,"zip-child-private","zip-candidates",
            Convert.ToHexString(SHA256.HashData(Encoding.ASCII.GetBytes(retention.Identity.CreateOperationId)))+".candidate");

    [Fact]
    public async Task Live_protected_release_records_exact_receipt_and_stops_before_candidate_cleanup()
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        var before=(await setup.Read())!;var retained=(await fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!;
        var candidate=CandidatePath(fixture,retained);Assert.True(File.Exists(candidate));
        var result=await fixture.Runtime.Driver.RunProtectedReleaseAsync(setup.Attempt);
        var after=(await setup.Read())!;var release=(await fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!;
        Assert.Equal(ZipProtectedReleaseStop.ReleaseReceiptRecorded,result.Stop);
        Assert.Equal(ZipRetentionState.ReleasePending,release.State);
        Assert.NotNull(release.ReleaseReceipt);Assert.Null(release.CandidateCleanup);
        Assert.Equal(retained.Identity.ReleaseOperationId,release.ReleaseReceipt.OperationId.Value);
        Assert.Equal(retained.CreateReceipt!.CurrentRevision,release.ReleaseReceipt.PriorRevision);
        Assert.True(File.Exists(candidate));
        Assert.Equal(ZipEntryState.Acknowledged,after.Entries.Single().State);
        Assert.Equal(ZipParentState.Processing,after.State);
        Assert.Equal(before.Budget,after.Budget);
        Assert.Equal(before.Fence.Revision+2,after.Fence.Revision);
    }

    [Theory]
    [InlineData("parent")][InlineData("fence")][InlineData("file")][InlineData("central")]
    [InlineData("ack")][InlineData("contains")][InlineData("derived")][InlineData("canonical")]
    [InlineData("retained-object")][InlineData("retained-revision")][InlineData("namespace")]
    [InlineData("create-receipt")][InlineData("candidate-hash")][InlineData("release-operation")]
    public async Task Exact_release_request_mismatch_denies_claim_without_effect(string part)
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        var before=(await setup.Read())!;
        var evidence=await fixture.Journal.ReadProtectedReleaseEvidenceAsync(before.Binding.OperationId,0);
        await using var guard=setup.Attempt.ProtectedReleaseBoundary(()=>evidence);
        var request=setup.Attempt.ProtectedReleaseRequest(evidence);
        request=part switch
        {
            "parent"=>request with{Parent=request.Parent with{OperationId="foreign"}},
            "fence"=>request with{Fence=request.Fence with{Revision=request.Fence.Revision+1}},
            "file"=>request with{Occurrence=request.Occurrence with{FileOrdinal=1}},
            "central"=>request with{Occurrence=request.Occurrence with{CentralOrdinal=1}},
            "ack"=>request with{Acknowledgement=request.Acknowledgement with{ChildSha256=new string('A',64)}},
            "contains"=>request with{ContainsId=request.ContainsId+100},
            "derived"=>request with{DerivedFromId=request.DerivedFromId+100},
            "canonical"=>request with{Ingestion=request.Ingestion with{CanonicalArtifactId="foreign"}},
            "retained-object"=>request with{Retention=request.Retention with{Identity=request.Retention.Identity with{ObjectId="foreign"}}},
            "retained-revision"=>request with{ChildInput=request.ChildInput with{Revision="foreign"}},
            "namespace"=>request with{Retention=request.Retention with{Identity=request.Retention.Identity with{NamespaceId="foreign"}}},
            "create-receipt"=>request with{Retention=request.Retention with{CreateReceipt=request.Retention.CreateReceipt! with{AuditObligationVersion=2}}},
            "candidate-hash"=>request with{Retention=request.Retention with{CandidateHash=new string('A',64)}},
            _=>request with{Retention=request.Retention with{Identity=request.Retention.Identity with{ReleaseOperationId="foreign"}}}
        };
        await Assert.ThrowsAnyAsync<Exception>(()=>fixture.Journal.ClaimRetentionReleaseAsync(request,guard));
        var after=(await setup.Read())!;var retained=(await fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!;
        Assert.Equal(before.Fence,after.Fence);Assert.Equal(before.Budget,after.Budget);
        Assert.Equal(ZipRetentionState.Created,retained.State);Assert.Null(retained.ReleaseReceipt);
        Assert.Null(await fixture.Runtime.ChildStorage.GetReceiptAsync(new(retained.Identity.ReleaseOperationId)));
        Assert.True(File.Exists(CandidatePath(fixture,retained)));
    }

    [Theory]
    [InlineData("root", "ClaimCommitWindow")][InlineData("scope", "ClaimCommitWindow")]
    [InlineData("root", "PromoteCommitWindow")][InlineData("scope", "PromoteCommitWindow")]
    public async Task Revocation_at_journal_commit_denies_progression(string target,string point)
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        fixture.Journal.ProtectedReleaseCheckpoint=at=>
        {
            if(at!=point)return;
            if(target=="root")setup.Root.Revoke();else setup.Root.RevokeCurrentEffect!();
        };
        await Assert.ThrowsAnyAsync<Exception>(()=>fixture.Runtime.Driver.RunProtectedReleaseAsync(setup.Attempt));
        var retained=(await fixture.Journal.ReadRetentionAsync(fixture.Admission.OperationId,0))!;
        Assert.Equal(point=="ClaimCommitWindow"?ZipRetentionState.Created:ZipRetentionState.ReleasePending,retained.State);
        Assert.Null(retained.ReleaseReceipt);Assert.Null(retained.CandidateCleanup);
        Assert.True(File.Exists(CandidatePath(fixture,retained)));
        Assert.Equal(ZipEntryState.Acknowledged,(await setup.Read())!.Entries.Single().State);
    }

    [Theory]
    [InlineData("BeforeCommit",false)][InlineData("Committed",true)]
    public async Task Protected_catalog_commit_boundary_requires_live_scope_and_recovery_reconciles_receipt(
        string point,bool committed)
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        fixture.Runtime.ChildStorage.PhysicalCheckpoint=at=>
        {if(at==point)setup.Root.Revoke();return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<Exception>(()=>fixture.Runtime.Driver.RunProtectedReleaseAsync(setup.Attempt));
        var pending=(await fixture.Journal.ReadRetentionAsync(fixture.Admission.OperationId,0))!;
        Assert.Equal(ZipRetentionState.ReleasePending,pending.State);
        Assert.Null(pending.ReleaseReceipt);Assert.Null(pending.CandidateCleanup);
        var receipt=await fixture.Runtime.ChildStorage.GetReceiptAsync(new(pending.Identity.ReleaseOperationId));
        Assert.Equal(committed,receipt is {Outcome:ArtifactContentMutationOutcome.Deleted});
        Assert.True(File.Exists(CandidatePath(fixture,pending)));
        fixture.Runtime.ChildStorage.PhysicalCheckpoint=null;
        await using var recoveryRoot=new SyntheticZipExecutionGrant(fixture.Admission,ZipOwnershipPurpose.Recovery,fixture.Clock);
        await using var recovery=await ZipExecutionAttempt.ClaimAsync(
            new(fixture.Journal,fixture.Journal,fixture.Profile,fixture.Clock),recoveryRoot,
            await fixture.RequestAsync(recoveryRoot.Purpose),fixture.Clock);
        var result=await fixture.Runtime.Driver.RunProtectedReleaseAsync(recovery);
        Assert.Equal(ZipProtectedReleaseStop.ReleaseReceiptRecorded,result.Stop);
        var final=(await fixture.Journal.ReadRetentionAsync(fixture.Admission.OperationId,0))!;
        Assert.Equal(ZipRetentionState.ReleasePending,final.State);
        Assert.Equal(ArtifactContentMutationOutcome.Deleted,final.ReleaseReceipt!.Outcome);
        Assert.Null(final.CandidateCleanup);
        Assert.Equal(receipt??final.ReleaseReceipt,
            await fixture.Runtime.ChildStorage.GetReceiptAsync(new(final.Identity.ReleaseOperationId)));
    }

    [Theory]
    [InlineData("root","ClaimCommitWindow")][InlineData("scope","ClaimCommitWindow")]
    [InlineData("root","PromoteCommitWindow")][InlineData("scope","PromoteCommitWindow")]
    public async Task Expiry_at_journal_commit_denies_next_progression(string target,string point)
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        var start=fixture.Clock.Now;
        if(target=="scope")setup.Root.EffectLifetime=TimeSpan.FromSeconds(1);
        fixture.Journal.ProtectedReleaseCheckpoint=at=>
        {if(at==point)fixture.Clock.Now=target=="root"?setup.Root.ValidUntilUtc:start.AddSeconds(1);};
        await Assert.ThrowsAnyAsync<Exception>(()=>fixture.Runtime.Driver.RunProtectedReleaseAsync(setup.Attempt));
        var retained=(await fixture.Journal.ReadRetentionAsync(fixture.Admission.OperationId,0))!;
        Assert.Equal(point=="ClaimCommitWindow"?ZipRetentionState.Created:ZipRetentionState.ReleasePending,retained.State);
        Assert.Null(retained.ReleaseReceipt);Assert.Null(retained.CandidateCleanup);
        Assert.True(File.Exists(CandidatePath(fixture,retained)));
    }

    [Fact]
    public async Task Missing_protected_object_without_exact_release_receipt_denies_before_claim()
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        var before=(await setup.Read())!;var retained=(await fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!;
        var foreign=new ArtifactContentMutationContext(new("foreign-"+Guid.NewGuid().ToString("N")),
            new ArtifactContentOwnershipToken(retained.Identity.OwnerToken));
        await using(var prepared=await fixture.Runtime.ChildStorage.PrepareDeleteAsync(new(retained.Identity.ObjectId),
            retained.CreateReceipt!.CurrentRevision!.Value,foreign))
            Assert.Equal(ArtifactContentMutationOutcome.Deleted,(await prepared.ExecuteAsync()).Receipt.Outcome);
        await Assert.ThrowsAsync<InvalidDataException>(()=>fixture.Runtime.Driver.RunProtectedReleaseAsync(setup.Attempt));
        var after=(await setup.Read())!;var current=(await fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!;
        Assert.Equal(before.Fence,after.Fence);Assert.Equal(ZipRetentionState.Created,current.State);
        Assert.Null(current.ReleaseReceipt);Assert.Null(current.CandidateCleanup);
        Assert.Null(await fixture.Runtime.ChildStorage.GetReceiptAsync(new(retained.Identity.ReleaseOperationId)));
        Assert.True(File.Exists(CandidatePath(fixture,retained)));
    }

    [Fact]
    public async Task Mismatched_delete_operation_receipt_denies_before_claim()
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        var before=(await setup.Read())!;var retained=(await fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!;
        var context=new ArtifactContentMutationContext(new(retained.Identity.ReleaseOperationId),
            new ArtifactContentOwnershipToken(retained.Identity.OwnerToken));
        await using(var prepared=await fixture.Runtime.ChildStorage.PrepareDeleteAsync(new(retained.Identity.ObjectId),
            new("wrong-revision"),context))
            Assert.Equal(ArtifactContentMutationOutcome.VersionConflict,(await prepared.ExecuteAsync()).Receipt.Outcome);
        await Assert.ThrowsAsync<InvalidDataException>(()=>fixture.Runtime.Driver.RunProtectedReleaseAsync(setup.Attempt));
        var after=(await setup.Read())!;var current=(await fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!;
        Assert.Equal(before.Fence,after.Fence);Assert.Equal(ZipRetentionState.Created,current.State);
        Assert.Null(current.ReleaseReceipt);Assert.Null(current.CandidateCleanup);
        Assert.True(File.Exists(CandidatePath(fixture,retained)));
        await using var physical=await fixture.Runtime.ChildStorage.ReadProtectedAsync(new(retained.Identity.ObjectId),
            retained.CreateReceipt!.CurrentRevision!.Value);
        Assert.NotNull(physical);
    }

    [Fact]
    public async Task Durable_admission_stop_denies_release_with_live_root()
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        var before=(await setup.Read())!;
        await fixture.Journal.AppendAdmissionEventAsync(fixture.Admission,
            new("release-stop",ZipAdmissionEventKind.Revoked,"synthetic",fixture.Clock.Now,SafeReason:"Stopped"));
        await Assert.ThrowsAnyAsync<Exception>(()=>fixture.Runtime.Driver.RunProtectedReleaseAsync(setup.Attempt));
        var after=(await setup.Read())!;var retained=(await fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!;
        Assert.Equal(before.Fence,after.Fence);Assert.Equal(ZipRetentionState.Created,retained.State);
        Assert.Null(retained.ReleaseReceipt);Assert.True(File.Exists(CandidatePath(fixture,retained)));
    }

    [Fact]
    public async Task Expired_owner_lease_denies_before_release_claim()
    {
        await using var setup=await AcknowledgedAsync(rootLife:TimeSpan.FromHours(2));var fixture=setup.Fixture;
        var before=(await setup.Read())!;fixture.Clock.Now=before.OwnerUntil;
        await Assert.ThrowsAnyAsync<Exception>(()=>fixture.Runtime.Driver.RunProtectedReleaseAsync(setup.Attempt));
        var after=(await setup.Read())!;var retained=(await fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!;
        Assert.Equal(before.Fence,after.Fence);Assert.Equal(ZipRetentionState.Created,retained.State);
        Assert.Null(retained.ReleaseReceipt);Assert.True(File.Exists(CandidatePath(fixture,retained)));
    }

    [Fact]
    public async Task Expired_durable_admission_evidence_denies_with_live_recovery_root_and_lease()
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        await setup.Attempt.DisposeAsync();await setup.Root.DisposeAsync();
        var expires=fixture.Admission.LatestAuthorityEvidence!.ExpiresUtc;
        fixture.Clock.Now=expires.AddMinutes(-1);
        await using var root=new SyntheticZipExecutionGrant(fixture.Admission,ZipOwnershipPurpose.Recovery,
            fixture.Clock,TimeSpan.FromHours(2));
        await using var recovery=await ZipExecutionAttempt.ClaimAsync(
            new(fixture.Journal,fixture.Journal,fixture.Profile,fixture.Clock),root,
            await fixture.RequestAsync(root.Purpose),fixture.Clock);
        var before=(await setup.Read())!;
        fixture.Clock.Now=expires;
        await Assert.ThrowsAnyAsync<Exception>(()=>fixture.Runtime.Driver.RunProtectedReleaseAsync(recovery));
        var after=(await setup.Read())!;var retained=(await fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!;
        Assert.Equal(before.Fence,after.Fence);Assert.Equal(ZipRetentionState.Created,retained.State);
        Assert.Null(retained.ReleaseReceipt);Assert.True(File.Exists(CandidatePath(fixture,retained)));
    }

    [Fact]
    public async Task Parent_review_state_blocks_release_without_receipt_or_candidate_cleanup()
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        var before=(await setup.Read())!;
        await fixture.Journal.ReviewAsync(before.Fence,"synthetic-review");
        var result=await fixture.Runtime.Driver.RunProtectedReleaseAsync(setup.Attempt);
        Assert.Equal(ZipProtectedReleaseStop.OutsideProtectedReleaseBoundary,result.Stop);
        var retained=(await fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!;
        Assert.Equal(ZipRetentionState.Created,retained.State);Assert.Null(retained.ReleaseReceipt);
        Assert.Null(retained.CandidateCleanup);Assert.True(File.Exists(CandidatePath(fixture,retained)));
    }

    [Theory]
    [InlineData("root")][InlineData("scope")]
    public async Task Expiry_at_protected_catalog_commit_prevents_delete(string target)
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        var start=fixture.Clock.Now;
        if(target=="scope")setup.Root.EffectLifetime=TimeSpan.FromSeconds(1);
        fixture.Runtime.ChildStorage.PhysicalCheckpoint=point=>
        {if(point=="BeforeCommit")fixture.Clock.Now=target=="root"?setup.Root.ValidUntilUtc:start.AddSeconds(1);
            return Task.CompletedTask;};
        await Assert.ThrowsAnyAsync<Exception>(()=>fixture.Runtime.Driver.RunProtectedReleaseAsync(setup.Attempt));
        var retained=(await fixture.Journal.ReadRetentionAsync(fixture.Admission.OperationId,0))!;
        Assert.Equal(ZipRetentionState.ReleasePending,retained.State);
        Assert.Null(retained.ReleaseReceipt);Assert.Null(retained.CandidateCleanup);
        Assert.Null(await fixture.Runtime.ChildStorage.GetReceiptAsync(new(retained.Identity.ReleaseOperationId)));
        Assert.True(File.Exists(CandidatePath(fixture,retained)));
    }

    [Fact]
    public async Task Recovery_rejects_changed_current_tombstone_even_when_delete_receipt_survives()
    {
        await using var setup=await AcknowledgedAsync();var fixture=setup.Fixture;
        await fixture.Runtime.Driver.RunProtectedReleaseAsync(setup.Attempt);
        var before=(await fixture.Journal.ReadRetentionAsync(fixture.Admission.OperationId,0))!;
        var context=new ArtifactContentMutationContext(new("foreign-"+Guid.NewGuid().ToString("N")),
            new ArtifactContentOwnershipToken(before.Identity.OwnerToken));
        await using(var prepared=await fixture.Runtime.ChildStorage.PrepareCreateAsync(new(before.Identity.ObjectId),
            "foreign protected content"u8.ToArray(),context))
            Assert.Equal(ArtifactContentMutationOutcome.Created,(await prepared.ExecuteAsync()).Receipt.Outcome);
        await using var root=new SyntheticZipExecutionGrant(fixture.Admission,ZipOwnershipPurpose.Recovery,fixture.Clock);
        await using var recovery=await ZipExecutionAttempt.ClaimAsync(
            new(fixture.Journal,fixture.Journal,fixture.Profile,fixture.Clock),root,
            await fixture.RequestAsync(root.Purpose),fixture.Clock);
        await Assert.ThrowsAsync<InvalidDataException>(()=>fixture.Runtime.Driver.RunProtectedReleaseAsync(recovery));
        var after=(await fixture.Journal.ReadRetentionAsync(fixture.Admission.OperationId,0))!;
        Assert.Equal(before,after);
        Assert.Equal(ZipRetentionState.ReleasePending,after.State);
        Assert.Null(after.CandidateCleanup);Assert.True(File.Exists(CandidatePath(fixture,before)));
    }
}

using EMF.Core.Contracts.Zip;
using EMF.Orchestration.Services;
using EMF.Persistence.Repositories;
using EMF.Tests.TestInfrastructure;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EMF.Tests;

[Collection(ZipProcessWideGateCollection.Name)]
public sealed class ZipAcknowledgementAuthorityTests
{
    private static async Task<ZipScannerAuthorityTests.Setup> IngestedAsync()
    {
        var setup=await ZipScannerAuthorityTests.Setup.Create();
        await setup.Scan();
        setup.Root.GuardedChildOpener=(p,e,gate,ct)=>setup.Fixture.Runtime.Host.OpenGuardedAsync(p,e,gate,ct);
        Assert.Equal(ZipChildIngestionStop.Ingested,
            (await setup.Fixture.Runtime.Driver.RunChildIngestionAsync(setup.Attempt)).Stop);
        return setup;
    }

    private static async Task<long> CountAsync(ZipOwnershipFixture f,string table)
    {
        await using var c=new SqliteConnection("Data Source="+f.Evidence.DatabasePath);
        await c.OpenAsync();using var q=c.CreateCommand();
        q.CommandText=table switch
        {
            "Relationships"=>"SELECT COUNT(*) FROM Relationships",
            "ACK"=>"SELECT COUNT(*) FROM ZipExtractionAcknowledgements",
            _=>throw new ArgumentOutOfRangeException(nameof(table))
        };
        return Convert.ToInt64(await q.ExecuteScalarAsync());
    }

    private static async Task NoAckAsync(ZipScannerAuthorityTests.Setup s,ZipParentSnapshot before)
    {
        var after=(await s.Read())!;
        Assert.Equal(before.Fence,after.Fence);
        Assert.Equal(before.Budget,after.Budget);
        Assert.Equal(ZipEntryState.Ingested,after.Entries.Single().State);
        Assert.Equal(0,await CountAsync(s.Fixture,"ACK"));
        Assert.Equal(0,await CountAsync(s.Fixture,"Relationships"));
    }

    [Fact]
    public async Task Live_ack_commits_composite_transaction_and_stops_before_release()
    {
        await using var s=await IngestedAsync();var before=(await s.Read())!;
        var result=await s.Fixture.Runtime.Driver.RunAcknowledgementAsync(s.Attempt);
        Assert.Equal(ZipAcknowledgementStop.Acknowledged,result.Stop);
        Assert.False(result.AlreadyAcknowledged);
        Assert.Equal(ZipEntryState.Acknowledged,result.Parent.Entries.Single().State);
        Assert.Equal(0,result.Parent.Fence.ConfirmedOrdinal);
        Assert.Equal(before.Fence.Revision+1,result.Parent.Fence.Revision);
        Assert.Equal(before.Budget,result.Parent.Budget);
        Assert.Equal(1,await CountAsync(s.Fixture,"ACK"));
        Assert.Equal(2,await CountAsync(s.Fixture,"Relationships"));
        Assert.Equal(ZipRetentionState.Created,(await s.Fixture.Journal.ReadRetentionAsync(before.Binding.OperationId,0))!.State);
        Assert.NotEqual(result.ContainsId,result.DerivedFromId);
    }

    [Fact]
    public async Task Fresh_recovery_reuses_committed_ack_without_new_rows_or_frontier_change()
    {
        await using var s=await IngestedAsync();var first=await s.Fixture.Runtime.Driver.RunAcknowledgementAsync(s.Attempt);
        await using var grant=new SyntheticZipExecutionGrant(s.Fixture.Admission,ZipOwnershipPurpose.Recovery,s.Fixture.Clock);
        await using var attempt=await ZipExecutionAttempt.ClaimAsync(
            new(s.Fixture.Journal,s.Fixture.Journal,s.Fixture.Profile,s.Fixture.Clock),grant,
            await s.Fixture.RequestAsync(grant.Purpose),s.Fixture.Clock);
        var claimed=(await s.Read())!;
        var replay=await s.Fixture.Runtime.Driver.RunAcknowledgementAsync(attempt);
        Assert.Equal(ZipAcknowledgementStop.Acknowledged,replay.Stop);
        Assert.True(replay.AlreadyAcknowledged);
        Assert.Equal(first.ContainsId,replay.ContainsId);
        Assert.Equal(first.DerivedFromId,replay.DerivedFromId);
        Assert.Equal(claimed.Fence,replay.Parent.Fence);
        Assert.Equal(claimed.Budget,replay.Parent.Budget);
        Assert.Equal(1,await CountAsync(s.Fixture,"ACK"));
        Assert.Equal(2,await CountAsync(s.Fixture,"Relationships"));
        await using var forbidden=new SyntheticZipExecutionGrant(s.Fixture.Admission,ZipOwnershipPurpose.InitialExecution,s.Fixture.Clock);
        var forbiddenRequest=await s.Fixture.RequestAsync(forbidden.Purpose);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(()=>ZipExecutionAttempt.ClaimAsync(
            new(s.Fixture.Journal,s.Fixture.Journal,s.Fixture.Profile,s.Fixture.Clock),forbidden,
            forbiddenRequest,s.Fixture.Clock));
    }

    [Theory]
    [InlineData("root-revoked")][InlineData("root-expired")]
    [InlineData("scope-revoked")][InlineData("scope-expired")]
    [InlineData("root-revoked-at-accept")][InlineData("scope-revoked-at-accept")]
    [InlineData("root-expired-at-accept")][InlineData("scope-expired-at-accept")]
    public async Task Revocation_or_expiry_before_commit_rolls_back_every_ack_effect(string caseName)
    {
        await using var s=await IngestedAsync();var before=(await s.Read())!;
        if(caseName.StartsWith("scope-expired",StringComparison.Ordinal))s.Root.EffectLifetime=TimeSpan.FromSeconds(1);
        var start=s.Fixture.Clock.Now;
        s.Fixture.Journal.AcknowledgementCheckpoint=point=>
        {
            if(point!=(caseName.EndsWith("-at-accept",StringComparison.Ordinal)?"CommitWindow":"Frontier"))return;
            switch(caseName)
            {
                case "root-revoked":s.Root.Revoke();break;
                case "root-expired":s.Fixture.Clock.Now=s.Root.ValidUntilUtc;break;
                case "scope-revoked":s.Root.RevokeCurrentEffect!();break;
                case "scope-expired":s.Fixture.Clock.Now=start.AddSeconds(1);break;
                case "root-revoked-at-accept":s.Root.Revoke();break;
                case "scope-revoked-at-accept":s.Root.RevokeCurrentEffect!();break;
                case "root-expired-at-accept":s.Fixture.Clock.Now=s.Root.ValidUntilUtc;break;
                case "scope-expired-at-accept":s.Fixture.Clock.Now=start.AddSeconds(1);break;
            }
        };
        await Assert.ThrowsAnyAsync<Exception>(()=>s.Fixture.Runtime.Driver.RunAcknowledgementAsync(s.Attempt));
        await NoAckAsync(s,before);
    }

    [Theory]
    [InlineData("parent")][InlineData("fence")][InlineData("ordinal")][InlineData("central")]
    [InlineData("proof")][InlineData("admission")]
    public async Task Guarded_journal_rejects_changed_request_without_side_effects(string part)
    {
        await using var s=await IngestedAsync();var before=(await s.Read())!;
        var entry=before.Entries.Single();
        await using var guard=s.Attempt.AcknowledgementBoundary(()=>before,0);
        var request=new ZipAcknowledgementAuthorityRequest(s.Fixture.Admission,before.Binding,before.Fence,
            entry.Plan,entry.Retained!,entry.Ingestion!,s.Attempt.ExecutingActorId,s.Attempt.Purpose,s.Attempt.ValidUntilUtc);
        request=part switch
        {
            "parent"=>request with{Parent=request.Parent with{OperationId="unrelated"}},
            "fence"=>request with{Fence=request.Fence with{Revision=request.Fence.Revision+1}},
            "ordinal"=>request with{Occurrence=request.Occurrence with{FileOrdinal=1}},
            "central"=>request with{Occurrence=request.Occurrence with{CentralOrdinal=request.Occurrence.CentralOrdinal+1}},
            "proof"=>request with{Ingestion=request.Ingestion with{Sha256=new string('A',64)}},
            _=>request with{Admission=request.Admission with{Revision=request.Admission.Revision+1}}
        };
        await Assert.ThrowsAnyAsync<Exception>(()=>s.Fixture.Journal.AcknowledgeEntryAsync(request,guard));
        await NoAckAsync(s,before);
    }

    [Fact]
    public async Task Durable_admission_change_before_commit_rolls_back_ack()
    {
        await using var s=await IngestedAsync();var before=(await s.Read())!;
        // A separate writer cannot change admission while the ACK write transaction
        // owns SQLite's write lock. Change it before the call; the final read rejects it.
        await s.Fixture.Journal.AppendAdmissionEventAsync(s.Fixture.Admission,
            new("ack-stop",ZipAdmissionEventKind.Revoked,"synthetic",s.Fixture.Clock.Now,SafeReason:"Stopped"));
        await Assert.ThrowsAnyAsync<Exception>(()=>s.Fixture.Runtime.Driver.RunAcknowledgementAsync(s.Attempt));
        await NoAckAsync(s,before);
    }
}

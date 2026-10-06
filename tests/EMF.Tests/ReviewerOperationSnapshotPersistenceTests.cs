using System.Text.Json;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ReviewerOperationSnapshotPersistenceTests
{
    private const string Table = "VeteransClaims_ReviewerOperationSnapshots";
    private static readonly OperationSnapshotId Snapshot = new("snapshot");
    private static readonly ReviewerOperationId Operation = new("operation");
    private static readonly ReviewerSnapshotOwnerToken Owner = new("owner");
    private static readonly ReviewerSnapshotOwnerToken NextOwner = new("next-owner");
    private static readonly string Hash = new('A', 64);
    private static readonly string OtherHash = new('B', 64);
    private static ReviewerSnapshotCandidate Candidate(string? hash = null) =>
        new(new(Snapshot, hash ?? Hash), ReviewerRetainedValidator.Profile, 1);
    private static ReviewerSnapshotReadyEvidence Evidence(string? hash = null) => new(Candidate(hash), 1);

    private static readonly Lazy<Task<byte[]>> EmptyDatabaseTemplate =
        new(CreateEmptyDatabaseTemplateAsync);

    private static async Task<byte[]> CreateEmptyDatabaseTemplateAsync()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "emf-reviewer-authority-template-" + Guid.NewGuid().ToString("N") + ".db");

        try
        {
            await new VeteransClaimsSqliteSchema(path).InitializeAsync();
            using var connection = VeteransClaimsSqliteConnectionFactory.Create(path);
            SqliteConnection.ClearPool(connection);
            return await File.ReadAllBytesAsync(path);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-wal", "-shm" })
                File.Delete(path + suffix);
        }
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "emf-reviewer-authority-" + Guid.NewGuid().ToString("N") + ".db");
        public SqliteReviewerOperationSnapshotRepository Repository => new(Path);
        public static async Task<Fixture> Create()
        {
            var f = new Fixture();
            await File.WriteAllBytesAsync(f.Path, await EmptyDatabaseTemplate.Value);
            await new VeteransClaimsSqliteSchema(f.Path).InitializeAsync();
            return f;
        }
        public async Task<ReviewerOperationSnapshotRecord> Seed(ReviewerOperationSnapshotState state = ReviewerOperationSnapshotState.Capturing)
        {
            var row = await Repository.CreateCapturingAsync(Snapshot, Operation, Owner);
            if (state != ReviewerOperationSnapshotState.Capturing)
                row = await Repository.BindAsync(Snapshot, Operation, row.Revision, Owner, Candidate());
            if (state == ReviewerOperationSnapshotState.Ready)
                row = await Repository.ReadyAsync(Snapshot, Operation, row.Revision, Owner, Evidence());
            return row;
        }
        public async Task Sql(string sql)
        {
            await using var c = new SqliteConnection($"Data Source={Path};Pooling=False");
            await c.OpenAsync();
            await using var command = c.CreateCommand(); command.CommandText = sql;
            await command.ExecuteNonQueryAsync();
        }
        public async Task<string> Dump()
        {
            await using var c = new SqliteConnection($"Data Source={Path};Pooling=False");
            await c.OpenAsync();
            await using var command = c.CreateCommand(); command.CommandText = $"SELECT * FROM {Table} ORDER BY OperationSnapshotId";
            await using var reader = await command.ExecuteReaderAsync();
            var rows = new List<object[]>();
            while (await reader.ReadAsync())
            {
                var values = new object[reader.FieldCount]; reader.GetValues(values);
                rows.Add(values.Select(x => x is DBNull ? null! : x).ToArray());
            }
            return JsonSerializer.Serialize(rows);
        }
        public async Task Reject(Func<Task> action)
        {
            var before = await Dump();
            Assert.NotNull(await Record.ExceptionAsync(action));
            Assert.Equal(before, await Dump());
        }
        public async Task RejectSql(string sql)
        {
            var before = await Dump();
            var error = await Assert.ThrowsAsync<SqliteException>(() => Sql(sql));
            Assert.Equal(19, error.SqliteErrorCode);
            Assert.Equal(before, await Dump());
        }
        public ValueTask DisposeAsync()
        {
            using var connection = VeteransClaimsSqliteConnectionFactory.Create(Path);
            SqliteConnection.ClearPool(connection);
            foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(Path + suffix);
            return ValueTask.CompletedTask;
        }
    }

    [Fact]
    public async Task Create_initial_shape_and_replays_return_actual_authority_without_writing()
    {
        await using var f = await Fixture.Create(); var repo = f.Repository;
        Assert.Null(await repo.ReadAsync(Snapshot)); Assert.Null(await repo.ReadByReviewerOperationAsync(Operation));
        var row = await f.Seed();
        Assert.Equal(new ReviewerOperationSnapshotRecord(Snapshot, Operation, ReviewerOperationSnapshotState.Capturing,
            1, Owner, ReviewerRetainedValidator.Profile, 1, null, null, ReviewerOperationSnapshotDisposition.Active, null), row);
        Assert.False(row.IsConsumable);
        async Task Replay(ReviewerOperationSnapshotRecord expected)
        {
            var before = await f.Dump();
            Assert.Equal(expected, await repo.CreateCapturingAsync(Snapshot, Operation, NextOwner));
            Assert.Equal(expected, await repo.ReadByReviewerOperationAsync(Operation));
            Assert.Equal(before, await f.Dump());
        }
        await Replay(row);
        row = await repo.TransferOwnershipAsync(Snapshot, Operation, 1, Owner, NextOwner);
        await Replay(row);
        row = await repo.BindAsync(Snapshot, Operation, 2, NextOwner, Candidate()); await Replay(row);
        row = await repo.ReadyAsync(Snapshot, Operation, 3, NextOwner, Evidence()); await Replay(row);
        row = await repo.RecordReviewOrFailureAsync(Snapshot, Operation, row.State, 4, NextOwner,
            ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid); await Replay(row);
    }

    [Theory]
    [InlineData(1)] [InlineData(2)]
    public async Task Create_replay_preserves_terminal_capture_outcome(int category)
    {
        await using var f = await Fixture.Create(); var row = await f.Seed();
        row = await f.Repository.RecordReviewOrFailureAsync(Snapshot, Operation, row.State, row.Revision, Owner,
            (ReviewerOperationSnapshotFailureCategory)category);
        var before = await f.Dump();
        Assert.Equal(row, await f.Repository.CreateCapturingAsync(Snapshot, Operation, NextOwner));
        Assert.Equal(before, await f.Dump());
    }

    [Fact]
    public async Task Create_identity_cross_collision_and_profile_version_conflicts_preserve_all_rows()
    {
        await using var f = await Fixture.Create(); var r = f.Repository; await f.Seed();
        await r.CreateCapturingAsync(new("snapshot-2"), new("operation-2"), Owner);
        await f.Reject(() => r.CreateCapturingAsync(Snapshot, new("operation-3"), Owner));
        await f.Reject(() => r.CreateCapturingAsync(new("snapshot-3"), Operation, Owner));
        await f.Reject(() => r.CreateCapturingAsync(Snapshot, new("operation-2"), Owner));
        foreach (var ids in new[] { (Snapshot, Operation), (new OperationSnapshotId("new"), new ReviewerOperationId("new")) })
        {
            await f.Reject(() => r.CreateCapturingAsync(ids.Item1, ids.Item2, Owner, "unsupported", 1));
            await f.Reject(() => r.CreateCapturingAsync(ids.Item1, ids.Item2, Owner, ReviewerRetainedValidator.Profile, 2));
        }
    }

    [Theory]
    [InlineData("create", true)] [InlineData("create", false)]
    [InlineData("bind", true)] [InlineData("bind", false)]
    [InlineData("ready", true)] [InlineData("ready", false)]
    [InlineData("transfer", false)]
    public async Task Concurrent_mutations_have_one_authoritative_write_and_exact_replay_is_read_only(string mutation, bool identical)
    {
        await using var f = await Fixture.Create();
        if (mutation != "create") await f.Seed(mutation == "ready" ? ReviewerOperationSnapshotState.Materializing : ReviewerOperationSnapshotState.Capturing);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<ReviewerOperationSnapshotRecord> Attempt(bool second)
        {
            await gate.Task;
            var r = f.Repository;
            return mutation switch
            {
                "create" => await r.CreateCapturingAsync(Snapshot, second && !identical ? new("conflicting-operation") : Operation, Owner),
                "bind" => await r.BindAsync(Snapshot, Operation, 1, Owner, Candidate(second && !identical ? OtherHash : Hash)),
                "ready" => await r.ReadyAsync(Snapshot, Operation, 2, Owner, Evidence(second && !identical ? OtherHash : Hash)),
                _ => await r.TransferOwnershipAsync(Snapshot, Operation, 1, Owner, second ? new("other-owner") : NextOwner)
            };
        }
        var a = Attempt(false); var b = Attempt(true); gate.SetResult();
        var errors = await Task.WhenAll(Record.ExceptionAsync(async () => await a), Record.ExceptionAsync(async () => await b));
        Assert.Equal(identical ? 0 : 1, errors.Count(x => x is not null));
        var row = (await f.Repository.ReadAsync(Snapshot))!;
        Assert.Equal(mutation == "create" ? 1 : mutation == "ready" ? 3 : 2, row.Revision);
        if (identical) { Assert.Equal(row, await a); Assert.Equal(row, await b); }
    }

    [Fact]
    public async Task Bind_exact_shape_replay_and_invalid_candidate_or_preconditions_preserve_authority()
    {
        await using var f = await Fixture.Create(); var r = f.Repository; var before = await f.Seed();
        await f.Reject(() => r.BindAsync(Snapshot, Operation, 0, Owner, Candidate()));
        await f.Reject(() => r.BindAsync(Snapshot, Operation, 1, NextOwner, Candidate()));
        await f.Reject(() => r.BindAsync(Snapshot, new("foreign-operation"), 1, Owner, Candidate()));
        await f.Reject(() => r.BindAsync(Snapshot, Operation, 1, Owner, new(new(new("foreign-snapshot"), Hash), ReviewerRetainedValidator.Profile, 1)));
        await f.Reject(() => r.BindAsync(Snapshot, Operation, 1, Owner, Candidate() with { Profile = "unsupported" }));
        await f.Reject(() => r.BindAsync(Snapshot, Operation, 1, Owner, Candidate() with { RepresentationVersion = 2 }));
        foreach (var hash in new[] { "", new string('a', 64), new string('G', 64), new string('A', 63) })
            await f.Reject(() => r.BindAsync(Snapshot, Operation, 1, Owner, Candidate(hash)));
        var bound = await r.BindAsync(Snapshot, Operation, 1, Owner, Candidate());
        Assert.Equal(before with { State = ReviewerOperationSnapshotState.Materializing, Revision = 2, BundleSha256 = Hash }, bound);
        var dump = await f.Dump();
        Assert.Equal(bound, await f.Repository.BindAsync(Snapshot, Operation, 1, Owner, Candidate()));
        Assert.Equal(dump, await f.Dump());
        await f.Reject(() => r.BindAsync(Snapshot, Operation, 1, Owner, Candidate(OtherHash)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Ready_exact_shape_replay_and_invalid_evidence_preserve_authority(bool reviewAfterReady)
    {
        await using var f = await Fixture.Create(); var r = f.Repository;
        await f.Seed();
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, 1, Owner, Evidence()));
        var materializing = await r.BindAsync(Snapshot, Operation, 1, Owner, Candidate());
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, 1, Owner, Evidence()));
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, 2, NextOwner, Evidence()));
        await f.Reject(() => r.ReadyAsync(Snapshot, new("wrong"), 2, Owner, Evidence()));
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, 2, Owner, Evidence(OtherHash)));
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, 2, Owner, Evidence() with { ValidationVersion = 2 }));
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, 2, Owner, Evidence() with { Candidate = Candidate() with { Profile = "wrong" } }));
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, 2, Owner, Evidence() with { Candidate = Candidate() with { RepresentationVersion = 2 } }));
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, 2, Owner, null!));
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, 2, Owner, new(null!, 1)));
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, 2, Owner, new(Candidate() with { Reference = null! }, 1)));
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, 2, Owner, Evidence() with { Candidate = Candidate() with { Reference = new(new("foreign"), Hash) } }));
        var ready = await r.ReadyAsync(Snapshot, Operation, 2, Owner, Evidence());
        Assert.Equal(materializing with { State = ReviewerOperationSnapshotState.Ready, Revision = 3, ReadyValidationVersion = 1 }, ready);
        Assert.True(ready.IsConsumable);
        if (reviewAfterReady)
        {
            ready = await r.RecordReviewOrFailureAsync(Snapshot, Operation, ready.State, 3, Owner, ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid);
            Assert.False(ready.IsConsumable); Assert.Equal(Hash, ready.BundleSha256); Assert.Equal(1, ready.ReadyValidationVersion);
        }
        var dump = await f.Dump();
        Assert.Equal(ready, await f.Repository.ReadyAsync(Snapshot, Operation, 2, Owner, Evidence()));
        Assert.Equal(dump, await f.Dump());
    }

    [Theory]
    [InlineData(ReviewerOperationSnapshotState.Capturing)]
    [InlineData(ReviewerOperationSnapshotState.Materializing)]
    public async Task Ownership_transfer_changes_only_token_and_revision_and_fences_former_owner(ReviewerOperationSnapshotState state)
    {
        await using var f = await Fixture.Create(); var row = await f.Seed(state); var r = f.Repository;
        await f.Reject(() => r.TransferOwnershipAsync(Snapshot, Operation, row.Revision, Owner, Owner));
        await f.Reject(() => r.TransferOwnershipAsync(Snapshot, Operation, row.Revision - 1, Owner, NextOwner));
        await f.Reject(() => r.TransferOwnershipAsync(Snapshot, Operation, row.Revision, NextOwner, new("another")));
        await f.Reject(() => r.TransferOwnershipAsync(Snapshot, new("wrong"), row.Revision, Owner, NextOwner));
        var transferred = await r.TransferOwnershipAsync(Snapshot, Operation, row.Revision, Owner, NextOwner);
        Assert.Equal(row with { OwnerToken = NextOwner, Revision = row.Revision + 1 }, transferred);
        if (state == ReviewerOperationSnapshotState.Capturing)
            await f.Reject(() => r.BindAsync(Snapshot, Operation, transferred.Revision, Owner, Candidate()));
        else
        {
            var beforeReplay = await f.Dump();
            Assert.Equal(transferred, await r.BindAsync(Snapshot, Operation, row.Revision - 1, Owner, Candidate()));
            Assert.Equal(beforeReplay, await f.Dump());
        }
        await f.Reject(() => r.ReadyAsync(Snapshot, Operation, transferred.Revision, Owner, Evidence()));
        var dump = await f.Dump();
        Assert.Equal(transferred, await f.Repository.ReadAsync(Snapshot));
        Assert.Equal(transferred, await f.Repository.TransferOwnershipAsync(Snapshot, Operation, row.Revision, Owner, NextOwner));
        Assert.Equal(dump, await f.Dump()); // Reconcile a lost transfer response without another mutation.
    }

    [Theory]
    [InlineData(ReviewerOperationSnapshotState.Capturing, 1)]
    [InlineData(ReviewerOperationSnapshotState.Capturing, 2)]
    [InlineData(ReviewerOperationSnapshotState.Materializing, 3)]
    [InlineData(ReviewerOperationSnapshotState.Ready, 3)]
    public async Task Terminal_outcomes_are_write_once_state_specific_and_block_all_advancement(ReviewerOperationSnapshotState state, int category)
    {
        await using var f = await Fixture.Create(); var r = f.Repository; var row = await f.Seed(state);
        var legal = (ReviewerOperationSnapshotFailureCategory)category;
        foreach (var invalid in new[] { 0, 1, 2, 3, 4 }.Where(x => state == ReviewerOperationSnapshotState.Capturing ? x is not (1 or 2) : x != 3))
            await f.Reject(() => r.RecordReviewOrFailureAsync(Snapshot, Operation, state, row.Revision, Owner, (ReviewerOperationSnapshotFailureCategory)invalid));
        await f.Reject(() => r.RecordReviewOrFailureAsync(Snapshot, Operation, state, row.Revision - 1, Owner, legal));
        await f.Reject(() => r.RecordReviewOrFailureAsync(Snapshot, Operation, state, row.Revision, NextOwner, legal));
        await f.Reject(() => r.RecordReviewOrFailureAsync(Snapshot, Operation, (ReviewerOperationSnapshotState)99, row.Revision, Owner, legal));
        await f.Reject(() => r.RecordReviewOrFailureAsync(Snapshot, Operation,
            state == ReviewerOperationSnapshotState.Ready ? ReviewerOperationSnapshotState.Materializing : ReviewerOperationSnapshotState.Ready,
            row.Revision, Owner, legal));
        var terminal = await r.RecordReviewOrFailureAsync(Snapshot, Operation, state, row.Revision, Owner, legal);
        Assert.Equal(row with { Revision = row.Revision + 1, FailureCategory = legal,
            Disposition = category == 2 ? ReviewerOperationSnapshotDisposition.Failed : ReviewerOperationSnapshotDisposition.RequiresReview }, terminal);
        Assert.False(terminal.IsConsumable);
        var dump = await f.Dump();
        Assert.Equal(terminal, await f.Repository.RecordReviewOrFailureAsync(Snapshot, Operation, state, row.Revision, Owner, legal));
        Assert.Equal(dump, await f.Dump());
        await f.Reject(() => r.RecordReviewOrFailureAsync(Snapshot, Operation, state, terminal.Revision, Owner,
            category == 1 ? ReviewerOperationSnapshotFailureCategory.CaptureRejected : ReviewerOperationSnapshotFailureCategory.CaptureRecoveryAmbiguous));
        await f.Reject(() => r.TransferOwnershipAsync(Snapshot, Operation, terminal.Revision, Owner, NextOwner));
        if (state == ReviewerOperationSnapshotState.Capturing)
            await f.Reject(() => r.BindAsync(Snapshot, Operation, terminal.Revision, Owner, Candidate()));
        if (state == ReviewerOperationSnapshotState.Materializing)
            await f.Reject(() => r.ReadyAsync(Snapshot, Operation, terminal.Revision, Owner, Evidence()));
        await f.RejectSql($"UPDATE {Table} SET Disposition='Active',FailureCategory=NULL,Revision=Revision+1");
        await f.RejectSql($"UPDATE {Table} SET Disposition='{(category == 2 ? "RequiresReview" : "Failed")}',FailureCategory={(category == 2 ? 1 : 2)},Revision=Revision+1");
        await f.RejectSql($"UPDATE {Table} SET FailureCategory=2,Revision=Revision+1");
        await f.RejectSql($"UPDATE {Table} SET State='Ready',BundleSha256='{Hash}',ReadyValidationVersion=1,Disposition='RequiresReview',FailureCategory=3,Revision=Revision+1");
    }

    [Fact]
    public async Task Ready_active_ownership_transfer_rejects()
    {
        await using var f = await Fixture.Create(); var row = await f.Seed(ReviewerOperationSnapshotState.Ready);
        await f.Reject(() => f.Repository.TransferOwnershipAsync(Snapshot, Operation, row.Revision, Owner, NextOwner));
    }

    [Theory]
    [InlineData("bind")] [InlineData("ready")] [InlineData("transfer")] [InlineData("terminal")]
    public async Task Injected_SQLite_abort_rolls_back_entire_mutation_and_retry_succeeds(string mutation)
    {
        await using var f = await Fixture.Create();
        var row = await f.Seed(mutation == "ready" ? ReviewerOperationSnapshotState.Materializing : ReviewerOperationSnapshotState.Capturing);
        await f.Sql($"CREATE TRIGGER SyntheticFailure AFTER UPDATE ON {Table} BEGIN SELECT RAISE(ABORT,'synthetic mutation failure'); END");
        Task<ReviewerOperationSnapshotRecord> Mutate() => mutation switch
        {
            "bind" => f.Repository.BindAsync(Snapshot, Operation, row.Revision, Owner, Candidate()),
            "ready" => f.Repository.ReadyAsync(Snapshot, Operation, row.Revision, Owner, Evidence()),
            "transfer" => f.Repository.TransferOwnershipAsync(Snapshot, Operation, row.Revision, Owner, NextOwner),
            _ => f.Repository.RecordReviewOrFailureAsync(Snapshot, Operation, row.State, row.Revision, Owner, ReviewerOperationSnapshotFailureCategory.CaptureRejected)
        };
        await f.Reject(Mutate);
        await f.Sql("DROP TRIGGER SyntheticFailure");
        Assert.Equal(row.Revision + 1, (await Mutate()).Revision);
    }

    public static IEnumerable<object[]> Collisions()
    {
        foreach (var verb in new[] { "INSERT", "INSERT OR REPLACE", "INSERT OR IGNORE" })
            foreach (var collision in new[] { "snapshot", "operation", "cross" }) yield return [verb, collision, ""];
        foreach (var target in new[] { "OperationSnapshotId", "ReviewerOperationId" })
            foreach (var action in new[] { "DO UPDATE SET OwnerToken='intruder',Revision=Revision+1", "DO NOTHING" })
                yield return ["INSERT", target == "OperationSnapshotId" ? "snapshot" : "operation", $" ON CONFLICT({target}) {action}"];
        yield return ["INSERT", "cross", " ON CONFLICT DO NOTHING"];
    }
    private static string Insert(string snapshot = "new-snapshot", string operation = "new-operation", string overrides = "", string verb = "INSERT", string suffix = "") =>
        $"{verb} INTO {Table} VALUES('{snapshot}','{operation}','Capturing',1,'owner','{ReviewerRetainedValidator.Profile}',1,NULL,NULL,'Active',NULL){suffix}";

    [Theory]
    [MemberData(nameof(Collisions))]
    public async Task Duplicate_insert_replace_and_upsert_abort_before_replacement_and_preserve_both_rows(string verb, string collision, string suffix)
    {
        await using var f = await Fixture.Create(); await f.Seed();
        await f.Repository.CreateCapturingAsync(new("snapshot-2"), new("operation-2"), Owner);
        var snapshot = collision is "snapshot" or "cross" ? "snapshot" : "new-snapshot";
        var operation = collision == "cross" ? "operation-2" : collision == "operation" ? "operation" : "new-operation";
        await f.RejectSql(Insert(snapshot, operation, verb: verb, suffix: suffix));
    }

    [Fact]
    public async Task Failed_multirow_insert_is_atomic_and_preserves_existing_authority()
    {
        await using var f = await Fixture.Create(); await f.Seed();
        var first = Insert();
        var duplicate = Insert("snapshot", "another-operation");
        await f.RejectSql(first + "," + duplicate[(duplicate.IndexOf("VALUES", StringComparison.Ordinal) + 6)..]);
    }

    public static IEnumerable<object[]> InvalidInitialRows()
    {
        foreach (var change in new[]
        {
            "OperationSnapshotId=NULL", "ReviewerOperationId=NULL", "OperationSnapshotId=''", "ReviewerOperationId=''", "OwnerToken=''", "OwnerToken=NULL",
            "OperationSnapshotId='bad id'", "ReviewerOperationId='bad/id'", "OwnerToken='bad token'", "OperationSnapshotId=char(65,0,66)",
            "OperationSnapshotId='" + new string('A',129) + "'", "ReviewerOperationId='" + new string('A',129) + "'", "OwnerToken='" + new string('A',129) + "'",
            "State='Unknown'", "State=NULL", "Disposition='Unknown'", "Disposition=NULL", "Revision=0", "Revision=-1", "Revision=1.5", "Revision='bad'",
            "Profile=NULL", "Profile='other'", "RepresentationVersion=2", "RepresentationVersion=NULL", "ReadyValidationVersion=1",
            "BundleSha256='" + Hash + "'", "FailureCategory=0", "FailureCategory=1", "FailureCategory=2", "FailureCategory=3",
            "State='Materializing',BundleSha256='" + Hash + "'", "State='Ready',BundleSha256='" + Hash + "',ReadyValidationVersion=1",
            "Disposition='RequiresReview',FailureCategory=1", "Disposition='Failed',FailureCategory=2", "Revision=2"
        }) yield return [change];
    }
    [Theory]
    [MemberData(nameof(InvalidInitialRows))]
    public async Task Structural_constraints_and_initial_insert_guard_reject_invalid_shapes(string changes)
    {
        await using var f = await Fixture.Create();
        // A TEMP staging table constructs invalid NEW values without UPDATE guards affecting the test.
        await f.Sql($"CREATE TABLE SyntheticRows AS SELECT * FROM {Table}");
        await f.Sql(Insert().Replace(Table, "SyntheticRows", StringComparison.Ordinal));
        await f.Sql("UPDATE SyntheticRows SET " + changes);
        await f.RejectSql($"INSERT INTO {Table} SELECT * FROM SyntheticRows");
    }

    public static IEnumerable<object[]> InvalidUpdates()
    {
        var states = new[] { ReviewerOperationSnapshotState.Capturing, ReviewerOperationSnapshotState.Materializing, ReviewerOperationSnapshotState.Ready };
        foreach (var state in states)
        {
            foreach (var change in new[] { "Revision=Revision", "Revision=Revision+2", "Revision=Revision-1", "Revision=1.5", "Revision='bad'", "Revision=Revision+1",
                "OperationSnapshotId='other',Revision=Revision+1", "ReviewerOperationId='other',Revision=Revision+1", "Profile='other',Revision=Revision+1", "RepresentationVersion=2,Revision=Revision+1",
                "OwnerToken='next-owner',Disposition='RequiresReview',FailureCategory=" + (state == ReviewerOperationSnapshotState.Capturing ? 1 : 3) + ",Revision=Revision+1" })
                yield return [state, change];
            if (state != ReviewerOperationSnapshotState.Capturing)
            {
                yield return [state, $"BundleSha256='{OtherHash}',Revision=Revision+1"];
                yield return [state, "BundleSha256=NULL,State='Capturing',ReadyValidationVersion=NULL,Revision=Revision+1"];
            }
            if (state == ReviewerOperationSnapshotState.Ready)
            {
                yield return [state, "ReadyValidationVersion=NULL,State='Materializing',Revision=Revision+1"];
                yield return [state, "ReadyValidationVersion=2,Revision=Revision+1"];
            }
        }
        yield return [ReviewerOperationSnapshotState.Capturing, $"State='Materializing',BundleSha256='{Hash}',OwnerToken='next-owner',Revision=Revision+1"];
        yield return [ReviewerOperationSnapshotState.Capturing, $"State='Materializing',BundleSha256='{Hash}',Disposition='RequiresReview',FailureCategory=3,Revision=Revision+1"];
        yield return [ReviewerOperationSnapshotState.Capturing, $"State='Ready',BundleSha256='{Hash}',ReadyValidationVersion=1,Revision=Revision+1"];
        yield return [ReviewerOperationSnapshotState.Materializing, "State='Ready',ReadyValidationVersion=1,OwnerToken='next-owner',Revision=Revision+1"];
        yield return [ReviewerOperationSnapshotState.Materializing, "State='Ready',ReadyValidationVersion=1,Disposition='RequiresReview',FailureCategory=3,Revision=Revision+1"];
        yield return [ReviewerOperationSnapshotState.Materializing, $"State='Ready',ReadyValidationVersion=1,BundleSha256='{OtherHash}',Revision=Revision+1"];
        yield return [ReviewerOperationSnapshotState.Capturing, "OwnerToken='next-owner',ReadyValidationVersion=1,Revision=Revision+1"];
    }
    [Theory]
    [MemberData(nameof(InvalidUpdates))]
    public async Task Direct_SQLite_rejects_revision_immutability_candidate_evidence_and_combined_mutations(ReviewerOperationSnapshotState state, string change)
    {
        await using var f = await Fixture.Create(); await f.Seed(state);
        await f.RejectSql($"UPDATE {Table} SET {change}");
        await f.RejectSql($"DELETE FROM {Table}");
    }


    [Theory]
    [InlineData(ReviewerOperationSnapshotState.Capturing, 1)]
    [InlineData(ReviewerOperationSnapshotState.Capturing, 2)]
    [InlineData(ReviewerOperationSnapshotState.Materializing, 3)]
    [InlineData(ReviewerOperationSnapshotState.Ready, 3)]
    public async Task Accepted_direct_SQL_mutations_preserve_every_unspecified_column(ReviewerOperationSnapshotState terminalState, int category)
    {
        await using var f = await Fixture.Create();
        var expected = await f.Seed();
        async Task Apply(string changes, ReviewerOperationSnapshotRecord next)
        {
            await f.Sql($"UPDATE {Table} SET {changes},Revision=Revision+1");
            Assert.Equal(next, await f.Repository.ReadAsync(Snapshot));
            expected = next;
        }
        await Apply("OwnerToken='next-owner'", expected with { OwnerToken = NextOwner, Revision = expected.Revision + 1 });
        if (terminalState != ReviewerOperationSnapshotState.Capturing)
        {
            await Apply($"State='Materializing',BundleSha256='{Hash}'", expected with { State = ReviewerOperationSnapshotState.Materializing, BundleSha256 = Hash, Revision = expected.Revision + 1 });
            await Apply("OwnerToken='owner'", expected with { OwnerToken = Owner, Revision = expected.Revision + 1 });
        }
        if (terminalState == ReviewerOperationSnapshotState.Ready)
            await Apply("State='Ready',ReadyValidationVersion=1", expected with { State = ReviewerOperationSnapshotState.Ready, ReadyValidationVersion = 1, Revision = expected.Revision + 1 });
        var disposition = category == 2 ? ReviewerOperationSnapshotDisposition.Failed : ReviewerOperationSnapshotDisposition.RequiresReview;
        await Apply($"Disposition='{disposition}',FailureCategory={category}", expected with { Disposition = disposition, FailureCategory = (ReviewerOperationSnapshotFailureCategory)category, Revision = expected.Revision + 1 });
    }

    [Theory]
    [InlineData("NULL")] [InlineData("''")] [InlineData("'bad'")]
    [InlineData("'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa'")]
    [InlineData("'GGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGGG'")]
    public async Task Direct_bind_rejects_incomplete_or_noncanonical_hash(string hash)
    {
        await using var f = await Fixture.Create(); await f.Seed();
        await f.RejectSql($"UPDATE {Table} SET State='Materializing',BundleSha256={hash},Revision=Revision+1");
    }

    [Theory]
    [InlineData(ReviewerOperationSnapshotState.Capturing)]
    [InlineData(ReviewerOperationSnapshotState.Materializing)]
    [InlineData(ReviewerOperationSnapshotState.Ready)]
    public async Task Int64_maximum_fences_all_mutations_without_wraparound(ReviewerOperationSnapshotState state)
    {
        await using var f = await Fixture.Create(); await f.Seed(state);
        // Install a valid high-revision historical row by temporarily removing only the update guard.
        await f.Sql($"CREATE TABLE SyntheticTrigger AS SELECT sql FROM sqlite_master WHERE type='trigger' AND name='ReviewerOperationSnapshot_AllowedUpdate'");
        await using var c = new SqliteConnection($"Data Source={f.Path};Pooling=False"); await c.OpenAsync();
        await using var command = c.CreateCommand(); command.CommandText = "SELECT sql FROM SyntheticTrigger";
        var triggerSql = (string)(await command.ExecuteScalarAsync())!;
        await f.Sql($"DROP TRIGGER ReviewerOperationSnapshot_AllowedUpdate; UPDATE {Table} SET Revision={long.MaxValue}; {triggerSql}");
        await f.Reject(() => f.Repository.TransferOwnershipAsync(Snapshot, Operation, long.MaxValue, Owner, NextOwner));
        if (state == ReviewerOperationSnapshotState.Capturing)
            await f.Reject(() => f.Repository.BindAsync(Snapshot, Operation, long.MaxValue, Owner, Candidate()));
        else
            Assert.Equal(long.MaxValue, (await f.Repository.BindAsync(Snapshot, Operation, long.MaxValue, Owner, Candidate())).Revision);
        if (state != ReviewerOperationSnapshotState.Ready)
            await f.Reject(() => f.Repository.ReadyAsync(Snapshot, Operation, long.MaxValue, Owner, Evidence()));
        else
            Assert.Equal(long.MaxValue, (await f.Repository.ReadyAsync(Snapshot, Operation, long.MaxValue, Owner, Evidence())).Revision);
        await f.Reject(() => f.Repository.RecordReviewOrFailureAsync(Snapshot, Operation, state, long.MaxValue, Owner,
            state == ReviewerOperationSnapshotState.Capturing ? ReviewerOperationSnapshotFailureCategory.CaptureRejected : ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid));
        await f.RejectSql($"UPDATE {Table} SET OwnerToken='next-owner',Revision=Revision+1");
    }
}

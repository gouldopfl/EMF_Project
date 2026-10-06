using System.Diagnostics;
using System.Text.Json;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class ReviewerOperationSnapshotProcessTests
{
    private const string WorkerEnvironment = "EMF_REVIEWER_SNAPSHOT_SYNTHETIC_WORKER_ROOT";
    private const string Marker = "EMF-REVIEWER-SNAPSHOT-SYNTHETIC-WORKER";
    private static readonly OperationSnapshotId Snapshot = new("snapshot-process");
    private static readonly ReviewerOperationId Operation = new("operation-process");
    private static readonly ReviewerSnapshotOwnerToken Owner = new("owner-process");
    private static readonly ReviewerSnapshotCandidate Candidate = new(new(Snapshot, new string('A', 64)),
        ReviewerRetainedValidator.Profile, 1);
    private sealed record WorkerRequest(string Mutation, bool BeforeCommit, bool Replay);
    private sealed record StoredResult(string SnapshotId, string OperationId, string State, long Revision,
        string OwnerToken, string Profile, int RepresentationVersion, string? BundleSha256,
        int? ReadyValidationVersion, string Disposition, int? FailureCategory);

    [Theory]
    [InlineData("Create", false)]
    [InlineData("Bind", false)]
    [InlineData("Ready", false)]
    [InlineData("Review", false)]
    [InlineData("Create", true)]
    [InlineData("Bind", true)]
    [InlineData("Ready", true)]
    [InlineData("Review", true)]
    public async Task Killed_process_preserves_atomic_authority_and_fresh_process_recognizes_lost_response(
        string mutation, bool beforeCommit)
    {
        var root = Path.Combine(Path.GetTempPath(), "emf-reviewer-snapshot-process-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var database = Path.Combine(root, "metadata.sqlite");
            await new VeteransClaimsSqliteSchema(database).InitializeAsync();
            var repository = new SqliteReviewerOperationSnapshotRepository(database);
            if (mutation != "Create") await repository.CreateCapturingAsync(Snapshot, Operation, Owner);
            if (mutation is "Ready" or "Review") await repository.BindAsync(Snapshot, Operation, 1, Owner, Candidate);
            if (mutation == "Review") await repository.ReadyAsync(Snapshot, Operation, 2, Owner, new(Candidate, 1));
            var original = await repository.ReadAsync(Snapshot);
            await File.WriteAllTextAsync(Path.Combine(root, "worker-allowed"), Marker);
            await File.WriteAllTextAsync(Path.Combine(root, "request.json"),
                JsonSerializer.Serialize(new WorkerRequest(mutation, beforeCommit, false)));
            using (var worker = Process.Start(StartInfo(root))!)
            {
                var output = worker.StandardOutput.ReadToEndAsync();
                var error = worker.StandardError.ReadToEndAsync();
                try
                {
                    var clock = Stopwatch.StartNew();
                    while (!File.Exists(Path.Combine(root, "worker-ready")))
                    {
                        if (worker.HasExited) throw new InvalidOperationException(await output + await error);
                        if (clock.Elapsed > TimeSpan.FromSeconds(45)) throw new TimeoutException("Snapshot worker checkpoint timed out.");
                        await Task.Delay(25);
                    }
                    worker.Kill(entireProcessTree: true);
                    await worker.WaitForExitAsync();
                }
                finally
                {
                    if (!worker.HasExited) { worker.Kill(true); await worker.WaitForExitAsync(); }
                    await output; await error;
                }
            }
            var reopened = new SqliteReviewerOperationSnapshotRepository(database);
            if (beforeCommit)
            {
                // The killed SQLite writer never committed: every original column survives.
                Assert.Equal(original, await reopened.ReadAsync(Snapshot));
                await Apply(reopened, mutation);
            }
            var committed = Assert.IsType<ReviewerOperationSnapshotRecord>(await reopened.ReadAsync(Snapshot));
            Assert.Equal(mutation switch { "Create" => 1L, "Bind" => 2L, "Ready" => 3L, _ => 4L }, committed.Revision);
            Assert.Equal(mutation == "Review" ? ReviewerOperationSnapshotDisposition.RequiresReview : ReviewerOperationSnapshotDisposition.Active,
                committed.Disposition);
            await File.WriteAllTextAsync(Path.Combine(root, "request.json"),
                JsonSerializer.Serialize(new WorkerRequest(mutation, false, true)));
            using (var recovery = Process.Start(StartInfo(root))!)
            {
                var output = recovery.StandardOutput.ReadToEndAsync();
                var error = recovery.StandardError.ReadToEndAsync();
                try
                {
                    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                    await recovery.WaitForExitAsync(timeout.Token);
                    Assert.True(recovery.ExitCode == 0, await output + await error);
                }
                finally
                {
                    if (!recovery.HasExited) { recovery.Kill(true); await recovery.WaitForExitAsync(); }
                    await output; await error;
                }
            }
            var result = JsonSerializer.Deserialize<StoredResult>(await File.ReadAllTextAsync(Path.Combine(root, "result.json")));
            Assert.Equal(Result(committed), result);
            Assert.Equal(committed, await Apply(new SqliteReviewerOperationSnapshotRepository(database), mutation));
            Assert.Equal(committed, await reopened.ReadByReviewerOperationAsync(Operation));
        }
        finally
        {
            using var connection = VeteransClaimsSqliteConnectionFactory.Create(Path.Combine(root, "metadata.sqlite"));
            SqliteConnection.ClearPool(connection);
            Directory.Delete(root, true);
        }
    }

    private static ProcessStartInfo StartInfo(string root)
    {
        var info = new ProcessStartInfo("dotnet")
        { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true };
        info.ArgumentList.Add("vstest");
        info.ArgumentList.Add(typeof(ReviewerOperationSnapshotProcessTests).Assembly.Location);
        info.ArgumentList.Add("/TestCaseFilter:FullyQualifiedName=EMF.Tests.ReviewerOperationSnapshotProcessTests.SyntheticSnapshotProcessWorker");
        info.Environment[WorkerEnvironment] = root;
        info.Environment.Remove("EMF_AZURE_OPENAI_LIVE");
        info.Environment["EMF_AZURE_OPENAI_LIVE_TESTS"] = "false";
        return info;
    }

    [Fact]
    public async Task SyntheticSnapshotProcessWorker()
    {
        var root = Environment.GetEnvironmentVariable(WorkerEnvironment);
        if (root is null) return;
        root = Path.GetFullPath(root);
        if (!root.StartsWith(Path.Combine(Path.GetTempPath(), "emf-reviewer-snapshot-process-"), StringComparison.Ordinal)
            || new DirectoryInfo(root).LinkTarget is not null
            || await File.ReadAllTextAsync(Path.Combine(root, "worker-allowed")) != Marker)
            throw new InvalidOperationException("Snapshot worker requires a marked synthetic temporary database.");
        Assert.Null(Environment.GetEnvironmentVariable("EMF_AZURE_OPENAI_LIVE"));
        var request = JsonSerializer.Deserialize<WorkerRequest>(await File.ReadAllTextAsync(Path.Combine(root, "request.json")))!;
        var database = Path.Combine(root, "metadata.sqlite");
        var repository = new SqliteReviewerOperationSnapshotRepository(database);
        if (request.Replay)
        {
            var result = await Apply(repository, request.Mutation);
            await File.WriteAllTextAsync(Path.Combine(root, "result.json"), JsonSerializer.Serialize(Result(result)));
            return;
        }
        if (!request.BeforeCommit)
        {
            await Apply(repository, request.Mutation);
            await File.WriteAllTextAsync(Path.Combine(root, "worker-ready"), "Committed; acknowledgement withheld.");
            await Task.Delay(Timeout.Infinite);
            return;
        }
        // Exercise process-death rollback under the actual migrated table/guards,
        // without introducing a production callback or coordinator test hook.
        await using var connection = new SqliteConnection($"Data Source={database};Pooling=False");
        await connection.OpenAsync();
        using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = request.Mutation switch
        {
            "Create" => "INSERT INTO VeteransClaims_ReviewerOperationSnapshots VALUES ($snapshot,$operation,'Capturing',1,$owner,$profile,1,NULL,NULL,'Active',NULL)",
            "Bind" => "UPDATE VeteransClaims_ReviewerOperationSnapshots SET State='Materializing',Revision=2,BundleSha256=$hash WHERE OperationSnapshotId=$snapshot AND Revision=1 AND OwnerToken=$owner",
            "Ready" => "UPDATE VeteransClaims_ReviewerOperationSnapshots SET State='Ready',Revision=3,ReadyValidationVersion=1 WHERE OperationSnapshotId=$snapshot AND Revision=2 AND OwnerToken=$owner",
            "Review" => "UPDATE VeteransClaims_ReviewerOperationSnapshots SET Revision=4,Disposition='RequiresReview',FailureCategory=3 WHERE OperationSnapshotId=$snapshot AND Revision=3 AND OwnerToken=$owner",
            _ => throw new InvalidOperationException("Unknown synthetic mutation.")
        };
        command.Parameters.AddWithValue("$snapshot", Snapshot.Value);
        command.Parameters.AddWithValue("$operation", Operation.Value);
        command.Parameters.AddWithValue("$owner", Owner.Value);
        command.Parameters.AddWithValue("$profile", ReviewerRetainedValidator.Profile);
        command.Parameters.AddWithValue("$hash", Candidate.Reference.BundleSha256);
        Assert.Equal(1, await command.ExecuteNonQueryAsync());
        await File.WriteAllTextAsync(Path.Combine(root, "worker-ready"), "Uncommitted mutation; transaction still owned.");
        await Task.Delay(Timeout.Infinite);
    }

    private static Task<ReviewerOperationSnapshotRecord> Apply(SqliteReviewerOperationSnapshotRepository repository, string mutation) => mutation switch
    {
        "Create" => repository.CreateCapturingAsync(Snapshot, Operation, Owner),
        "Bind" => repository.BindAsync(Snapshot, Operation, 1, Owner, Candidate),
        "Ready" => repository.ReadyAsync(Snapshot, Operation, 2, Owner, new(Candidate, 1)),
        "Review" => repository.RecordReviewOrFailureAsync(Snapshot, Operation, ReviewerOperationSnapshotState.Ready,
            3, Owner, ReviewerOperationSnapshotFailureCategory.RetainedMaterialInvalid),
        _ => throw new InvalidOperationException("Unknown synthetic mutation.")
    };

    private static StoredResult Result(ReviewerOperationSnapshotRecord record) => new(record.SnapshotId.Value,
        record.ReviewerOperationId.Value, record.State.ToString(), record.Revision, record.OwnerToken.Value,
        record.Profile, record.RepresentationVersion, record.BundleSha256, record.ReadyValidationVersion,
        record.Disposition.ToString(), record.FailureCategory is null ? null : (int)record.FailureCategory);
}

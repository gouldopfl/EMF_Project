using EMF.Security.Auditing.Models;
using EMF.Security.Monitoring;
using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class Adr032MonitoringTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.UnixEpoch.AddDays(20000);
    private static SecurityTelemetryExpectation Expectation(bool enabled = true, IReadOnlyList<SecurityTelemetryMaintenanceInterval>? maintenance = null) => new()
    {
        ExpectationId = "expectation-1",
        SourceId = "source-1",
        Operation = "heartbeat",
        ResourceType = "Service",
        ResourceId = "producer-1",
        EffectiveFromUtc = Start,
        MaximumSilence = TimeSpan.FromMinutes(10),
        StartupGrace = TimeSpan.FromMinutes(2),
        Enabled = enabled,
        MaintenanceIntervals = maintenance ?? [],
        Severity = SecurityAlertSeverity.High
    };
    private static SecurityAuditIntegrityAlertPolicy Policy => new() { AuditSourceId = "source-1", Severity = SecurityAlertSeverity.High };

    [Theory]
    [InlineData("UPDATE SecurityAuditRecords SET SubjectId='protected private content' WHERE Id=3", SecurityAuditIntegrityFailureCategory.RecordHashMismatch, 3, 1)]
    [InlineData("PRAGMA ignore_check_constraints=ON; UPDATE SecurityAuditRecords SET IntegrityVersion=99 WHERE Id=3", SecurityAuditIntegrityFailureCategory.UnsupportedIntegrityVersion, 3, 1)]
    [InlineData("UPDATE SecurityAuditRecords SET RecordHash=NULL WHERE Id=3", SecurityAuditIntegrityFailureCategory.MissingRecordHash, 3, 1)]
    [InlineData("UPDATE SecurityAuditRecords SET PreviousRecordHash='changed' WHERE Id=3", SecurityAuditIntegrityFailureCategory.PreviousHashMismatch, 3, 1)]
    [InlineData("UPDATE SecurityAuditRecords SET IntegrityVersion=0, PreviousRecordHash=NULL, RecordHash=NULL WHERE Id=3", SecurityAuditIntegrityFailureCategory.InvalidLegacyPlacement, 3, 1)]
    [InlineData("DELETE FROM SecurityAuditRecords WHERE Id=2", SecurityAuditIntegrityFailureCategory.PreviousHashMismatch, 3, 0)]
    [InlineData("UPDATE SecurityAuditRecords SET Id=4 WHERE Id=2", SecurityAuditIntegrityFailureCategory.PreviousHashMismatch, 3, 0)]
    public async Task Corruption_generates_dedicated_bounded_alert(string mutation, SecurityAuditIntegrityFailureCategory category, long invalidId, int prefix)
    {
        using var db = new Database();
        await db.Add(Start); await db.Mutate("UPDATE SecurityAuditRecords SET IntegrityVersion=0, PreviousRecordHash=NULL, RecordHash=NULL");
        await db.Add(Start); await db.Add(Start);
        await db.Mutate(mutation);
        var verification = await new SqliteSecurityAuditIntegrityVerifier(db.Path).VerifyAsync();
        Assert.Equal(category, verification.FailureCategory); Assert.Equal(invalidId, verification.InvalidRecordId);
        Assert.Equal(prefix, verification.ProtectedRecordCount); Assert.Equal(1, verification.LegacyRecordCount);
        Assert.Null(verification.ChainHeadHash); Assert.Null(verification.LastProtectedRecordId);
        var sink = new Sink();
        var service = new SecurityAuditMonitoringService(new SqliteSecurityAuditMonitoringReader(db.Path, "source-1"), sink);
        var result = await service.EvaluateAsync(Policy, [Expectation()], Start.AddHours(1));
        Assert.Equal(SecurityTelemetryEvaluationStatus.EvidenceUntrusted, Assert.Single(result).Status);
        var alert = Assert.Single(sink.Alerts); Assert.Equal("audit.integrity-failure", alert.AlertType);
        Assert.Equal(1, alert.EventCount); Assert.Equal(Start.AddHours(1), alert.WindowStartedUtc);
        Assert.Equal(category.ToString(), alert.Facts["FailureCategory"]); Assert.Equal(6, alert.Facts.Count);
        Assert.DoesNotContain("private", string.Join(" ", alert.Facts.Values));
        Assert.False(alert.Facts.ContainsKey("Reason")); Assert.False(alert.Facts.ContainsKey("chainHeadHash"));
        Assert.Equal(mutation.StartsWith("DELETE") ? 2 : 3, await db.Count());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Valid_chains_do_not_alert(int kind)
    {
        using var db = new Database(); await db.Initialize();
        if (kind > 0) await db.Add(Start);
        if (kind == 1) await db.Mutate("UPDATE SecurityAuditRecords SET IntegrityVersion=0, PreviousRecordHash=NULL, RecordHash=NULL");
        var sink = new Sink();
        await new SecurityAuditMonitoringService(new SqliteSecurityAuditMonitoringReader(db.Path, "source-1"), sink).EvaluateAsync(Policy, [], Start);
        Assert.Empty(sink.Alerts);
    }

    [Fact]
    public void Deadlines_are_strict_and_startup_grace_ends_at_first_observation()
    {
        var evaluator = new SecurityTelemetryExpectationEvaluator(); var expectation = Expectation();
        var initial = Start.AddMinutes(12);
        Assert.Equal(SecurityTelemetryEvaluationStatus.AwaitingFirstObservation, evaluator.Evaluate(expectation, [], initial).Status);
        Assert.Equal(SecurityTelemetryEvaluationStatus.Missing, evaluator.Evaluate(expectation, [], initial.AddTicks(1)).Status);
        SecurityTelemetryObservation[] observations = [new() { SourceId = "source-1", Operation = "heartbeat", ResourceType = "Service", ResourceId = "producer-1", RecordId = 1, OccurredUtc = Start.AddMinutes(1) }];
        Assert.Equal(SecurityTelemetryEvaluationStatus.ObservedWithinInterval, evaluator.Evaluate(expectation, observations, Start.AddMinutes(11)).Status);
        Assert.Equal(SecurityTelemetryEvaluationStatus.Missing, evaluator.Evaluate(expectation, observations, Start.AddMinutes(11).AddTicks(1)).Status);
        Assert.Equal(SecurityTelemetryEvaluationStatus.Missing, new SecurityTelemetryExpectationEvaluator().Evaluate(expectation, [], initial.AddTicks(1)).Status);
    }

    [Fact]
    public void Disabled_before_start_and_maintenance_are_distinct()
    {
        var evaluator = new SecurityTelemetryExpectationEvaluator();
        Assert.Equal(SecurityTelemetryEvaluationStatus.Disabled, evaluator.Evaluate(Expectation(false), [], Start.AddDays(1)).Status);
        Assert.Equal(SecurityTelemetryEvaluationStatus.NotExpected, evaluator.Evaluate(Expectation(), [], Start.AddTicks(-1)).Status);
        var expectation = Expectation(maintenance: [new() { StartedUtc = Start.AddMinutes(1), EndedUtc = Start.AddMinutes(20) }]);
        Assert.Equal(SecurityTelemetryEvaluationStatus.Suspended, evaluator.Evaluate(expectation, [], Start.AddMinutes(1)).Status);
        Assert.Equal(SecurityTelemetryEvaluationStatus.Suspended, evaluator.Evaluate(expectation, [], Start.AddMinutes(20).AddTicks(-1)).Status);
        Assert.Equal(SecurityTelemetryEvaluationStatus.AwaitingFirstObservation, evaluator.Evaluate(expectation, [], Start.AddMinutes(20)).Status);
        Assert.Equal(Start.AddMinutes(32), evaluator.Evaluate(expectation, [], Start.AddMinutes(20)).DeadlineUtc);
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("operation")]
    [InlineData("resource")]
    [InlineData("old")]
    [InlineData("future")]
    [InlineData("source")]
    public async Task Nonqualifying_records_do_not_satisfy(string kind)
    {
        using var db = new Database();
        await db.Add(kind == "old" ? Start.AddTicks(-1) : kind == "future" ? Start.AddHours(2) : Start,
            kind == "operation" ? "other" : "heartbeat", kind == "resource" ? "other" : "producer-1");
        if (kind == "legacy") await db.Mutate("UPDATE SecurityAuditRecords SET IntegrityVersion=0, PreviousRecordHash=NULL, RecordHash=NULL");
        var sink = new Sink();
        var result = await new SecurityAuditMonitoringService(new SqliteSecurityAuditMonitoringReader(db.Path, kind == "source" ? "source-2" : "source-1"), sink).EvaluateAsync(Policy with { AuditSourceId = kind == "source" ? "source-2" : "source-1" }, [Expectation()], Start.AddHours(1));
        Assert.Equal(kind == "source" ? SecurityTelemetryEvaluationStatus.MonitoringUnavailable : SecurityTelemetryEvaluationStatus.Missing, Assert.Single(result).Status);
        if (kind == "source") Assert.Empty(sink.Alerts); else Assert.Equal("telemetry.required-missing", Assert.Single(sink.Alerts).AlertType);
    }

    [Fact]
    public async Task Protected_observation_satisfies_and_repeated_missing_has_stable_identity()
    {
        using var db = new Database(); await db.Add(Start.AddMinutes(5)); var sink = new Sink();
        var service = new SecurityAuditMonitoringService(new SqliteSecurityAuditMonitoringReader(db.Path, "source-1"), sink);
        Assert.Equal(SecurityTelemetryEvaluationStatus.ObservedWithinInterval, Assert.Single(await service.EvaluateAsync(Policy, [Expectation()], Start.AddMinutes(6))).Status);
        await service.EvaluateAsync(Policy, [Expectation()], Start.AddHours(1)); await service.EvaluateAsync(Policy, [Expectation()], Start.AddHours(2));
        Assert.Equal(2, sink.Alerts.Count); Assert.All(sink.Alerts, a => Assert.Equal("expectation-1", a.Facts["ExpectationId"]));
        Assert.NotEqual(sink.Alerts[0].AlertId, sink.Alerts[1].AlertId);
    }

    [Fact]
    public async Task Storage_and_invalid_timestamp_are_distinct_from_missing()
    {
        using var db = new Database(); await db.Initialize();
        var service = new SecurityAuditMonitoringService(new SqliteSecurityAuditMonitoringReader(db.Path, "source-1"), new Sink());
        await db.Mutate("DROP TABLE SecurityAuditRecords");
        var unavailable = Assert.Single(await service.EvaluateAsync(Policy, [Expectation()], Start.AddHours(1)));
        Assert.Equal(SecurityTelemetryEvaluationStatus.MonitoringUnavailable, unavailable.Status); Assert.NotNull(unavailable.Failure);
        using var malformed = new Database();
        await malformed.Add(Start, timestampOverride: "invalid");
        await Assert.ThrowsAsync<SecurityTelemetryEvidenceException>(() => new SqliteSecurityAuditMonitoringReader(malformed.Path, "source-1").ReadAsync([Expectation()], Start));
    }

    [Fact]
    public async Task Sink_failure_preserves_integrity_context_and_cancellation_propagates()
    {
        using var db = new Database(); await db.Add(Start); await db.Mutate("UPDATE SecurityAuditRecords SET RecordHash=NULL");
        var error = new InvalidOperationException("delivery failed");
        var service = new SecurityAuditMonitoringService(new SqliteSecurityAuditMonitoringReader(db.Path, "source-1"), new Sink(error));
        var ex = await Assert.ThrowsAsync<SecurityAlertDeliveryException>(() => service.EvaluateAsync(Policy, [], Start));
        Assert.Same(error, ex.InnerException); Assert.Equal(SecurityAuditIntegrityFailureCategory.MissingRecordHash, ex.IntegrityFailure!.FailureCategory);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EvaluateAsync(Policy, [], Start, cts.Token));
    }

    [Fact]
    public void Invalid_configuration_and_overflow_are_rejected()
    {
        var evaluator = new SecurityTelemetryExpectationEvaluator();
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(Expectation() with { MaximumSilence = TimeSpan.Zero }, [], Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(Expectation() with { StartupGrace = TimeSpan.FromTicks(-1) }, [], Start));
        Assert.Throws<ArgumentOutOfRangeException>(() => evaluator.Evaluate(Expectation() with { EffectiveFromUtc = DateTimeOffset.MaxValue }, [], DateTimeOffset.MaxValue));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(Expectation() with { ResourceType = null }, [], Start));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(Expectation() with { ExpectationId = "secret\ntext" }, [], Start));
    }

    [Fact]
    public void New_active_baselines_and_post_maintenance_require_new_evidence()
    {
        var evaluator = new SecurityTelemetryExpectationEvaluator();
        SecurityTelemetryObservation[] old = [new() { SourceId = "source-1", Operation = "heartbeat", ResourceType = "Service", ResourceId = "producer-1", RecordId = 1, OccurredUtc = Start }];
        Assert.Equal(SecurityTelemetryEvaluationStatus.Missing, evaluator.Evaluate(Expectation() with { EffectiveFromUtc = Start.AddMinutes(1) }, old, Start.AddHours(1)).Status);
        var resumed = Expectation(maintenance: [new() { StartedUtc = Start.AddMinutes(1), EndedUtc = Start.AddMinutes(20) }]);
        Assert.Equal(SecurityTelemetryEvaluationStatus.Missing, evaluator.Evaluate(resumed, old, Start.AddHours(1)).Status);
    }

    [Fact]
    public async Task Verification_and_observations_share_snapshot_under_concurrent_mutation()
    {
        using var db = new Database(); await db.Add(Start);
        await db.Mutate("PRAGMA journal_mode=WAL");
        var mutated = false;
        var reader = new SqliteSecurityAuditMonitoringReader(db.Path, "source-1", async () =>
        {
            await db.Mutate("UPDATE SecurityAuditRecords SET Operation='tampered' WHERE Id=1");
            mutated = true;
        });
        var snapshot = await reader.ReadAsync([Expectation()], Start);
        Assert.True(mutated); Assert.True(snapshot.Integrity.IsValid);
        Assert.Equal("heartbeat", Assert.Single(snapshot.Observations).Operation);
        Assert.False((await new SqliteSecurityAuditIntegrityVerifier(db.Path).VerifyAsync()).IsValid);
    }

    [Fact]
    public async Task Reader_failure_without_active_expectations_remains_observable()
    {
        using var db = new Database();
        var service = new SecurityAuditMonitoringService(new SqliteSecurityAuditMonitoringReader(db.Path, "source-1"), new Sink());
        await Assert.ThrowsAsync<SecurityAuditMonitoringUnavailableException>(() => service.EvaluateAsync(Policy, [], Start));
        await Assert.ThrowsAsync<SecurityAuditMonitoringUnavailableException>(() => service.EvaluateAsync(Policy, [Expectation(false)], Start));
    }

    [Fact]
    public async Task Cancellation_during_delivery_is_not_wrapped_as_delivery_failure()
    {
        using var db = new Database(); await db.Add(Start); await db.Mutate("UPDATE SecurityAuditRecords SET RecordHash=NULL");
        using var cts = new CancellationTokenSource(); cts.Cancel();
        var service = new SecurityAuditMonitoringService(new SqliteSecurityAuditMonitoringReader(db.Path, "source-1"), new Sink(new OperationCanceledException(cts.Token)));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.EvaluateAsync(Policy, [], Start));
    }

    [Fact]
    public void Invalid_selector_severity_maintenance_and_identity_are_rejected()
    {
        var evaluator = new SecurityTelemetryExpectationEvaluator();
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(Expectation() with { SourceId = "" }, [], Start));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(Expectation() with { Operation = "" }, [], Start));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(Expectation() with { Severity = (SecurityAlertSeverity)99 }, [], Start));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(Expectation(maintenance: [new() { StartedUtc = Start, EndedUtc = Start }]), [], Start));
        Assert.Throws<ArgumentException>(() => evaluator.Evaluate(Expectation(maintenance: [new() { StartedUtc = Start, EndedUtc = Start.AddHours(2) }, new() { StartedUtc = Start.AddHours(1), EndedUtc = Start.AddHours(3) }]), [], Start));
    }

    [Fact]
    public async Task Invalid_chain_returns_no_observations_and_null_first_id_is_supported()
    {
        using var db = new Database(); await db.Add(Start); await db.Mutate("UPDATE SecurityAuditRecords SET RecordHash=NULL");
        var snapshot = await new SqliteSecurityAuditMonitoringReader(db.Path, "source-1").ReadAsync([Expectation()], Start);
        Assert.False(snapshot.Integrity.IsValid); Assert.Empty(snapshot.Observations);
        var alert = new SecurityAuditIntegrityAlertDetector().Build(snapshot.Integrity with { FirstInvalidRecordId = null }, Policy, Start)!;
        Assert.False(alert.Facts.ContainsKey("FirstInvalidRecordId")); Assert.Equal(5, alert.Facts.Count);
    }

    [Fact]
    public async Task Sink_failure_on_missing_preserves_alert_context()
    {
        using var db = new Database(); await db.Initialize();
        var error = new InvalidOperationException("delivery failure");
        var ex = await Assert.ThrowsAsync<SecurityAlertDeliveryException>(() => new SecurityAuditMonitoringService(new SqliteSecurityAuditMonitoringReader(db.Path, "source-1"), new Sink(error)).EvaluateAsync(Policy, [Expectation()], Start.AddHours(1)));
        Assert.Same(error, ex.InnerException); Assert.Null(ex.IntegrityFailure); Assert.Equal("telemetry.required-missing", ex.Alert.AlertType);
    }

    [Fact]
    public async Task Legacy_row_with_hash_fields_is_invalid_without_a_trusted_head()
    {
        using var db = new Database(); await db.Add(Start);
        await db.Mutate("UPDATE SecurityAuditRecords SET IntegrityVersion=0 WHERE Id=1");
        var result = await new SqliteSecurityAuditIntegrityVerifier(db.Path).VerifyAsync();
        Assert.Equal(SecurityAuditIntegrityFailureCategory.InvalidLegacyPlacement, result.FailureCategory);
        Assert.Equal(1, result.InvalidRecordId); Assert.Equal(0, result.LegacyRecordCount); Assert.Equal(0, result.ProtectedRecordCount);
        Assert.NotNull(result.FailureReason); Assert.Null(result.ChainHeadHash);
    }

    [Theory]
    [InlineData("disabled", SecurityTelemetryEvaluationStatus.Disabled)]
    [InlineData("before", SecurityTelemetryEvaluationStatus.NotExpected)]
    [InlineData("maintenance", SecurityTelemetryEvaluationStatus.Suspended)]
    public async Task Inactive_expectations_never_deliver_missing_alerts(string kind, SecurityTelemetryEvaluationStatus expected)
    {
        using var db = new Database(); await db.Initialize(); var sink = new Sink();
        var expectation = kind == "maintenance" ? Expectation(maintenance: [new() { StartedUtc = Start, EndedUtc = Start.AddHours(2) }]) : Expectation(kind != "disabled");
        var time = kind == "before" ? Start.AddTicks(-1) : Start.AddHours(1);
        var result = await new SecurityAuditMonitoringService(new SqliteSecurityAuditMonitoringReader(db.Path, "source-1"), sink).EvaluateAsync(Policy, [expectation], time);
        Assert.Equal(expected, Assert.Single(result).Status); Assert.Empty(sink.Alerts);
    }

    [Fact]
    public async Task Provider_query_failure_is_monitoring_unavailable_with_original_context()
    {
        var failure = new InvalidOperationException("synthetic provider query failure"); var sink = new Sink();
        var result = await new SecurityAuditMonitoringService(new UnavailableReader(failure), sink).EvaluateAsync(Policy, [Expectation()], Start.AddDays(1));
        var status = Assert.Single(result); Assert.Equal(SecurityTelemetryEvaluationStatus.MonitoringUnavailable, status.Status);
        Assert.Same(failure, status.Failure!.InnerException); Assert.Empty(sink.Alerts);
    }

    [Fact]
    public async Task Invalid_chain_does_not_mark_unbound_source_expectation_as_evidence_untrusted()
    {
        using var db = new Database();
        await db.Add(Start);
        await db.Mutate("UPDATE SecurityAuditRecords SET RecordHash=NULL");
        var sink = new Sink();
        var service = new SecurityAuditMonitoringService(
            new SqliteSecurityAuditMonitoringReader(db.Path, "source-2"), sink);

        var results = await service.EvaluateAsync(
            Policy with { AuditSourceId = "source-2" }, [Expectation()], Start.AddHours(1));

        var result = Assert.Single(results);
        Assert.Equal(SecurityTelemetryEvaluationStatus.MonitoringUnavailable, result.Status);
        var failure = Assert.IsType<InvalidOperationException>(result.Failure);
        Assert.Equal("Expectation source has no matching monitoring provider binding.", failure.Message);
        var alert = Assert.Single(sink.Alerts);
        Assert.Equal("audit.integrity-failure", alert.AlertType);
        Assert.Equal("source-2", alert.Facts["AuditSourceId"]);
        Assert.DoesNotContain(sink.Alerts, a => a.AlertType == "telemetry.required-missing");
    }

    private sealed class UnavailableReader(Exception failure) : ISecurityAuditMonitoringReader
    {
        public Task<SecurityAuditMonitoringSnapshot> ReadAsync(IReadOnlyList<SecurityTelemetryExpectation> expectations, DateTimeOffset evaluationUtc, CancellationToken cancellationToken = default)
            => throw new SecurityAuditMonitoringUnavailableException(failure);
    }

    private sealed class Sink(Exception? failure = null) : ISecurityAlertSink
    {
        public List<SecurityAlert> Alerts { get; } = [];
        public Task WriteAsync(SecurityAlert alert, CancellationToken cancellationToken = default)
        { cancellationToken.ThrowIfCancellationRequested(); if (failure is not null) throw failure; Alerts.Add(alert); return Task.CompletedTask; }
    }
    private sealed class Database : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"emf-adr032-{Guid.NewGuid():N}.db");
        public Task Initialize() => new SqliteSecurityAuditSink(Path).InitializeAsync();
        public async Task Add(DateTimeOffset occurred, string operation = "heartbeat", string resource = "producer-1", string? timestampOverride = null)
        {
            var sink = new SqliteSecurityAuditSink(Path); await sink.InitializeAsync();
            await sink.WriteAsync(new SecurityAuditRecord
            {
                Operation = operation,
                ResourceType = "Service",
                ResourceId = resource,
                SubjectId = "synthetic",
                Outcome = SecurityAuditOutcome.Succeeded,
                OccurredUtc = occurred,
                Facts = new Dictionary<string, string> { ["private"] = "protected medical content token key" }
            });
            if (timestampOverride is not null)
            {
                // Build valid hash-protected malformed timestamp evidence, without tampering the chain.
                await using var c = new SqliteConnection($"Data Source={Path}"); await c.OpenAsync();
                await using var q = c.CreateCommand(); q.CommandText = "SELECT FactsJson FROM SecurityAuditRecords WHERE Id=1";
                var facts = (string)(await q.ExecuteScalarAsync())!;
                var hasher = typeof(SqliteSecurityAuditSink).Assembly.GetType("EMF.Security.Persistence.Sqlite.Auditing.SecurityAuditRecordHasher")!;
                var method = hasher.GetMethod("ComputeHash", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public)!;
                var hash = (string)method.Invoke(null, [1, null, operation, "Service", resource, "synthetic", null, null, "Succeeded", timestampOverride, facts])!;
                q.CommandText = "UPDATE SecurityAuditRecords SET OccurredUtc=$time, RecordHash=$hash WHERE Id=1"; q.Parameters.AddWithValue("$time", timestampOverride); q.Parameters.AddWithValue("$hash", hash); await q.ExecuteNonQueryAsync();
            }
        }
        public async Task Mutate(string sql) { await using var c = new SqliteConnection($"Data Source={Path}"); await c.OpenAsync(); await using var q = c.CreateCommand(); q.CommandText = sql; await q.ExecuteNonQueryAsync(); }
        public async Task<long> Count() { await using var c = new SqliteConnection($"Data Source={Path}"); await c.OpenAsync(); await using var q = c.CreateCommand(); q.CommandText = "SELECT COUNT(*) FROM SecurityAuditRecords"; return (long)(await q.ExecuteScalarAsync())!; }
        public void Dispose() { SqliteConnection.ClearAllPools(); File.Delete(Path); File.Delete(Path + "-wal"); File.Delete(Path + "-shm"); }
    }
}

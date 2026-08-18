using EMF.Persistence.Repositories;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Core.Models.Workflow;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace EMF.Tests;

public sealed class PersistenceTests
{

    [Fact]
    public async Task SqliteEvidenceRepository_FindArtifactAsync_MatchesSourceAndFingerprint()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-evidence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);
            await repository.InitializeAsync();

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-find-001"),
                Name = "evidence.db",
                ArtifactType = "file",
                Fingerprint = new ContentFingerprint
                {
                    Algorithm = "SHA-256",
                    Value = "ABC123"
                }
            };

            await repository.AddArtifactWithProvenanceAsync(
                artifact,
                new Provenance
                {
                    ArtifactId = artifact.Id,
                    Source = "/data/evidence.db",
                    RecordedBy = "EMF.Tests"
                });

            var result = await repository.FindArtifactAsync(
                "/data/evidence.db",
                artifact.Fingerprint);

            Assert.NotNull(result);
            Assert.Equal(artifact.Id, result!.Id);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }



    [Fact]
    public async Task SqliteEvidenceRepository_AddArtifactWithProvenanceAsync_RollsBackOnFailure()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-evidence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);
            await repository.InitializeAsync();

            await using (var connection =
                new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={databasePath}"))
            {
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TRIGGER fail_provenance
                    BEFORE INSERT ON Provenance
                    BEGIN
                        SELECT RAISE(ABORT, 'forced provenance failure');
                    END;
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var artifactId =
                new ArtifactId("artifact-rollback-001");

            var artifact =
                new Artifact
                {
                    Id = artifactId,
                    Name = "rollback.db",
                    ArtifactType = "file"
                };

            var provenance =
                new Provenance
                {
                    ArtifactId = artifactId,
                    Source = "/data/rollback.db",
                    RecordedBy = "EMF.Tests"
                };

            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
                () => repository.AddArtifactWithProvenanceAsync(
                    artifact,
                    provenance));

            var stored =
                await repository.GetArtifactAsync(artifactId);

            Assert.Null(stored);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }


    [Fact]
    public async Task SqliteEvidenceRepository_AggregatePersistence_RollsBackOnRelationshipFailure()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-evidence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);
            await repository.InitializeAsync();

            await using (var connection =
                new Microsoft.Data.Sqlite.SqliteConnection(
                    $"Data Source={databasePath}"))
            {
                await connection.OpenAsync();

                var command = connection.CreateCommand();
                command.CommandText =
                    """
                    CREATE TRIGGER fail_relationship
                    BEFORE INSERT ON Relationships
                    BEGIN
                        SELECT RAISE(ABORT, 'forced relationship failure');
                    END;
                    """;

                await command.ExecuteNonQueryAsync();
            }

            var artifactId =
                new ArtifactId("artifact-rollback-001");

            var artifact =
                new Artifact
                {
                    Id = artifactId,
                    Name = "rollback.db",
                    ArtifactType = "file"
                };

            var provenance =
                new Provenance
                {
                    ArtifactId = artifactId,
                    Source = "/data/rollback.db",
                    RecordedBy = "EMF.Tests"
                };

            await Assert.ThrowsAsync<Microsoft.Data.Sqlite.SqliteException>(
                () => repository.AddArtifactWithProvenanceAndRelationshipsAsync(
                    artifact,
                    provenance,
                    [
                        new Relationship
                        {
                            SourceArtifactId = artifactId,
                            TargetArtifactId =
                                new ArtifactId("source-rollback-001"),
                            RelationshipType =
                                RelationshipTypes.GeneratedFrom
                        }
                    ]));

            var stored =
                await repository.GetArtifactAsync(artifactId);

            Assert.Null(stored);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }


    [Fact]
    public async Task SqliteEvidenceRepository_InitializeAsync_CreatesSchema()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-persistence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            Assert.True(File.Exists(databasePath));

            await using var connection = new SqliteConnection(
                $"Data Source={databasePath}");

            await connection.OpenAsync();

            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT name
                FROM sqlite_master
                WHERE type = 'table'
                  AND name IN ('Artifacts', 'Relationships', 'Provenance');
                """;

            var tables = new List<string>();

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            Assert.Contains("Artifacts", tables);
            Assert.Contains("Relationships", tables);
            Assert.Contains("Provenance", tables);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task SqliteEvidenceRepository_ArtifactRoundTrip()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-persistence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            var createdUtc = DateTimeOffset.UtcNow;

            var artifact = new Artifact
            {
                Id = new ArtifactId("artifact-001"),
                Name = "oscar.db",
                ArtifactType = "file",
                CreatedUtc = createdUtc,
                Fingerprint = new ContentFingerprint
                {
                    Algorithm = "SHA-256",
                    Value = "ABC123"
                },
                Metadata = new Dictionary<string, object>
                {
                    ["extension"] = ".db"
                }
            };

            await repository.AddArtifactAsync(artifact);

            var result = await repository.GetArtifactAsync(artifact.Id);

            Assert.NotNull(result);
            Assert.Equal(artifact.Id, result!.Id);
            Assert.Equal("oscar.db", result.Name);
            Assert.Equal("file", result.ArtifactType);
            Assert.Equal(createdUtc, result.CreatedUtc);
            Assert.Equal(artifact.Fingerprint, result.Fingerprint);

            var extension =
                Assert.IsType<JsonElement>(result.Metadata["extension"]);

            Assert.Equal(".db", extension.GetString());
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }


    [Fact]
    public async Task SqliteEvidenceRepository_RelationshipRoundTrip()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-persistence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            var relationship = new Relationship
            {
                SourceArtifactId = new ArtifactId("artifact-a"),
                TargetArtifactId = new ArtifactId("artifact-b"),
                RelationshipType = RelationshipTypes.Contains,
                Properties = new Dictionary<string, object>
                {
                    ["role"] = "child"
                }
            };

            await repository.AddRelationshipAsync(relationship);

            var results = await repository.GetRelationshipsAsync(
                relationship.SourceArtifactId);

            var result = Assert.Single(results);

            Assert.Equal(
                relationship.SourceArtifactId,
                result.SourceArtifactId);

            Assert.Equal(
                relationship.TargetArtifactId,
                result.TargetArtifactId);

            Assert.Equal(
                RelationshipTypes.Contains,
                result.RelationshipType);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }


    [Fact]
    public async Task SqliteEvidenceRepository_ProvenanceRoundTrip()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-persistence-{Guid.NewGuid():N}.db");

        try
        {
            var repository = new SqliteEvidenceRepository(databasePath);

            await repository.InitializeAsync();

            var provenance = new Provenance
            {
                ArtifactId = new ArtifactId("artifact-001"),
                Source = "/data/oscar.db",
                RecordedBy = "EMF.Discovery",
                Properties = new Dictionary<string, object>
                {
                    ["sourceType"] = "file"
                }
            };

            await repository.AddProvenanceAsync(provenance);

            var results = await repository.GetProvenanceAsync(
                provenance.ArtifactId);

            var result = Assert.Single(results);

            Assert.Equal(provenance.ArtifactId, result.ArtifactId);
            Assert.Equal("/data/oscar.db", result.Source);
            Assert.Equal("EMF.Discovery", result.RecordedBy);
        }
        finally
        {
            if (File.Exists(databasePath))
            {
                File.Delete(databasePath);
            }
        }
    }

    [Fact]
    public async Task SqliteWorkflowRepository_UpdateOperationAsync_ThrowsWhenOperationDoesNotExist()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-workflow-operation-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(databasePath);

            await repository.InitializeAsync();

            var operation = new WorkflowOperationRecord
            {
                WorkflowId = new WorkflowId("workflow-operation-missing"),
                ActivityId = "activity-missing",
                OperationId = new OperationId("operation-missing"),
                OperationType = "external-side-effect",
                Status = "Completed",
                CreatedUtc = DateTimeOffset.UtcNow,
                CompletedUtc = DateTimeOffset.UtcNow
            };

            await Assert.ThrowsAsync<InvalidOperationException>(
                () => repository.UpdateOperationAsync(operation));
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task SqliteWorkflowRepository_UpdateOperationAsync_PersistsCompletion()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-workflow-operation-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(databasePath);

            await repository.InitializeAsync();

            var createdUtc = DateTimeOffset.UtcNow;
            var completedUtc = createdUtc.AddMinutes(2);

            var operation = new WorkflowOperationRecord
            {
                WorkflowId = new WorkflowId("workflow-operation-002"),
                ActivityId = "activity-002",
                OperationId = new OperationId("operation-002"),
                OperationType = "external-side-effect",
                Status = "Pending",
                CreatedUtc = createdUtc
            };

            Assert.True(
                await repository.TryCreateOperationAsync(operation));

            var completed = new WorkflowOperationRecord
            {
                WorkflowId = operation.WorkflowId,
                ActivityId = operation.ActivityId,
                OperationId = operation.OperationId,
                OperationType = operation.OperationType,
                Status = "Completed",
                CreatedUtc = operation.CreatedUtc,
                CompletedUtc = completedUtc
            };

            await repository.UpdateOperationAsync(completed);

            var result =
                await repository.GetOperationAsync(
                    completed.WorkflowId,
                    completed.ActivityId,
                    completed.OperationId);

            Assert.NotNull(result);
            Assert.Equal("Completed", result!.Status);
            Assert.Equal(completedUtc, result.CompletedUtc);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

    [Fact]
    public async Task SqliteWorkflowRepository_OperationPersistence_RoundTrips()
    {
        var databasePath = Path.Combine(
            Path.GetTempPath(),
            $"emf-workflow-operation-{Guid.NewGuid():N}.db");

        try
        {
            var repository =
                new SqliteWorkflowRepository(databasePath);

            await repository.InitializeAsync();

            var operation = new WorkflowOperationRecord
            {
                WorkflowId = new WorkflowId("workflow-operation-001"),
                ActivityId = "activity-001",
                OperationId = new OperationId("operation-001"),
                OperationType = "external-side-effect",
                Status = "Pending",
                CreatedUtc = DateTimeOffset.UtcNow
            };

            Assert.True(
                await repository.TryCreateOperationAsync(operation));

            Assert.False(
                await repository.TryCreateOperationAsync(operation));

            var result =
                await repository.GetOperationAsync(
                    operation.WorkflowId,
                    operation.ActivityId,
                    operation.OperationId);

            Assert.NotNull(result);
            Assert.Equal(operation.WorkflowId, result!.WorkflowId);
            Assert.Equal(operation.ActivityId, result.ActivityId);
            Assert.Equal(operation.OperationId, result.OperationId);
            Assert.Equal(operation.OperationType, result.OperationType);
            Assert.Equal(operation.Status, result.Status);
        }
        finally
        {
            if (File.Exists(databasePath))
                File.Delete(databasePath);
        }
    }

}

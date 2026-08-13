using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Core.Models.Integrity;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
using EMF.Integrity;

namespace EMF.Tests;

public sealed class InventoryWorkflowActivityTests
{

    [Fact]
    public async Task ExecuteAsync_SkipsDuplicateSourceAndFingerprint()
    {
        var existingId = new ArtifactId("artifact-existing");
        var newId = new ArtifactId("artifact-new");

        var existingContent =
            System.Text.Encoding.UTF8.GetBytes(
                "existing artifact content");

        var fingerprintService =
            new Sha256ContentFingerprintService();

        var fingerprint =
            await fingerprintService.ComputeAsync(
                existingContent);

        var service = new FakeInventoryOrchestrationService
        {
            Results =
            {
                new InventoryOrchestrationResult
                {
                    DiscoveredItem = null!,
                    Artifact = new EMF.Core.Models.Artifact
                    {
                        Id = newId,
                        Name = "evidence.db",
                        ArtifactType = "file",
                        Fingerprint = fingerprint
                    },
                    Provenance = new EMF.Core.Models.Provenance
                    {
                        ArtifactId = newId,
                        Source = "/data/evidence.db",
                        RecordedBy = "EMF.Tests"
                    },
                    Success = true,
                    Inventory = null
                }
            }
        };

        var persistence = new FakeEvidencePersistenceService
        {
            ExistingArtifact = new EMF.Core.Models.Artifact
            {
                Id = existingId,
                Name = "evidence.db",
                ArtifactType = "file",
                Fingerprint = fingerprint
            }
        };

        var contentStore = new FakeArtifactContentStore();

        contentStore.Stored[existingId] = existingContent;

        contentStore.Stored[newId] =
            System.Text.Encoding.UTF8.GetBytes(
                "new duplicate content");

        var activity =
            new InventoryWorkflowActivity(
                service,
                persistence,
                new Sha256ContentFingerprintService(),
                contentStore,
                "/tmp/source",
                new DiscoveryOptions());

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-inventory")
            };

        var result = await activity.ExecuteAsync(context);

        Assert.True(result.Succeeded);
        Assert.Empty(persistence.Persisted);
        Assert.Single(contentStore.Deleted);
        Assert.Equal(newId, contentStore.Deleted[0]);
    }




    [Fact]
    public async Task ExecuteAsync_FailsWhenDuplicateContentCannotBeRecovered()
    {
        var existingId = new ArtifactId("artifact-existing");
        var newId = new ArtifactId("artifact-new");

        var fingerprint = new ContentFingerprint
        {
            Algorithm = "SHA-256",
            Value = "ABC123"
        };

        var service = new FakeInventoryOrchestrationService
        {
            Results =
            {
                new InventoryOrchestrationResult
                {
                    DiscoveredItem = null!,
                    Artifact = new EMF.Core.Models.Artifact
                    {
                        Id = newId,
                        Name = "evidence.db",
                        ArtifactType = "file",
                        Fingerprint = fingerprint
                    },
                    Provenance = new EMF.Core.Models.Provenance
                    {
                        ArtifactId = newId,
                        Source = "/data/evidence.db",
                        RecordedBy = "EMF.Tests"
                    },
                    Success = true,
                    Inventory = null
                }
            }
        };

        var persistence = new FakeEvidencePersistenceService
        {
            ExistingArtifact = new EMF.Core.Models.Artifact
            {
                Id = existingId,
                Name = "evidence.db",
                ArtifactType = "file",
                Fingerprint = fingerprint
            }
        };

        var contentStore = new FakeArtifactContentStore();

        var activity =
            new InventoryWorkflowActivity(
                service,
                persistence,
                new Sha256ContentFingerprintService(),
                contentStore,
                "/tmp/source",
                new DiscoveryOptions());

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-inventory")
            };

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => activity.ExecuteAsync(context));

        Assert.Equal(
            "Duplicate artifact content is missing.",
            exception.Message);

        Assert.Empty(persistence.Persisted);
        Assert.Empty(contentStore.Deleted);
        Assert.Empty(contentStore.Written);
    }

    [Fact]
    public async Task ExecuteAsync_DoesNotReuseExistingContentWhenFingerprintDoesNotMatch()
    {
        var existingId = new ArtifactId("artifact-existing");
        var newId = new ArtifactId("artifact-new");

        var fingerprint = new ContentFingerprint
        {
            Algorithm = "SHA-256",
            Value = "ABC123"
        };

        var service = new FakeInventoryOrchestrationService
        {
            Results =
            {
                new InventoryOrchestrationResult
                {
                    DiscoveredItem = null!,
                    Artifact = new EMF.Core.Models.Artifact
                    {
                        Id = newId,
                        Name = "evidence.db",
                        ArtifactType = "file",
                        Fingerprint = fingerprint
                    },
                    Provenance = new EMF.Core.Models.Provenance
                    {
                        ArtifactId = newId,
                        Source = "/data/evidence.db",
                        RecordedBy = "EMF.Tests"
                    },
                    Success = true,
                    Inventory = null
                }
            }
        };

        var persistence = new FakeEvidencePersistenceService
        {
            ExistingArtifact = new EMF.Core.Models.Artifact
            {
                Id = existingId,
                Name = "evidence.db",
                ArtifactType = "file",
                Fingerprint = fingerprint
            }
        };

        var contentStore = new FakeArtifactContentStore();

        contentStore.Stored[existingId] =
            System.Text.Encoding.UTF8.GetBytes(
                "tampered existing content");

        contentStore.Stored[newId] =
            System.Text.Encoding.UTF8.GetBytes(
                "new duplicate content");

        var activity =
            new InventoryWorkflowActivity(
                service,
                persistence,
                new Sha256ContentFingerprintService(),
                contentStore,
                "/tmp/source",
                new DiscoveryOptions());

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-integrity")
            };

        var exception =
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => activity.ExecuteAsync(context));

        Assert.Equal(
            "Existing artifact content failed fingerprint validation.",
            exception.Message);

        Assert.Empty(persistence.Persisted);
    }

    [Fact]
    public async Task ExecuteAsync_AggregatesPersistenceAndCleanupFailures()
    {
        var artifactId = new ArtifactId("artifact-cleanup-failure");

        var service = new FakeInventoryOrchestrationService
        {
            Results =
            {
                new InventoryOrchestrationResult
                {
                    DiscoveredItem = null!,
                    Artifact = new EMF.Core.Models.Artifact
                    {
                        Id = artifactId,
                        Name = "evidence.db",
                        ArtifactType = "file"
                    },
                    Provenance = null!,
                    Success = true,
                    Inventory = null
                }
            }
        };

        var activity =
            new InventoryWorkflowActivity(
                service,
                new FailingEvidencePersistenceService(),
                new Sha256ContentFingerprintService(),
                new FailingArtifactContentStore(),
                "/tmp/source",
                new DiscoveryOptions());

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-inventory")
            };

        var exception =
            await Assert.ThrowsAsync<AggregateException>(
                () => activity.ExecuteAsync(context));

        Assert.Equal(2, exception.InnerExceptions.Count);
        Assert.Contains(
            exception.InnerExceptions,
            ex => ex.Message == "forced persistence failure");
        Assert.Contains(
            exception.InnerExceptions,
            ex => ex.Message == "forced cleanup failure");
    }



    [Fact]
    public async Task ExecuteAsync_DeletesContentWhenPersistenceFails()
    {
        var artifactId = new ArtifactId("artifact-cleanup-001");

        var service = new FakeInventoryOrchestrationService
        {
            Results =
            {
                new InventoryOrchestrationResult
                {
                    DiscoveredItem = null!,
                    Artifact = new EMF.Core.Models.Artifact
                    {
                        Id = artifactId,
                        Name = "evidence.db",
                        ArtifactType = "file"
                    },
                    Provenance = null!,
                    Success = true,
                    Inventory = null
                }
            }
        };

        var contentStore = new FakeArtifactContentStore();

        var activity =
            new InventoryWorkflowActivity(
                service,
                new FailingEvidencePersistenceService(),
                new Sha256ContentFingerprintService(),
                contentStore,
                "/tmp/source",
                new DiscoveryOptions());

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-inventory")
            };

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => activity.ExecuteAsync(context));

        Assert.Single(contentStore.Deleted);
        Assert.Equal(artifactId, contentStore.Deleted[0]);
    }


    [Fact]
    public async Task ExecuteAsync_succeeds_when_all_inventory_results_succeed()
    {
        var service = new FakeInventoryOrchestrationService
        {
            Results =
            {
                CreateResult(true),
                CreateResult(true)
            }
        };

        var persistence = new FakeEvidencePersistenceService();

        var activity =
            new InventoryWorkflowActivity(
                service,
                persistence,
                new Sha256ContentFingerprintService(),
                null,
                "/tmp/source",
                new DiscoveryOptions());

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-inventory"),
            };

        var result =
            await activity.ExecuteAsync(context);

        Assert.True(result.Succeeded);
        Assert.Equal(2, persistence.Persisted.Count);
    }


    [Fact]
    public async Task ExecuteAsync_RestoresMissingContentForExistingArtifact()
    {
        var existingId = new ArtifactId("artifact-existing");
        var newId = new ArtifactId("artifact-new");

        var content =
            System.Text.Encoding.UTF8.GetBytes(
                "restored artifact content");

        var fingerprintService =
            new Sha256ContentFingerprintService();

        var fingerprint =
            await fingerprintService.ComputeAsync(
                content);

        var sourcePath = "/data/evidence.txt";


        {
            var service = new FakeInventoryOrchestrationService
            {
                Results =
                {
                    new InventoryOrchestrationResult
                    {
                        DiscoveredItem = null!,
                        Artifact = new EMF.Core.Models.Artifact
                        {
                            Id = newId,
                            Name = "evidence.txt",
                            ArtifactType = "file",
                            Fingerprint = fingerprint
                        },
                        Provenance = new EMF.Core.Models.Provenance
                        {
                            ArtifactId = newId,
                            Source = sourcePath,
                            RecordedBy = "EMF.Tests"
                        },
                        Success = true,
                        Inventory = null
                    }
                }
            };

            var persistence = new FakeEvidencePersistenceService
            {
                ExistingArtifact = new EMF.Core.Models.Artifact
                {
                    Id = existingId,
                    Name = "evidence.txt",
                    ArtifactType = "file",
                    Fingerprint = fingerprint
                }
            };

            var contentStore = new FakeArtifactContentStore();
            contentStore.Stored[newId] = content;

            var activity =
                new InventoryWorkflowActivity(
                    service,
                    persistence,
                    new Sha256ContentFingerprintService(),
                    contentStore,
                    "/tmp/source",
                    new DiscoveryOptions());

            var context =
                new WorkflowExecutionContext
                {
                    WorkflowId = new WorkflowId("workflow-inventory")
                };

            var result = await activity.ExecuteAsync(context);

            Assert.True(result.Succeeded);
            Assert.Empty(persistence.Persisted);
            Assert.Single(contentStore.Deleted);
            Assert.Equal(newId, contentStore.Deleted[0]);
            Assert.Single(contentStore.Written);

            Assert.Equal(existingId, contentStore.Written[0].ArtifactId);
            Assert.Equal(
                content,
                contentStore.Written[0].Content);
        }
    }

    [Fact]
    public async Task ExecuteAsync_fails_when_any_inventory_result_fails()
    {
        var service = new FakeInventoryOrchestrationService
        {
            Results =
            {
                CreateResult(true),
                CreateResult(false)
            }
        };

        var persistence = new FakeEvidencePersistenceService();

        var activity =
            new InventoryWorkflowActivity(
                service,
                persistence,
                new Sha256ContentFingerprintService(),
                null,
                "/tmp/source",
                new DiscoveryOptions());

        var context =
            new WorkflowExecutionContext
            {
                WorkflowId = new WorkflowId("workflow-inventory"),
            };

        var result =
            await activity.ExecuteAsync(context);

        Assert.False(result.Succeeded);
        Assert.Single(persistence.Persisted);
    }

    private static InventoryOrchestrationResult CreateResult(
        bool success)
    {
        var artifactId =
            new ArtifactId(Guid.NewGuid().ToString("N"));

        return new InventoryOrchestrationResult
        {
            DiscoveredItem = null!,
            Artifact = new EMF.Core.Models.Artifact
            {
                Id = artifactId,
                Name = "test.db",
                ArtifactType = "file"
            },
            Provenance = new EMF.Core.Models.Provenance
            {
                ArtifactId = artifactId,
                Source = "/tmp/test.db",
                RecordedBy = "EMF.Tests"
            },
            Success = success,
            Inventory = null
        };
    }



    private sealed class FailingEvidencePersistenceService :
        IEvidencePersistenceService
    {

        public Task<EMF.Core.Models.Artifact?> FindArtifactAsync(
            string source,
            EMF.Core.Models.Integrity.ContentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<EMF.Core.Models.Artifact?>(null);
        }

        public Task PersistAsync(
            InventoryOrchestrationResult result,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("forced persistence failure");
        }
    }

    private sealed class FakeArtifactContentStore :
        IArtifactContentStore
    {
        public List<ArtifactId> Deleted { get; } = [];

        public List<(ArtifactId ArtifactId, byte[] Content)> Written { get; } = [];

        public Dictionary<ArtifactId, byte[]> Stored { get; } = [];

        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default)
        {
            var bytes = content.ToArray();

            Written.Add((artifactId, bytes));
            Stored[artifactId] = bytes;

            return Task.CompletedTask;
        }

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            Stored.TryGetValue(artifactId, out var content);
            return Task.FromResult<byte[]?>(content);
        }

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            Deleted.Add(artifactId);
            return Task.CompletedTask;
        }
    }


    private sealed class FailingArtifactContentStore :
        IArtifactContentStore
    {
        public Task WriteAsync(
            ArtifactId artifactId,
            ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<byte[]?> ReadAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<byte[]?>(null);

        public Task DeleteAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException(
                "forced cleanup failure");
        }
    }

    private sealed class FakeEvidencePersistenceService :
        IEvidencePersistenceService
    {
        public EMF.Core.Models.Artifact? ExistingArtifact { get; init; }

        public List<InventoryOrchestrationResult> Persisted { get; } = [];


        public Task<EMF.Core.Models.Artifact?> FindArtifactAsync(
            string source,
            EMF.Core.Models.Integrity.ContentFingerprint fingerprint,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(ExistingArtifact);
        }

        public Task PersistAsync(
            InventoryOrchestrationResult result,
            CancellationToken cancellationToken = default)
        {
            Persisted.Add(result);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeInventoryOrchestrationService :
        IInventoryOrchestrationService
    {
        public List<InventoryOrchestrationResult> Results { get; }
            = new();

        public async IAsyncEnumerable<InventoryOrchestrationResult> ExecuteAsync(
            string sourcePath,
            DiscoveryOptions options,
            [System.Runtime.CompilerServices.EnumeratorCancellation]
            CancellationToken cancellationToken = default)
        {
            foreach (var result in Results)
            {
                yield return result;
                await Task.Yield();
            }
        }
    }
}

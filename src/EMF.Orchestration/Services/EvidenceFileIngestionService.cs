using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Contracts.Ingestion;
using System.Security.Cryptography;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class EvidenceFileIngestionService :
    IEvidenceFileIngestionService
{
    public const long DefaultMaxFileBytes =
        100L * 1024 * 1024;

    private readonly IArtifactIngestionCoordinator _coordinator;
    private readonly IContentFingerprintService _fingerprintService;
    private readonly IArtifactIdGenerator _artifactIdGenerator;
    private readonly IArtifactFactory _artifactFactory;
    private readonly long _maxFileBytes;

    public EvidenceFileIngestionService(
        IArtifactIngestionCoordinator coordinator,
        IContentFingerprintService fingerprintService,
        IArtifactIdGenerator artifactIdGenerator,
        IArtifactFactory artifactFactory,
        long maxFileBytes = DefaultMaxFileBytes)
    {
        ArgumentNullException.ThrowIfNull(coordinator);
        ArgumentNullException.ThrowIfNull(fingerprintService);
        ArgumentNullException.ThrowIfNull(artifactIdGenerator);
        ArgumentNullException.ThrowIfNull(artifactFactory);
        if (maxFileBytes <= 0 || maxFileBytes > Array.MaxLength)
            throw new ArgumentOutOfRangeException(nameof(maxFileBytes));
        _coordinator = coordinator;
        _fingerprintService = fingerprintService;
        _artifactIdGenerator = artifactIdGenerator;
        _artifactFactory = artifactFactory;
        _maxFileBytes = maxFileBytes;
    }

    // Source-compatible admission failure for hosts that have not composed ADR-048.
    // No legacy unconditional ingestion path remains available.
    [Obsolete("Compose the authenticated ADR-048 ingestion coordinator.")]
    public EvidenceFileIngestionService(IEvidenceRepository repository, IArtifactContentStore contentStore,
        IContentFingerprintService fingerprintService, IArtifactIdGenerator artifactIdGenerator,
        IArtifactFactory artifactFactory, long maxFileBytes = DefaultMaxFileBytes)
        : this(new UnsupportedIngestionCoordinator(), fingerprintService, artifactIdGenerator, artifactFactory, maxFileBytes)
        => throw new NotSupportedException("Evidence ingestion requires authenticated durable lifecycle coordination.");

    private sealed class UnsupportedIngestionCoordinator : IArtifactIngestionCoordinator
    {
        public Task<ArtifactIngestionOutcome> IngestAsync(IngestionMetadataDraft draft, ReadOnlyMemory<byte> content,
            CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ArtifactIngestionOutcome?> RecoverInterruptedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    public async Task<EvidenceFileIngestionResult> IngestAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);

        var fullPath = Path.GetFullPath(sourcePath);

        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException(
                "Evidence file was not found.",
                fullPath);
        }

        var file = new FileInfo(fullPath);

        if (file.Length > _maxFileBytes)
            throw new InvalidDataException(
                "Evidence file exceeds the maximum allowed size.");

        await using var stream = File.OpenRead(fullPath);

        var length = stream.Length;

        if (length > _maxFileBytes)
            throw new InvalidDataException(
                "Evidence file exceeds the maximum allowed size.");

        var content = new byte[(int)length];

        try
        {
            await stream.ReadExactlyAsync(
                content,
                cancellationToken);

            if (stream.Position != stream.Length)
                throw new IOException(
                    "Evidence file changed during ingestion.");

            var fingerprint =
                await _fingerprintService.ComputeAsync(
                    content,
                    cancellationToken);

            var artifactId = _artifactIdGenerator.Generate();

            var item =
                new EMF.Discovery.Models.DiscoveredItem
                {
                    Name = file.Name,
                    SourcePath = fullPath,
                    SourceType = "file",
                    SizeBytes = content.LongLength,
                    CreatedUtc = file.CreationTimeUtc,
                    ModifiedUtc = file.LastWriteTimeUtc
                };

            var creation =
                _artifactFactory.Create(
                    item,
                    artifactId,
                    fingerprint);


            if (creation is null || creation.Artifact is null ||
                creation.Artifact.Id != artifactId)
                throw new InvalidOperationException("Evidence file factory returned an invalid artifact identity.");

            if (creation.Artifact.Fingerprint is null ||
                creation.Artifact.Fingerprint != fingerprint)
                throw new InvalidOperationException("Evidence file factory returned an invalid content fingerprint.");

            if (creation.Provenance is null ||
                creation.Provenance.ArtifactId != artifactId)
                throw new InvalidOperationException("Evidence file factory returned an invalid provenance identity.");

            if (!string.Equals(creation.Provenance.Source, fullPath, StringComparison.Ordinal))
                throw new InvalidOperationException("Evidence file factory returned an invalid provenance source.");

            try
            {
                var outcome = await _coordinator.IngestAsync(new(creation.Artifact, creation.Provenance), content, cancellationToken);
                if (outcome.Result is null || outcome.Disposition is not
                    (ArtifactIngestionDisposition.ProvisionalArtifactAdopted or ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact))
                    throw new ArtifactIngestionReviewException(outcome.OperationId);
                return Result(outcome);
            }
            catch (OperationCanceledException)
            {
                CryptographicOperations.ZeroMemory(content);
                using var serviceBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var recovered = await _coordinator.RecoverInterruptedAsync(serviceBudget.Token);
                    if (recovered is { IsAdopted: true, Result: not null }) return Result(recovered);
                }
                catch { /* Durable intent/receipt work survives failed bounded immediate compensation. */ }
                throw new OperationCanceledException("Evidence ingestion was cancelled; any unresolved lifecycle remains durable recovery work.", cancellationToken);
            }
            catch (Exception error) when (error is not (UnauthorizedAccessException or ArtifactContentIdempotencyException or ArtifactIngestionReviewException))
            {
                CryptographicOperations.ZeroMemory(content);
                using var serviceBudget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
                try
                {
                    var recovered = await _coordinator.RecoverInterruptedAsync(serviceBudget.Token);
                    if (recovered is { IsAdopted: true, Result: not null }) return Result(recovered);
                }
                catch { /* Recovery failure remains durable work; provider details are not exposed. */ }
                throw new InvalidOperationException("Evidence ingestion could not complete; unresolved lifecycle work requires recovery or review.");
            }
        }
        finally { CryptographicOperations.ZeroMemory(content); }
    }
    private static EvidenceFileIngestionResult Result(ArtifactIngestionOutcome outcome) => new()
    {
        Artifact = outcome.Result!.Artifact,
        Provenance = outcome.Result.Provenance,
        AlreadyExisted = outcome.Disposition == ArtifactIngestionDisposition.DeduplicatedToCanonicalArtifact,
        OperationId = outcome.OperationId,
        LifecycleState = outcome.State,
        IsAdopted = outcome.IsAdopted,
        AuditDelivery = outcome.AuditDelivery
    };
}

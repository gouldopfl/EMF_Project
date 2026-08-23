using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using EMF.Discovery.Models;
using EMF.Orchestration.Contracts;
using EMF.Orchestration.Models;

namespace EMF.Orchestration.Services;

public sealed class EmailAttachmentExtractionService :
    IEmailAttachmentExtractionService
{
    private readonly IEvidenceRepository _repository;
    private readonly IArtifactContentStore _contentStore;
    private readonly IContentFingerprintService _fingerprintService;
    private readonly IArtifactIdGenerator _artifactIdGenerator;
    private readonly IArtifactFactory _artifactFactory;

    public EmailAttachmentExtractionService(
        IEvidenceRepository repository,
        IArtifactContentStore contentStore,
        IContentFingerprintService fingerprintService,
        IArtifactIdGenerator artifactIdGenerator,
        IArtifactFactory artifactFactory)
    {
        ArgumentNullException.ThrowIfNull(repository);
        ArgumentNullException.ThrowIfNull(contentStore);
        ArgumentNullException.ThrowIfNull(fingerprintService);
        ArgumentNullException.ThrowIfNull(artifactIdGenerator);
        ArgumentNullException.ThrowIfNull(artifactFactory);

        _repository = repository;
        _contentStore = contentStore;
        _fingerprintService = fingerprintService;
        _artifactIdGenerator = artifactIdGenerator;
        _artifactFactory = artifactFactory;
    }

    public async Task<EmailAttachmentExtractionResult> ExtractAsync(
        ArtifactId emailArtifactId,
        string fileName,
        string? contentType,
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fileName);

        var artifactId = _artifactIdGenerator.Generate();

        var fingerprint =
            await _fingerprintService.ComputeAsync(
                content,
                cancellationToken);

        var metadata =
            new Dictionary<string, object>();

        if (!string.IsNullOrWhiteSpace(contentType))
            metadata["contentType"] = contentType;

        var item =
            new DiscoveredItem
            {
                Name = fileName,
                SourcePath =
                    $"{emailArtifactId.Value}/{fileName}",
                SourceType = "email-attachment",
                SizeBytes = content.Length,
                Metadata = metadata
            };

        var creation =
            _artifactFactory.Create(
                item,
                artifactId,
                fingerprint);

        var relationships =
            new[]
            {
                new Relationship
                {
                    SourceArtifactId = emailArtifactId,
                    TargetArtifactId = artifactId,
                    RelationshipType = RelationshipTypes.Contains
                },
                new Relationship
                {
                    SourceArtifactId = artifactId,
                    TargetArtifactId = emailArtifactId,
                    RelationshipType = RelationshipTypes.DerivedFrom
                }
            };

        await _contentStore.WriteAsync(
            artifactId,
            content,
            cancellationToken);

        try
        {
            await _repository
                .AddArtifactWithProvenanceAndRelationshipsAsync(
                    creation.Artifact,
                    creation.Provenance,
                    relationships,
                    cancellationToken);
        }
        catch (Exception persistenceException)
            when (persistenceException is not OperationCanceledException)
        {
            try
            {
                await _contentStore.DeleteAsync(
                    artifactId,
                    cancellationToken);
            }
            catch (Exception cleanupException)
                when (cleanupException is not OperationCanceledException)
            {
                throw new AggregateException(
                    "Attachment persistence failed and content cleanup also failed.",
                    persistenceException,
                    cleanupException);
            }

            throw;
        }

        return new EmailAttachmentExtractionResult
        {
            Artifact = creation.Artifact,
            Provenance = creation.Provenance,
            Relationships = relationships
        };
    }
}

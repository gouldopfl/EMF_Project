using System.Text.Json;
using EMF.Core.Models.Identities;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteMedicalLiteratureRepository :
    IMedicalLiteratureRepository
{
    private readonly string _databasePath;

    public SqliteMedicalLiteratureRepository(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection() =>
        VeteransClaimsSqliteConnectionFactory.Create(_databasePath);

    public Task InitializeAsync(
        CancellationToken cancellationToken = default) =>
        new VeteransClaimsSqliteSchema(_databasePath)
            .InitializeAsync(cancellationToken);

    public async Task AddMedicalLiteratureSourceAsync(
        MedicalLiteratureSource source,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO VeteransClaims_MedicalLiteratureSources
            (Id, Title, Authors, Publication, PublicationYear,
             VaAffiliated, VaFunded, PeerReviewed,
             FundingSource, ResearchOrganization,
             Doi, Pmid, SourceUri, SourceHash, RetrievedUtc)
            VALUES
            ($id, $title, $authors, $publication, $year,
             $vaAffiliated, $vaFunded, $peerReviewed,
             $fundingSource, $researchOrganization,
             $doi, $pmid, $uri, $hash, $retrieved);
            """;

        command.Parameters.AddWithValue("$id", source.Id.Value);
        command.Parameters.AddWithValue("$title", source.Title);
        command.Parameters.AddWithValue("$authors", source.Authors);
        command.Parameters.AddWithValue(
            "$publication",
            source.Publication);
        command.Parameters.AddWithValue(
            "$year",
            (object?)source.PublicationYear ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$vaAffiliated",
            source.VaAffiliated ? 1 : 0);
        command.Parameters.AddWithValue(
            "$vaFunded",
            source.VaFunded ? 1 : 0);
        command.Parameters.AddWithValue(
            "$peerReviewed",
            source.PeerReviewed ? 1 : 0);
        command.Parameters.AddWithValue(
            "$fundingSource",
            (object?)source.FundingSource ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$researchOrganization",
            (object?)source.ResearchOrganization ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$doi",
            (object?)source.Doi ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$pmid",
            (object?)source.Pmid ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$uri",
            (object?)source.SourceUri ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$hash",
            (object?)source.SourceHash ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$retrieved",
            source.RetrievedUtc?.ToString("O")
                ?? (object)DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<MedicalLiteratureSource?>
        GetMedicalLiteratureSourceAsync(
            MedicalLiteratureSourceId sourceId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT Id, Title, Authors, Publication, PublicationYear,
                   VaAffiliated, VaFunded, PeerReviewed,
                   FundingSource, ResearchOrganization,
                   Doi, Pmid, SourceUri, SourceHash, RetrievedUtc
            FROM VeteransClaims_MedicalLiteratureSources
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue("$id", sourceId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken)
            ? ReadSource(reader)
            : null;
    }

    public async Task AddRequirementMedicalLiteratureAsync(
        RequirementMedicalLiterature literature,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(literature);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO VeteransClaims_RequirementMedicalLiterature
            (RequirementId, MedicalLiteratureSourceId,
             GuidanceRole, Description)
            VALUES ($requirement, $source, $role, $description);
            """;

        command.Parameters.AddWithValue(
            "$requirement",
            literature.RequirementId.Value);
        command.Parameters.AddWithValue(
            "$source",
            literature.MedicalLiteratureSourceId.Value);
        command.Parameters.AddWithValue(
            "$role",
            literature.GuidanceRole);
        command.Parameters.AddWithValue(
            "$description",
            literature.Description);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<RequirementMedicalLiterature>>
        GetRequirementMedicalLiteratureAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT RequirementId, MedicalLiteratureSourceId,
                   GuidanceRole, Description
            FROM VeteransClaims_RequirementMedicalLiterature
            WHERE RequirementId = $requirement
            ORDER BY MedicalLiteratureSourceId, GuidanceRole;
            """;

        command.Parameters.AddWithValue(
            "$requirement",
            requirementId.Value);

        var results =
            new List<RequirementMedicalLiterature>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new RequirementMedicalLiterature
                {
                    RequirementId =
                        new RequirementId(reader.GetString(0)),
                    MedicalLiteratureSourceId =
                        new MedicalLiteratureSourceId(
                            reader.GetString(1)),
                    GuidanceRole = reader.GetString(2),
                    Description = reader.GetString(3)
                });
        }

        return results;
    }


    public async Task AddMedicalLiteratureSourceArtifactAsync(
        MedicalLiteratureSourceArtifact association,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(association);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO VeteransClaims_MedicalLiteratureSourceArtifacts
                (MedicalLiteratureSourceId, ArtifactId)
            SELECT source.Id, artifact.Id
            FROM VeteransClaims_MedicalLiteratureSources AS source
            INNER JOIN Artifacts AS artifact
                ON artifact.Id = $artifactId
            WHERE source.Id = $sourceId;
            """;

        command.Parameters.AddWithValue(
            "$sourceId",
            association.MedicalLiteratureSourceId.Value);
        command.Parameters.AddWithValue(
            "$artifactId",
            association.ArtifactId.Value);

        if (await command.ExecuteNonQueryAsync(cancellationToken) != 1)
            throw new InvalidOperationException(
                "The medical literature source and artifact must exist.");
    }

    public async Task<IReadOnlyList<ArtifactId>> GetArtifactIdsAsync(
        MedicalLiteratureSourceId sourceId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT ArtifactId
            FROM VeteransClaims_MedicalLiteratureSourceArtifacts
            WHERE MedicalLiteratureSourceId = $sourceId
            ORDER BY ArtifactId;
            """;

        command.Parameters.AddWithValue("$sourceId", sourceId.Value);

        var results = new List<ArtifactId>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            results.Add(new ArtifactId(reader.GetString(0)));

        return results;
    }


    public async Task<IReadOnlyList<MedicalLiteratureSourceId>>
        GetMedicalLiteratureSourceIdsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT MedicalLiteratureSourceId
            FROM VeteransClaims_MedicalLiteratureSourceArtifacts
            WHERE ArtifactId = $artifactId
            ORDER BY MedicalLiteratureSourceId;
            """;

        command.Parameters.AddWithValue(
            "$artifactId",
            artifactId.Value);

        var results = new List<MedicalLiteratureSourceId>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
            results.Add(
                new MedicalLiteratureSourceId(reader.GetString(0)));

        return results;
    }

    public Task AddReviewedClassificationAsync(
        ReviewedMedicalLiteratureClassification classification,
        CancellationToken cancellationToken = default) =>
        AddReviewedClassificationsAsync(
            [classification],
            cancellationToken);

    public async Task AddReviewedClassificationsAsync(
        IReadOnlyList<ReviewedMedicalLiteratureClassification>
            classifications,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(classifications);

        if (classifications.Count == 0)
            throw new InvalidOperationException(
                "At least one reviewed medical literature classification is required.");

        foreach (var classification in classifications)
            ValidateReviewedClassification(classification);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);

        foreach (var classification in classifications)
        {
            await AddReviewedClassificationAsync(
                connection,
                transaction,
                classification,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task AddReviewedClassificationAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ReviewedMedicalLiteratureClassification classification,
        CancellationToken cancellationToken)
    {
        var association = classification.Association;

        await using (var lookup = connection.CreateCommand())
        {
            lookup.Transaction = transaction;
            lookup.CommandText = """
                SELECT Description
                FROM VeteransClaims_RequirementMedicalLiterature
                WHERE RequirementId = $requirement
                  AND MedicalLiteratureSourceId = $source
                  AND GuidanceRole = $role;
                """;
            lookup.Parameters.AddWithValue(
                "$requirement",
                association.RequirementId.Value);
            lookup.Parameters.AddWithValue(
                "$source",
                association.MedicalLiteratureSourceId.Value);
            lookup.Parameters.AddWithValue(
                "$role",
                association.GuidanceRole);

            var existing =
                await lookup.ExecuteScalarAsync(cancellationToken);

            if (existing is string existingDescription)
            {
                if (!string.Equals(
                        existingDescription,
                        association.Description,
                        StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        "The accepted medical literature association has a conflicting description.");
                }
            }
            else
            {
                await using var insertAssociation =
                    connection.CreateCommand();
                insertAssociation.Transaction = transaction;
                insertAssociation.CommandText = """
                    INSERT INTO VeteransClaims_RequirementMedicalLiterature
                    (RequirementId, MedicalLiteratureSourceId,
                     GuidanceRole, Description)
                    VALUES ($requirement, $source, $role, $description);
                    """;
                insertAssociation.Parameters.AddWithValue(
                    "$requirement",
                    association.RequirementId.Value);
                insertAssociation.Parameters.AddWithValue(
                    "$source",
                    association.MedicalLiteratureSourceId.Value);
                insertAssociation.Parameters.AddWithValue(
                    "$role",
                    association.GuidanceRole);
                insertAssociation.Parameters.AddWithValue(
                    "$description",
                    association.Description);

                await insertAssociation.ExecuteNonQueryAsync(
                    cancellationToken);
            }
        }

        await using (var sourceArtifact = connection.CreateCommand())
        {
            sourceArtifact.Transaction = transaction;
            sourceArtifact.CommandText = """
                SELECT COUNT(*)
                FROM VeteransClaims_MedicalLiteratureSourceArtifacts
                WHERE MedicalLiteratureSourceId = $source
                  AND ArtifactId = $artifact;
                """;
            sourceArtifact.Parameters.AddWithValue(
                "$source",
                association.MedicalLiteratureSourceId.Value);
            sourceArtifact.Parameters.AddWithValue(
                "$artifact",
                classification.ArtifactId.Value);

            var count = Convert.ToInt32(
                await sourceArtifact.ExecuteScalarAsync(
                    cancellationToken));

            if (count != 1)
            {
                throw new InvalidOperationException(
                    "The classified artifact must be associated with the medical literature source.");
            }
        }

        await using (var existingReview = connection.CreateCommand())
        {
            existingReview.Transaction = transaction;
            existingReview.CommandText = """
                SELECT CorrelationId
                FROM VeteransClaims_ReviewedMedicalLiteratureClassifications
                WHERE RequirementId = $requirement
                  AND MedicalLiteratureSourceId = $source
                  AND GuidanceRole = $role
                  AND ArtifactId = $artifact
                LIMIT 1;
                """;
            existingReview.Parameters.AddWithValue(
                "$requirement",
                association.RequirementId.Value);
            existingReview.Parameters.AddWithValue(
                "$source",
                association.MedicalLiteratureSourceId.Value);
            existingReview.Parameters.AddWithValue(
                "$role",
                association.GuidanceRole);
            existingReview.Parameters.AddWithValue(
                "$artifact",
                classification.ArtifactId.Value);

            var existingCorrelation =
                await existingReview.ExecuteScalarAsync(cancellationToken);

            if (existingCorrelation is string)
            {
                throw new InvalidOperationException(
                    "A reviewed medical literature classification already " +
                    "exists for this requirement, source, role, and artifact. " +
                    "Explicit supersession is required before another reviewed " +
                    "classification can be promoted.");
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO
                    VeteransClaims_ReviewedMedicalLiteratureClassifications
                (RequirementId, MedicalLiteratureSourceId, GuidanceRole,
                 ArtifactId, Description, PromotedBy, PromotedUtc,
                 ReviewedBy, ReviewedUtc, IntelligenceOutput,
                 CapabilityId, ProviderId, CorrelationId, EngineName,
                 EngineVersion, ProviderOperationId, StartedUtc,
                 CompletedUtc, RequiresReview, WarningsJson)
                VALUES
                ($requirement, $source, $role, $artifact, $description,
                 $promotedBy, $promotedUtc, $reviewedBy, $reviewedUtc,
                 $output, $capability, $provider, $correlation, $engine,
                 $engineVersion, $operation, $started, $completed,
                 $requiresReview, $warnings);
                """;

            command.Parameters.AddWithValue(
                "$requirement",
                association.RequirementId.Value);
            command.Parameters.AddWithValue(
                "$source",
                association.MedicalLiteratureSourceId.Value);
            command.Parameters.AddWithValue(
                "$role",
                association.GuidanceRole);
            command.Parameters.AddWithValue(
                "$artifact",
                classification.ArtifactId.Value);
            command.Parameters.AddWithValue(
                "$description",
                association.Description);
            command.Parameters.AddWithValue(
                "$promotedBy",
                classification.PromotedBy);
            command.Parameters.AddWithValue(
                "$promotedUtc",
                classification.PromotedUtc.ToString("O"));
            command.Parameters.AddWithValue(
                "$reviewedBy",
                classification.ReviewedBy);
            command.Parameters.AddWithValue(
                "$reviewedUtc",
                classification.ReviewedUtc.ToString("O"));
            command.Parameters.AddWithValue(
                "$output",
                classification.IntelligenceOutput);
            command.Parameters.AddWithValue(
                "$capability",
                classification.CapabilityId);
            command.Parameters.AddWithValue(
                "$provider",
                classification.ProviderId);
            command.Parameters.AddWithValue(
                "$correlation",
                classification.CorrelationId);
            command.Parameters.AddWithValue(
                "$engine",
                classification.EngineName);
            command.Parameters.AddWithValue(
                "$engineVersion",
                (object?)classification.EngineVersion ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$operation",
                (object?)classification.ProviderOperationId
                    ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$started",
                classification.StartedUtc.ToString("O"));
            command.Parameters.AddWithValue(
                "$completed",
                classification.CompletedUtc.ToString("O"));
            command.Parameters.AddWithValue(
                "$requiresReview",
                classification.RequiresReview ? 1 : 0);
            command.Parameters.AddWithValue(
                "$warnings",
                JsonSerializer.Serialize(classification.Warnings));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        for (var index = 0;
             index < classification.SourceExcerpts.Count;
             index++)
        {
            var excerpt = classification.SourceExcerpts[index];

            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO
                    VeteransClaims_ReviewedMedicalLiteratureExcerpts
                (RequirementId, MedicalLiteratureSourceId, GuidanceRole,
                 ArtifactId, CorrelationId, ExcerptOrdinal, Text,
                 StartOffset, Length)
                VALUES
                ($requirement, $source, $role, $artifact, $correlation,
                 $ordinal, $text, $startOffset, $length);
                """;
            command.Parameters.AddWithValue(
                "$requirement",
                association.RequirementId.Value);
            command.Parameters.AddWithValue(
                "$source",
                association.MedicalLiteratureSourceId.Value);
            command.Parameters.AddWithValue(
                "$role",
                association.GuidanceRole);
            command.Parameters.AddWithValue(
                "$artifact",
                classification.ArtifactId.Value);
            command.Parameters.AddWithValue(
                "$correlation",
                classification.CorrelationId);
            command.Parameters.AddWithValue("$ordinal", index);
            command.Parameters.AddWithValue("$text", excerpt.Text);
            command.Parameters.AddWithValue(
                "$startOffset",
                (object?)excerpt.StartOffset ?? DBNull.Value);
            command.Parameters.AddWithValue(
                "$length",
                (object?)excerpt.Length ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static void ValidateReviewedClassification(
        ReviewedMedicalLiteratureClassification classification)
    {
        ArgumentNullException.ThrowIfNull(classification);
        ArgumentNullException.ThrowIfNull(classification.Association);
        ArgumentNullException.ThrowIfNull(classification.Warnings);
        ArgumentNullException.ThrowIfNull(classification.SourceExcerpts);

        var association = classification.Association;

        if (association.GuidanceRole is not (
            EvidenceGuidanceRoles.SupportsRequirement or
            EvidenceGuidanceRoles.EstablishesElement or
            EvidenceGuidanceRoles.Corroborates or
            EvidenceGuidanceRoles.Clarifies))
        {
            throw new InvalidOperationException(
                "The reviewed medical literature guidance role is unsupported.");
        }

        if (string.IsNullOrWhiteSpace(association.Description))
            throw new InvalidOperationException(
                "The reviewed medical literature description is required.");

        if (string.IsNullOrWhiteSpace(classification.PromotedBy) ||
            string.IsNullOrWhiteSpace(classification.ReviewedBy) ||
            string.IsNullOrWhiteSpace(classification.IntelligenceOutput) ||
            string.IsNullOrWhiteSpace(classification.CapabilityId) ||
            string.IsNullOrWhiteSpace(classification.ProviderId) ||
            string.IsNullOrWhiteSpace(classification.CorrelationId) ||
            string.IsNullOrWhiteSpace(classification.EngineName))
        {
            throw new InvalidOperationException(
                "Reviewed medical literature promotion provenance is incomplete.");
        }

        if (classification.StartedUtc == default ||
            classification.CompletedUtc == default ||
            classification.ReviewedUtc == default ||
            classification.PromotedUtc == default ||
            classification.CompletedUtc < classification.StartedUtc ||
            classification.ReviewedUtc < classification.CompletedUtc ||
            classification.PromotedUtc < classification.ReviewedUtc)
        {
            throw new InvalidOperationException(
                "Reviewed medical literature promotion timestamps are invalid.");
        }

        if (classification.SourceExcerpts.Count == 0)
            throw new InvalidOperationException(
                "Reviewed medical literature requires at least one accepted excerpt.");

        foreach (var excerpt in classification.SourceExcerpts)
        {
            ArgumentNullException.ThrowIfNull(excerpt);

            if (excerpt.ArtifactId != classification.ArtifactId)
                throw new InvalidOperationException(
                    "Every reviewed excerpt must reference the classified artifact.");

            if (string.IsNullOrWhiteSpace(excerpt.Text) ||
                excerpt.StartOffset is null ||
                excerpt.Length is null ||
                excerpt.StartOffset.Value < 0 ||
                excerpt.Length.Value != excerpt.Text.Length)
            {
                throw new InvalidOperationException(
                    "Reviewed medical literature excerpt provenance is invalid.");
            }
        }
    }

    public Task<IReadOnlyList<ReviewedMedicalLiteratureClassification>>
        GetReviewedClassificationsAsync(
            RequirementId requirementId,
            CancellationToken cancellationToken = default) =>
        GetReviewedClassificationsAsync(
            requirementId: requirementId,
            artifactId: null,
            cancellationToken: cancellationToken);

    public Task<IReadOnlyList<ReviewedMedicalLiteratureClassification>>
        GetReviewedClassificationsAsync(
            ArtifactId artifactId,
            CancellationToken cancellationToken = default) =>
        GetReviewedClassificationsAsync(
            requirementId: null,
            artifactId: artifactId,
            cancellationToken: cancellationToken);

    private async Task<IReadOnlyList<ReviewedMedicalLiteratureClassification>>
        GetReviewedClassificationsAsync(
            RequirementId? requirementId,
            ArtifactId? artifactId,
            CancellationToken cancellationToken)
    {
        if ((requirementId is null) == (artifactId is null))
            throw new ArgumentException(
                "Exactly one reviewed-classification filter is required.");

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        var rows = new List<ReviewedClassificationRow>();

        await using (var command = connection.CreateCommand())
        {
            if (requirementId is not null)
            {
                command.CommandText = """
                    SELECT RequirementId, MedicalLiteratureSourceId,
                           GuidanceRole, ArtifactId, Description,
                           PromotedBy, PromotedUtc, ReviewedBy, ReviewedUtc,
                           IntelligenceOutput, CapabilityId, ProviderId,
                           CorrelationId, EngineName, EngineVersion,
                           ProviderOperationId, StartedUtc, CompletedUtc,
                           RequiresReview, WarningsJson
                    FROM VeteransClaims_ReviewedMedicalLiteratureClassifications
                    WHERE RequirementId = $filter
                    ORDER BY MedicalLiteratureSourceId, GuidanceRole,
                             ArtifactId, CorrelationId;
                    """;
                command.Parameters.AddWithValue(
                    "$filter",
                    requirementId.Value.Value);
            }
            else
            {
                command.CommandText = """
                    SELECT RequirementId, MedicalLiteratureSourceId,
                           GuidanceRole, ArtifactId, Description,
                           PromotedBy, PromotedUtc, ReviewedBy, ReviewedUtc,
                           IntelligenceOutput, CapabilityId, ProviderId,
                           CorrelationId, EngineName, EngineVersion,
                           ProviderOperationId, StartedUtc, CompletedUtc,
                           RequiresReview, WarningsJson
                    FROM VeteransClaims_ReviewedMedicalLiteratureClassifications
                    WHERE ArtifactId = $filter
                    ORDER BY RequirementId, MedicalLiteratureSourceId,
                             GuidanceRole, CorrelationId;
                    """;
                command.Parameters.AddWithValue(
                    "$filter",
                    artifactId!.Value.Value);
            }

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
                rows.Add(ReadReviewedClassificationRow(reader));
        }

        var results =
            new List<ReviewedMedicalLiteratureClassification>(
                rows.Count);

        foreach (var row in rows)
        {
            var excerpts =
                new List<MedicalLiteratureSourceExcerpt>();

            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT ArtifactId, Text, StartOffset, Length
                FROM VeteransClaims_ReviewedMedicalLiteratureExcerpts
                WHERE RequirementId = $requirement
                  AND MedicalLiteratureSourceId = $source
                  AND GuidanceRole = $role
                  AND ArtifactId = $artifact
                  AND CorrelationId = $correlation
                ORDER BY ExcerptOrdinal;
                """;
            command.Parameters.AddWithValue(
                "$requirement",
                row.RequirementId.Value);
            command.Parameters.AddWithValue(
                "$source",
                row.MedicalLiteratureSourceId.Value);
            command.Parameters.AddWithValue(
                "$role",
                row.GuidanceRole);
            command.Parameters.AddWithValue(
                "$artifact",
                row.ArtifactId.Value);
            command.Parameters.AddWithValue(
                "$correlation",
                row.CorrelationId);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                excerpts.Add(
                    new MedicalLiteratureSourceExcerpt
                    {
                        ArtifactId =
                            new ArtifactId(reader.GetString(0)),
                        Text = reader.GetString(1),
                        StartOffset =
                            reader.IsDBNull(2)
                                ? null
                                : reader.GetInt32(2),
                        Length =
                            reader.IsDBNull(3)
                                ? null
                                : reader.GetInt32(3)
                    });
            }

            results.Add(
                new ReviewedMedicalLiteratureClassification
                {
                    Association =
                        new RequirementMedicalLiterature
                        {
                            RequirementId = row.RequirementId,
                            MedicalLiteratureSourceId =
                                row.MedicalLiteratureSourceId,
                            GuidanceRole = row.GuidanceRole,
                            Description = row.Description
                        },
                    ArtifactId = row.ArtifactId,
                    PromotedBy = row.PromotedBy,
                    PromotedUtc = row.PromotedUtc,
                    ReviewedBy = row.ReviewedBy,
                    ReviewedUtc = row.ReviewedUtc,
                    IntelligenceOutput = row.IntelligenceOutput,
                    CapabilityId = row.CapabilityId,
                    ProviderId = row.ProviderId,
                    CorrelationId = row.CorrelationId,
                    EngineName = row.EngineName,
                    EngineVersion = row.EngineVersion,
                    ProviderOperationId = row.ProviderOperationId,
                    StartedUtc = row.StartedUtc,
                    CompletedUtc = row.CompletedUtc,
                    RequiresReview = row.RequiresReview,
                    Warnings = row.Warnings,
                    SourceExcerpts = excerpts
                });
        }

        return results;
    }

    private static ReviewedClassificationRow
        ReadReviewedClassificationRow(SqliteDataReader reader) =>
        new()
        {
            RequirementId = new RequirementId(reader.GetString(0)),
            MedicalLiteratureSourceId =
                new MedicalLiteratureSourceId(reader.GetString(1)),
            GuidanceRole = reader.GetString(2),
            ArtifactId = new ArtifactId(reader.GetString(3)),
            Description = reader.GetString(4),
            PromotedBy = reader.GetString(5),
            PromotedUtc = DateTimeOffset.Parse(reader.GetString(6)),
            ReviewedBy = reader.GetString(7),
            ReviewedUtc = DateTimeOffset.Parse(reader.GetString(8)),
            IntelligenceOutput = reader.GetString(9),
            CapabilityId = reader.GetString(10),
            ProviderId = reader.GetString(11),
            CorrelationId = reader.GetString(12),
            EngineName = reader.GetString(13),
            EngineVersion =
                reader.IsDBNull(14) ? null : reader.GetString(14),
            ProviderOperationId =
                reader.IsDBNull(15) ? null : reader.GetString(15),
            StartedUtc = DateTimeOffset.Parse(reader.GetString(16)),
            CompletedUtc = DateTimeOffset.Parse(reader.GetString(17)),
            RequiresReview = reader.GetInt32(18) != 0,
            Warnings =
                JsonSerializer.Deserialize<string[]>(
                    reader.GetString(19))
                ?? Array.Empty<string>()
        };

    private sealed class ReviewedClassificationRow
    {
        public required RequirementId RequirementId { get; init; }

        public required MedicalLiteratureSourceId
            MedicalLiteratureSourceId { get; init; }

        public required string GuidanceRole { get; init; }

        public required ArtifactId ArtifactId { get; init; }

        public required string Description { get; init; }

        public required string PromotedBy { get; init; }

        public required DateTimeOffset PromotedUtc { get; init; }

        public required string ReviewedBy { get; init; }

        public required DateTimeOffset ReviewedUtc { get; init; }

        public required string IntelligenceOutput { get; init; }

        public required string CapabilityId { get; init; }

        public required string ProviderId { get; init; }

        public required string CorrelationId { get; init; }

        public required string EngineName { get; init; }

        public string? EngineVersion { get; init; }

        public string? ProviderOperationId { get; init; }

        public required DateTimeOffset StartedUtc { get; init; }

        public required DateTimeOffset CompletedUtc { get; init; }

        public required bool RequiresReview { get; init; }

        public required IReadOnlyList<string> Warnings { get; init; }
    }

    private static MedicalLiteratureSource ReadSource(
        SqliteDataReader reader) =>
        new()
        {
            Id = new MedicalLiteratureSourceId(reader.GetString(0)),
            Title = reader.GetString(1),
            Authors = reader.GetString(2),
            Publication = reader.GetString(3),
            PublicationYear =
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
            VaAffiliated = reader.GetInt32(5) != 0,
            VaFunded = reader.GetInt32(6) != 0,
            PeerReviewed = reader.GetInt32(7) != 0,
            FundingSource =
                reader.IsDBNull(8) ? null : reader.GetString(8),
            ResearchOrganization =
                reader.IsDBNull(9) ? null : reader.GetString(9),
            Doi = reader.IsDBNull(10) ? null : reader.GetString(10),
            Pmid = reader.IsDBNull(11) ? null : reader.GetString(11),
            SourceUri =
                reader.IsDBNull(12) ? null : reader.GetString(12),
            SourceHash =
                reader.IsDBNull(13) ? null : reader.GetString(13),
            RetrievedUtc =
                reader.IsDBNull(14)
                    ? null
                    : DateTimeOffset.Parse(reader.GetString(14))
        };
}

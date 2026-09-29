using System.Text.Json;
using System.Globalization;
using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteEvidencePackageRepository :
    IEvidencePackageRepository
{
    private readonly string _databasePath;

    public bool SupportsReviewerOutputProvenance => true;
    public bool SupportsReviewerOutputBuildProvenance => true;

    public SqliteEvidencePackageRepository(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        _databasePath = databasePath;
    }

    private SqliteConnection CreateConnection()
    {
        return VeteransClaimsSqliteConnectionFactory
            .Create(_databasePath);
    }

    public async Task<ReviewerPackageSnapshot?> GetReviewerSnapshotAsync(
        EvidencePackageId packageId, CancellationToken cancellationToken = default)
    {
        var read = await ReadReviewerSnapshotAsync(packageId, cancellationToken);
        if (read.IsLegacy)
            throw new InvalidDataException("Legacy reviewer package has no historical snapshot; immutable historical reconstruction is unavailable. Create a new package from reviewed current inputs.");
        return read.Snapshot;
    }

    public async Task<ReviewerPackageSnapshotRead> ReadReviewerSnapshotAsync(
        EvidencePackageId packageId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT p.ReviewerSnapshotVersion, s.Version, s.Payload, s.Sha256, p.ReviewerSnapshotSealed
            FROM VeteransClaims_EvidencePackages p
            LEFT JOIN VeteransClaims_ReviewerPackageSnapshots s ON s.EvidencePackageId = p.Id
            WHERE p.Id = $id;
            """;
        command.Parameters.AddWithValue("$id", packageId.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
            throw new InvalidOperationException("Reviewer package not found.");
        var version = reader.GetInt32(0);
        if (version == 0 && reader.GetInt32(4) == 0 && reader.IsDBNull(1))
            return new(true, null);
        if (version != 1)
            throw new InvalidDataException("Unsupported or inconsistent reviewer snapshot eligibility.");
        var sealedState = reader.GetInt32(4) == 1;
        if (sealedState == reader.IsDBNull(1))
            throw new InvalidDataException("Reviewer snapshot sealed state and manifest row are inconsistent; historical reconstruction is unavailable.");
        if (!sealedState) return new(false, null);
        var snapshot = new ReviewerPackageSnapshot(packageId, reader.GetInt32(1), reader.GetString(2), reader.GetString(3));
        snapshot.ValidateIntegrity();
        return new(false, snapshot);
    }

    public async Task SaveReviewerSnapshotAsync(ReviewerPackageSnapshot snapshot,
        EvidencePackageDetails expectedMembership, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(expectedMembership);
        snapshot.ValidateMembership(expectedMembership);
        if (snapshot.PackageId != expectedMembership.Package.Id ||
            expectedMembership.Artifacts.Any(x => x.EvidencePackageId != snapshot.PackageId) ||
            expectedMembership.Artifacts.GroupBy(x => x.ArtifactId).Any(g => g.Count() != 1))
            throw new InvalidDataException("Reviewer snapshot membership identity mismatch.");

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT ClaimIssueId, Purpose, ReviewerRole, ServiceConnectionBasisId, ReviewerSnapshotVersion, ReviewerSnapshotSealed
            FROM VeteransClaims_EvidencePackages WHERE Id = $id;
            """;
        command.Parameters.AddWithValue("$id", snapshot.PackageId.Value);
        var sealedState = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            var p = expectedMembership.Package;
            if (!await reader.ReadAsync(cancellationToken) || reader.GetInt32(4) != 1 ||
                reader.GetString(0) != p.ClaimIssueId.Value || reader.GetString(1) != p.Purpose ||
                reader.GetString(2) != p.ReviewerRole ||
                (reader.IsDBNull(3) ? null : reader.GetString(3)) != p.ServiceConnectionBasisId?.Value)
                throw new InvalidDataException("Reviewer snapshot package changed or is legacy.");
            sealedState = reader.GetInt32(5) == 1;
        }
        command.CommandText = """
            SELECT ArtifactId, ContentRole, ReviewerPageSelection
            FROM VeteransClaims_EvidencePackageArtifacts WHERE EvidencePackageId = $id ORDER BY ArtifactId;
            """;
        var actual = new List<(string, string, string?)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                actual.Add((reader.GetString(0), reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2)));
        var expected = expectedMembership.Artifacts.OrderBy(x => x.ArtifactId.Value, StringComparer.Ordinal)
            .Select(x => (x.ArtifactId.Value, x.ContentRole, x.ReviewerPageSelection));
        if (!actual.OrderBy(x => x.Item1, StringComparer.Ordinal).SequenceEqual(expected))
            throw new InvalidDataException("Reviewer package membership or page selections changed during snapshot capture.");

        // Check under the write transaction: an identical retry is a no-op, and
        // direct INSERT OR REPLACE is forbidden by database triggers.
        command.CommandText = "SELECT Version, Payload, Sha256 FROM VeteransClaims_ReviewerPackageSnapshots WHERE EvidencePackageId = $id;";
        var exists = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            exists = await reader.ReadAsync(cancellationToken);
            if (exists != sealedState)
                throw new InvalidDataException("Reviewer snapshot sealed state and manifest row are inconsistent.");
            if (exists && (reader.GetInt32(0) != snapshot.Version ||
                reader.GetString(1) != snapshot.Payload || reader.GetString(2) != snapshot.Sha256))
                throw new InvalidDataException("Conflicting reviewer snapshot already exists for this package.");
        }
        if (!exists)
        {
            command.CommandText = """
                INSERT INTO VeteransClaims_ReviewerPackageSnapshots(EvidencePackageId, Version, Payload, Sha256)
                VALUES ($id, $version, $payload, $hash);
                """;
            command.Parameters.AddWithValue("$version", snapshot.Version);
            command.Parameters.AddWithValue("$payload", snapshot.Payload);
            command.Parameters.AddWithValue("$hash", snapshot.Sha256);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewerPackageOutputProvenance>> GetReviewerOutputProvenanceAsync(
        EvidencePackageId packageId, CancellationToken cancellationToken = default)
    {
        var snapshotState = await ReadReviewerSnapshotAsync(packageId, cancellationToken);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT ProvenanceId, EvidencePackageId, Version, Format, SnapshotSha256,
                   RendererContract, RendererBuild, ConverterIdentity, ConverterVersion,
                   SourceReviewDate, OutputSha256, ByteLength, GeneratedUtc
            FROM VeteransClaims_ReviewerPackageOutputProvenance
            WHERE EvidencePackageId = $id
            ORDER BY GeneratedUtc, ProvenanceId;
            """;
        command.Parameters.AddWithValue("$id", packageId.Value);

        var rows = new List<ReviewerPackageOutputProvenance>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var row = ReadReviewerOutputProvenance(reader);
            row.ValidateIntegrity();
            rows.Add(row);
        }

        if (rows.Count == 0)
            return rows;

        if (snapshotState.IsLegacy || snapshotState.Snapshot is null)
            throw new InvalidDataException(
                "Reviewer output provenance exists without a sealed reviewer snapshot.");

        if (rows.Any(row => !string.Equals(
                row.SnapshotSha256,
                snapshotState.Snapshot.Sha256,
                StringComparison.Ordinal)))
        {
            throw new InvalidDataException(
                "Reviewer output provenance is not bound to the package's sealed snapshot.");
        }

        return rows;
    }

    public async Task SaveReviewerOutputProvenanceAsync(
        ReviewerPackageOutputProvenance provenance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        provenance.ValidateIntegrity();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction = connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            SELECT s.Sha256
            FROM VeteransClaims_EvidencePackages p
            JOIN VeteransClaims_ReviewerPackageSnapshots s
                ON s.EvidencePackageId = p.Id
            WHERE p.Id = $id
              AND p.ReviewerSnapshotVersion = 1
              AND p.ReviewerSnapshotSealed = 1
              AND s.Version = 1;
            """;
        command.Parameters.AddWithValue("$id", provenance.PackageId.Value);

        var snapshotHash = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (snapshotHash is null || !string.Equals(
                snapshotHash,
                provenance.SnapshotSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Reviewer output provenance requires the matching sealed reviewer snapshot.");
        }

        command.CommandText = """
            SELECT ProvenanceId, EvidencePackageId, Version, Format, SnapshotSha256,
                   RendererContract, RendererBuild, ConverterIdentity, ConverterVersion,
                   SourceReviewDate, OutputSha256, ByteLength, GeneratedUtc
            FROM VeteransClaims_ReviewerPackageOutputProvenance
            WHERE ProvenanceId = $provenanceId;
            """;
        command.Parameters.AddWithValue("$provenanceId", provenance.ProvenanceId);

        ReviewerPackageOutputProvenance? existing = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                existing = ReadReviewerOutputProvenance(reader);
                existing.ValidateIntegrity();
            }
        }

        if (existing is not null)
        {
            if (existing != (provenance with { GeneratedUtc = existing.GeneratedUtc }))
                throw new InvalidDataException(
                    "Conflicting reviewer output provenance already exists.");

            await transaction.CommitAsync(cancellationToken);
            return;
        }

        command.CommandText = """
            INSERT INTO VeteransClaims_ReviewerPackageOutputProvenance (
                ProvenanceId, EvidencePackageId, Version, Format, SnapshotSha256,
                RendererContract, RendererBuild, ConverterIdentity, ConverterVersion,
                SourceReviewDate, OutputSha256, ByteLength, GeneratedUtc)
            VALUES (
                $provenanceId, $id, $version, $format, $snapshotHash,
                $rendererContract, $rendererBuild, $converterIdentity, $converterVersion,
                $sourceReviewDate, $outputHash, $byteLength, $generatedUtc);
            """;
        command.Parameters.AddWithValue("$version", provenance.Version);
        command.Parameters.AddWithValue("$format", provenance.Format);
        command.Parameters.AddWithValue("$snapshotHash", provenance.SnapshotSha256);
        command.Parameters.AddWithValue("$rendererContract", provenance.RendererContract);
        command.Parameters.AddWithValue("$rendererBuild", provenance.RendererBuild);
        command.Parameters.AddWithValue(
            "$converterIdentity",
            provenance.ConverterIdentity is null ? DBNull.Value : provenance.ConverterIdentity);
        command.Parameters.AddWithValue(
            "$converterVersion",
            provenance.ConverterVersion is null ? DBNull.Value : provenance.ConverterVersion);
        command.Parameters.AddWithValue(
            "$sourceReviewDate",
            provenance.SourceReviewDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outputHash", provenance.OutputSha256);
        command.Parameters.AddWithValue("$byteLength", provenance.ByteLength);
        command.Parameters.AddWithValue(
            "$generatedUtc",
            provenance.GeneratedUtc.ToString("O", CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReviewerPackageOutputBuildProvenance>>
        GetReviewerOutputBuildProvenanceAsync(
            string provenanceId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT LinkId, ProvenanceId, Version, BuildId,
                   SourceRevisionId, LinkedUtc
            FROM VeteransClaims_ReviewerPackageOutputBuildProvenance
            WHERE ProvenanceId = $id
            ORDER BY LinkedUtc, LinkId;
            """;
        command.Parameters.AddWithValue("$id", provenanceId);

        var rows = new List<ReviewerPackageOutputBuildProvenance>();
        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var row = ReadReviewerOutputBuildProvenance(reader);
            row.ValidateIntegrity();
            rows.Add(row);
        }

        return rows;
    }

    public async Task<ReviewerBuildManifestDocument?> GetBuildManifestAsync(
        string buildId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildId);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();

        command.CommandText = """
            SELECT ManifestJson
            FROM VeteransClaims_ReviewerBuildManifests
            WHERE BuildId = $buildId;
            """;
        command.Parameters.AddWithValue("$buildId", buildId);

        var json = await command.ExecuteScalarAsync(cancellationToken) as string;
        if (json is null)
            return null;

        ReviewerBuildManifestDocument manifest;
        try
        {
            manifest =
                ReviewerBuildManifestDocument.Parse(json)
                ?? throw new InvalidDataException(
                    "Archived reviewer build manifest is invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Archived reviewer build manifest is invalid.",
                ex);
        }

        manifest.ValidateIntegrity();

        if (!string.Equals(
                manifest.BuildId,
                buildId,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Archived reviewer build manifest identity does not match its key.");
        }

        return manifest;
    }

    public async Task SaveBuildManifestAsync(
        ReviewerBuildManifestDocument manifest,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);
        manifest.ValidateIntegrity();

        var json = manifest.Json;

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            SELECT ManifestJson
            FROM VeteransClaims_ReviewerBuildManifests
            WHERE BuildId = $buildId;
            """;
        command.Parameters.AddWithValue("$buildId", manifest.BuildId);

        var existingJson =
            await command.ExecuteScalarAsync(cancellationToken) as string;

        if (existingJson is not null)
        {
            ReviewerBuildManifestDocument existing;
            try
            {
                existing =
                    ReviewerBuildManifestDocument.Parse(existingJson)
                    ?? throw new InvalidDataException(
                        "Archived reviewer build manifest is invalid.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    "Archived reviewer build manifest is invalid.",
                    ex);
            }

            existing.ValidateIntegrity();

            if (!string.Equals(
                    existing.BuildId,
                    manifest.BuildId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Conflicting reviewer build manifest already exists.");
            }

            await transaction.CommitAsync(cancellationToken);
            return;
        }

        command.Parameters.Clear();
        command.CommandText = """
            INSERT INTO VeteransClaims_ReviewerBuildManifests (
                BuildId,
                ManifestJson,
                ArchivedUtc)
            VALUES (
                $buildId,
                $manifestJson,
                $archivedUtc);
            """;

        command.Parameters.AddWithValue("$buildId", manifest.BuildId);
        command.Parameters.AddWithValue("$manifestJson", json);
        command.Parameters.AddWithValue(
            "$archivedUtc",
            DateTimeOffset.UtcNow.ToString(
                "O",
                CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task SaveReviewerOutputBuildProvenanceAsync(
        ReviewerPackageOutputBuildProvenance provenance,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provenance);
        provenance.ValidateIntegrity();

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText = """
            SELECT 1
            FROM VeteransClaims_ReviewerPackageOutputProvenance
            WHERE ProvenanceId = $provenanceId;
            """;
        command.Parameters.AddWithValue(
            "$provenanceId",
            provenance.ProvenanceId);

        if (await command.ExecuteScalarAsync(cancellationToken) is null)
            throw new InvalidDataException(
                "Reviewer output build provenance requires existing output provenance.");

        command.CommandText = """
            SELECT LinkId, ProvenanceId, Version, BuildId,
                   SourceRevisionId, LinkedUtc
            FROM VeteransClaims_ReviewerPackageOutputBuildProvenance
            WHERE LinkId = $linkId;
            """;
        command.Parameters.AddWithValue("$linkId", provenance.LinkId);

        ReviewerPackageOutputBuildProvenance? existing = null;
        await using (var reader =
            await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                existing = ReadReviewerOutputBuildProvenance(reader);
                existing.ValidateIntegrity();
            }
        }

        if (existing is not null)
        {
            if (existing !=
                (provenance with { LinkedUtc = existing.LinkedUtc }))
            {
                throw new InvalidDataException(
                    "Conflicting reviewer output build provenance already exists.");
            }

            await transaction.CommitAsync(cancellationToken);
            return;
        }

        command.CommandText = """
            INSERT INTO VeteransClaims_ReviewerPackageOutputBuildProvenance (
                LinkId, ProvenanceId, Version, BuildId,
                SourceRevisionId, LinkedUtc)
            VALUES (
                $linkId, $provenanceId, $version, $buildId,
                $sourceRevisionId, $linkedUtc);
            """;

        command.Parameters.AddWithValue("$version", provenance.Version);
        command.Parameters.AddWithValue("$buildId", provenance.BuildId);
        command.Parameters.AddWithValue(
            "$sourceRevisionId",
            provenance.SourceRevisionId);
        command.Parameters.AddWithValue(
            "$linkedUtc",
            provenance.LinkedUtc.ToString(
                "O",
                CultureInfo.InvariantCulture));

        await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }


    public Task SaveReviewerOutputWithBuildProvenanceAsync(
        ReviewerPackageOutputProvenance outputProvenance,
        ReviewerPackageOutputBuildProvenance buildProvenance,
        CancellationToken cancellationToken = default) =>
        SaveReviewerOutputWithBuildProvenanceCoreAsync(
            outputProvenance,
            buildProvenance,
            buildManifestJson: null,
            cancellationToken);

    public Task SaveReviewerOutputWithBuildProvenanceAndManifestAsync(
        ReviewerPackageOutputProvenance outputProvenance,
        ReviewerPackageOutputBuildProvenance buildProvenance,
        string buildManifestJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(buildManifestJson);

        return SaveReviewerOutputWithBuildProvenanceCoreAsync(
            outputProvenance,
            buildProvenance,
            buildManifestJson,
            cancellationToken);
    }

    private async Task SaveReviewerOutputWithBuildProvenanceCoreAsync(
        ReviewerPackageOutputProvenance outputProvenance,
        ReviewerPackageOutputBuildProvenance buildProvenance,
        string? buildManifestJson,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(outputProvenance);
        ArgumentNullException.ThrowIfNull(buildProvenance);
        outputProvenance.ValidateIntegrity();
        buildProvenance.ValidateIntegrity();

        ReviewerBuildManifestDocument? buildManifest = null;
        if (buildManifestJson is not null)
        {
            try
            {
                buildManifest =
                    ReviewerBuildManifestDocument.Parse(
                        buildManifestJson)
                    ?? throw new InvalidDataException(
                        "Reviewer build manifest is invalid.");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException(
                    "Reviewer build manifest is invalid.",
                    ex);
            }

            buildManifest.ValidateIntegrity();

            if (!string.Equals(
                    buildManifest.BuildId,
                    buildProvenance.BuildId,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    buildManifest.SourceRevisionId,
                    buildProvenance.SourceRevisionId,
                    StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Reviewer build manifest does not match build provenance.");
            }
        }

        if (!string.Equals(
            outputProvenance.ProvenanceId,
            buildProvenance.ProvenanceId,
            StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Reviewer output and build provenance identities do not match.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);
        await using var transaction =
            connection.BeginTransaction(deferred: false);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        if (buildManifest is not null)
        {
            command.CommandText = """
                SELECT ManifestJson
                FROM VeteransClaims_ReviewerBuildManifests
                WHERE BuildId = $buildId;
                """;
            command.Parameters.AddWithValue(
                "$buildId",
                buildManifest.BuildId);

            var existingManifestJson =
                await command.ExecuteScalarAsync(cancellationToken) as string;

            if (existingManifestJson is not null)
            {
                ReviewerBuildManifestDocument existingManifest;
                try
                {
                    existingManifest =
                        ReviewerBuildManifestDocument.Parse(
                            existingManifestJson)
                        ?? throw new InvalidDataException(
                            "Archived reviewer build manifest is invalid.");
                }
                catch (JsonException ex)
                {
                    throw new InvalidDataException(
                        "Archived reviewer build manifest is invalid.",
                        ex);
                }

                existingManifest.ValidateIntegrity();

                if (!string.Equals(
                        existingManifest.BuildId,
                        buildManifest.BuildId,
                        StringComparison.Ordinal) ||
                    !string.Equals(
                        existingManifest.SourceRevisionId,
                        buildManifest.SourceRevisionId,
                        StringComparison.Ordinal))
                {
                    throw new InvalidDataException(
                        "Conflicting reviewer build manifest already exists.");
                }
            }
            else
            {
                command.Parameters.Clear();
                command.CommandText = """
                    INSERT INTO VeteransClaims_ReviewerBuildManifests (
                        BuildId,
                        ManifestJson,
                        ArchivedUtc)
                    VALUES (
                        $buildId,
                        $manifestJson,
                        $archivedUtc);
                    """;

                command.Parameters.AddWithValue(
                    "$buildId",
                    buildManifest.BuildId);
                command.Parameters.AddWithValue(
                    "$manifestJson",
                    buildManifestJson);
                command.Parameters.AddWithValue(
                    "$archivedUtc",
                    DateTimeOffset.UtcNow.ToString(
                        "O",
                        CultureInfo.InvariantCulture));

                await command.ExecuteNonQueryAsync(cancellationToken);
            }

            command.Parameters.Clear();
        }

        command.CommandText = """
            SELECT s.Sha256
            FROM VeteransClaims_EvidencePackages p
            JOIN VeteransClaims_ReviewerPackageSnapshots s
                ON s.EvidencePackageId = p.Id
            WHERE p.Id = $id
              AND p.ReviewerSnapshotVersion = 1
              AND p.ReviewerSnapshotSealed = 1
              AND s.Version = 1;
            """;
        command.Parameters.AddWithValue(
            "$id",
            outputProvenance.PackageId.Value);

        var snapshotHash =
            await command.ExecuteScalarAsync(cancellationToken) as string;

        if (snapshotHash is null ||
            !string.Equals(
                snapshotHash,
                outputProvenance.SnapshotSha256,
                StringComparison.Ordinal))
        {
            throw new InvalidDataException(
                "Reviewer output provenance requires the matching sealed reviewer snapshot.");
        }

        command.Parameters.Clear();
        command.CommandText = """
            SELECT ProvenanceId, EvidencePackageId, Version, Format, SnapshotSha256,
                   RendererContract, RendererBuild, ConverterIdentity, ConverterVersion,
                   SourceReviewDate, OutputSha256, ByteLength, GeneratedUtc
            FROM VeteransClaims_ReviewerPackageOutputProvenance
            WHERE ProvenanceId = $provenanceId;
            """;
        command.Parameters.AddWithValue(
            "$provenanceId",
            outputProvenance.ProvenanceId);

        ReviewerPackageOutputProvenance? existingOutput = null;
        await using (var reader =
            await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                existingOutput = ReadReviewerOutputProvenance(reader);
                existingOutput.ValidateIntegrity();
            }
        }

        if (existingOutput is not null)
        {
            if (existingOutput !=
                (outputProvenance with
                {
                    GeneratedUtc = existingOutput.GeneratedUtc
                }))
            {
                throw new InvalidDataException(
                    "Conflicting reviewer output provenance already exists.");
            }
        }
        else
        {
            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO VeteransClaims_ReviewerPackageOutputProvenance (
                    ProvenanceId, EvidencePackageId, Version, Format, SnapshotSha256,
                    RendererContract, RendererBuild, ConverterIdentity, ConverterVersion,
                    SourceReviewDate, OutputSha256, ByteLength, GeneratedUtc)
                VALUES (
                    $provenanceId, $id, $version, $format, $snapshotHash,
                    $rendererContract, $rendererBuild, $converterIdentity, $converterVersion,
                    $sourceReviewDate, $outputHash, $byteLength, $generatedUtc);
                """;

            command.Parameters.AddWithValue(
                "$provenanceId",
                outputProvenance.ProvenanceId);
            command.Parameters.AddWithValue(
                "$id",
                outputProvenance.PackageId.Value);
            command.Parameters.AddWithValue(
                "$version",
                outputProvenance.Version);
            command.Parameters.AddWithValue(
                "$format",
                outputProvenance.Format);
            command.Parameters.AddWithValue(
                "$snapshotHash",
                outputProvenance.SnapshotSha256);
            command.Parameters.AddWithValue(
                "$rendererContract",
                outputProvenance.RendererContract);
            command.Parameters.AddWithValue(
                "$rendererBuild",
                outputProvenance.RendererBuild);
            command.Parameters.AddWithValue(
                "$converterIdentity",
                outputProvenance.ConverterIdentity is null
                    ? DBNull.Value
                    : outputProvenance.ConverterIdentity);
            command.Parameters.AddWithValue(
                "$converterVersion",
                outputProvenance.ConverterVersion is null
                    ? DBNull.Value
                    : outputProvenance.ConverterVersion);
            command.Parameters.AddWithValue(
                "$sourceReviewDate",
                outputProvenance.SourceReviewDate.ToString(
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue(
                "$outputHash",
                outputProvenance.OutputSha256);
            command.Parameters.AddWithValue(
                "$byteLength",
                outputProvenance.ByteLength);
            command.Parameters.AddWithValue(
                "$generatedUtc",
                outputProvenance.GeneratedUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        command.Parameters.Clear();
        command.CommandText = """
            SELECT LinkId, ProvenanceId, Version, BuildId,
                   SourceRevisionId, LinkedUtc
            FROM VeteransClaims_ReviewerPackageOutputBuildProvenance
            WHERE LinkId = $linkId;
            """;
        command.Parameters.AddWithValue(
            "$linkId",
            buildProvenance.LinkId);

        ReviewerPackageOutputBuildProvenance? existingBuild = null;
        await using (var reader =
            await command.ExecuteReaderAsync(cancellationToken))
        {
            if (await reader.ReadAsync(cancellationToken))
            {
                existingBuild =
                    ReadReviewerOutputBuildProvenance(reader);
                existingBuild.ValidateIntegrity();
            }
        }

        if (existingBuild is not null)
        {
            if (existingBuild !=
                (buildProvenance with
                {
                    LinkedUtc = existingBuild.LinkedUtc
                }))
            {
                throw new InvalidDataException(
                    "Conflicting reviewer output build provenance already exists.");
            }
        }
        else
        {
            command.Parameters.Clear();
            command.CommandText = """
                INSERT INTO VeteransClaims_ReviewerPackageOutputBuildProvenance (
                    LinkId, ProvenanceId, Version, BuildId,
                    SourceRevisionId, LinkedUtc)
                VALUES (
                    $linkId, $provenanceId, $version, $buildId,
                    $sourceRevisionId, $linkedUtc);
                """;

            command.Parameters.AddWithValue(
                "$linkId",
                buildProvenance.LinkId);
            command.Parameters.AddWithValue(
                "$provenanceId",
                buildProvenance.ProvenanceId);
            command.Parameters.AddWithValue(
                "$version",
                buildProvenance.Version);
            command.Parameters.AddWithValue(
                "$buildId",
                buildProvenance.BuildId);
            command.Parameters.AddWithValue(
                "$sourceRevisionId",
                buildProvenance.SourceRevisionId);
            command.Parameters.AddWithValue(
                "$linkedUtc",
                buildProvenance.LinkedUtc.ToString(
                    "O",
                    CultureInfo.InvariantCulture));

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static ReviewerPackageOutputBuildProvenance
        ReadReviewerOutputBuildProvenance(SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            DateTimeOffset.Parse(
                reader.GetString(5),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));

    private static ReviewerPackageOutputProvenance ReadReviewerOutputProvenance(
        SqliteDataReader reader) =>
        new(
            reader.GetString(0),
            new EvidencePackageId(reader.GetString(1)),
            reader.GetInt32(2),
            reader.GetString(3),
            reader.GetString(4),
            reader.GetString(5),
            reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetString(7),
            reader.IsDBNull(8) ? null : reader.GetString(8),
            DateOnly.ParseExact(reader.GetString(9), "yyyy-MM-dd", CultureInfo.InvariantCulture),
            reader.GetString(10),
            reader.GetInt64(11),
            DateTimeOffset.Parse(
                reader.GetString(12),
                CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind));

    public async Task AddEvidencePackageAsync(
        EvidencePackage evidencePackage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidencePackage);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await InsertEvidencePackageAsync(
            connection,
            null,
            evidencePackage,
            cancellationToken);
    }

    public async Task AddEvidencePackageAsync(
        EvidencePackage evidencePackage,
        IReadOnlyCollection<EvidencePackageArtifact> artifacts,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidencePackage);
        ArgumentNullException.ThrowIfNull(artifacts);

        if (artifacts.Any(
            artifact =>
                artifact.EvidencePackageId !=
                    evidencePackage.Id))
        {
            throw new InvalidOperationException(
                "Every artifact must reference " +
                "the evidence package being persisted.");
        }

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(
                cancellationToken);

        await InsertEvidencePackageAsync(
            connection,
            transaction,
            evidencePackage,
            cancellationToken);

        foreach (var artifact in artifacts)
        {
            await InsertEvidencePackageArtifactAsync(
                connection,
                transaction,
                artifact,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertEvidencePackageAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        EvidencePackage evidencePackage,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidencePackages (
                Id,
                ClaimIssueId,
                Purpose,
                ReviewerRole,
                ServiceConnectionBasisId,
                ReviewerSnapshotVersion,
                CreationOrdinal
            )
            VALUES (
                $id,
                $claimIssueId,
                $purpose,
                $reviewerRole,
                $serviceConnectionBasisId,
                1,
                (
                    SELECT
                        COALESCE(MAX(CreationOrdinal), 0) + 1
                    FROM VeteransClaims_EvidencePackages
                )
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            evidencePackage.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            evidencePackage.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$purpose",
            evidencePackage.Purpose);

        command.Parameters.AddWithValue(
            "$reviewerRole",
            evidencePackage.ReviewerRole);

        command.Parameters.AddWithValue(
            "$serviceConnectionBasisId",
            evidencePackage.ServiceConnectionBasisId.HasValue
                ? evidencePackage.ServiceConnectionBasisId.Value.Value
                : DBNull.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<EvidencePackage?> GetEvidencePackageAsync(
        EvidencePackageId evidencePackageId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id,
                ClaimIssueId,
                Purpose,
                ReviewerRole,
                ServiceConnectionBasisId
            FROM VeteransClaims_EvidencePackages
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            evidencePackageId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EvidencePackage
        {
            Id =
                new EvidencePackageId(
                    reader.GetString(0)),
            ClaimIssueId =
                new ClaimIssueId(
                    reader.GetString(1)),
            Purpose =
                reader.GetString(2),
            ReviewerRole =
                reader.GetString(3),
            ServiceConnectionBasisId =
                reader.IsDBNull(4)
                    ? null
                    : new ServiceConnectionBasisId(
                        reader.GetString(4))
        };
    }

    public async Task AddEvidencePackageArtifactAsync(
        EvidencePackageArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await InsertEvidencePackageArtifactAsync(
            connection,
            null,
            artifact,
            cancellationToken);
    }

    private static async Task InsertEvidencePackageArtifactAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        EvidencePackageArtifact artifact,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidencePackageArtifacts (
                EvidencePackageId,
                ArtifactId,
                ContentRole,
                ReviewerPageSelection
            )
            VALUES (
                $evidencePackageId,
                $artifactId,
                $contentRole,
                $reviewerPageSelection
            );
            """;

        command.Parameters.AddWithValue(
            "$evidencePackageId",
            artifact.EvidencePackageId.Value);

        command.Parameters.AddWithValue(
            "$artifactId",
            artifact.ArtifactId.Value);

        command.Parameters.AddWithValue(
            "$contentRole",
            artifact.ContentRole);

        command.Parameters.AddWithValue(
            "$reviewerPageSelection",
            (object?)artifact.ReviewerPageSelection ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task SetReviewerPageSelectionAsync(
        EvidencePackageId evidencePackageId,
        EMF.Core.Models.Identities.ArtifactId artifactId,
        string? reviewerPageSelection,
        CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            UPDATE VeteransClaims_EvidencePackageArtifacts
            SET ReviewerPageSelection = $reviewerPageSelection
            WHERE EvidencePackageId = $evidencePackageId
              AND ArtifactId = $artifactId;
            """;

        command.Parameters.AddWithValue(
            "$reviewerPageSelection",
            (object?)reviewerPageSelection ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$evidencePackageId",
            evidencePackageId.Value);
        command.Parameters.AddWithValue(
            "$artifactId",
            artifactId.Value);

        var affected =
            await command.ExecuteNonQueryAsync(cancellationToken);

        if (affected != 1)
            throw new InvalidOperationException(
                "Evidence package artifact association was not found.");
    }

    public async Task<IReadOnlyList<EvidencePackageArtifact>>
        GetEvidencePackageArtifactsAsync(
            EvidencePackageId evidencePackageId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                EvidencePackageId,
                ArtifactId,
                ContentRole,
                ReviewerPageSelection
            FROM VeteransClaims_EvidencePackageArtifacts
            WHERE EvidencePackageId = $evidencePackageId
            ORDER BY ArtifactId, ContentRole;
            """;

        command.Parameters.AddWithValue(
            "$evidencePackageId",
            evidencePackageId.Value);

        var results =
            new List<EvidencePackageArtifact>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new EvidencePackageArtifact
                {
                    EvidencePackageId =
                        new EvidencePackageId(
                            reader.GetString(0)),
                    ArtifactId =
                        new EMF.Core.Models.Identities.ArtifactId(
                            reader.GetString(1)),
                    ContentRole =
                        reader.GetString(2),
                    ReviewerPageSelection =
                        reader.IsDBNull(3)
                            ? null
                            : reader.GetString(3)
                });
        }

        return results;
    }

    public async Task<IReadOnlyList<EvidencePackage>>
        GetEvidencePackagesAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                Id,
                ClaimIssueId,
                Purpose,
                ReviewerRole,
                ServiceConnectionBasisId
            FROM VeteransClaims_EvidencePackages
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY CreationOrdinal, Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var results =
            new List<EvidencePackage>();

        await using var reader =
            await command.ExecuteReaderAsync(
                cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new EvidencePackage
                {
                    Id =
                        new EvidencePackageId(
                            reader.GetString(0)),
                    ClaimIssueId =
                        new ClaimIssueId(
                            reader.GetString(1)),
                    Purpose =
                        reader.GetString(2),
                    ReviewerRole =
                        reader.GetString(3),
                    ServiceConnectionBasisId =
                        reader.IsDBNull(4)
                            ? null
                            : new ServiceConnectionBasisId(
                                reader.GetString(4))
                });
        }

        return results;
    }
}

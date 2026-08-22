using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Models.Adjudication;
using EMF.Extensions.VeteransClaims.Models.Identities;
using Microsoft.Data.Sqlite;

namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

public sealed class SqliteEvidenceDevelopmentPlanRepository :
    IEvidenceDevelopmentPlanRepository
{
    private readonly string _databasePath;

    public SqliteEvidenceDevelopmentPlanRepository(
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

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        return new VeteransClaimsSqliteSchema(_databasePath)
            .InitializeAsync(cancellationToken);
    }

    public async Task CreateEvidenceDevelopmentPlanAsync(
        EvidenceDevelopmentPlan plan,
        IReadOnlyCollection<EvidenceDevelopmentPlanEvidenceGap> evidenceGaps,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(evidenceGaps);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);

        await InsertPlanAsync(
            connection, transaction, plan, cancellationToken);

        foreach (var evidenceGap in evidenceGaps)
        {
            await InsertGapAsync(
                connection,
                transaction,
                evidenceGap,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task InsertPlanAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EvidenceDevelopmentPlan plan,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidenceDevelopmentPlans (
                Id, ClaimIssueId, Description
            )
            VALUES ($id, $claimIssueId, $description);
            """;

        command.Parameters.AddWithValue("$id", plan.Id.Value);
        command.Parameters.AddWithValue(
            "$claimIssueId", plan.ClaimIssueId.Value);
        command.Parameters.AddWithValue(
            "$description", plan.Description);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task InsertGapAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        EvidenceDevelopmentPlanEvidenceGap evidenceGap,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;

        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidenceDevelopmentPlanEvidenceGaps (
                EvidenceDevelopmentPlanId, EvidenceGapId
            )
            VALUES ($planId, $evidenceGapId);
            """;

        command.Parameters.AddWithValue(
            "$planId",
            evidenceGap.EvidenceDevelopmentPlanId.Value);

        command.Parameters.AddWithValue(
            "$evidenceGapId",
            evidenceGap.EvidenceGapId.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task AddEvidenceDevelopmentPlanAsync(
        EvidenceDevelopmentPlan plan,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidenceDevelopmentPlans (
                Id,
                ClaimIssueId,
                Description
            )
            VALUES (
                $id,
                $claimIssueId,
                $description
            );
            """;

        command.Parameters.AddWithValue(
            "$id",
            plan.Id.Value);

        command.Parameters.AddWithValue(
            "$claimIssueId",
            plan.ClaimIssueId.Value);

        command.Parameters.AddWithValue(
            "$description",
            plan.Description);

        await command.ExecuteNonQueryAsync(
            cancellationToken);
    }

    public async Task<EvidenceDevelopmentPlan?>
        GetEvidenceDevelopmentPlanAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, Description
            FROM VeteransClaims_EvidenceDevelopmentPlans
            WHERE Id = $id;
            """;

        command.Parameters.AddWithValue(
            "$id",
            planId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EvidenceDevelopmentPlan
        {
            Id = new EvidenceDevelopmentPlanId(
                reader.GetString(0)),
            ClaimIssueId = new ClaimIssueId(
                reader.GetString(1)),
            Description = reader.GetString(2)
        };
    }




    public async Task AddEvidenceDevelopmentPlanArtifactAsync(
        EvidenceDevelopmentPlanArtifact artifact,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artifact);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidenceDevelopmentPlanArtifacts (
                EvidenceDevelopmentPlanId,
                ArtifactId,
                Role
            )
            VALUES (
                $planId,
                $artifactId,
                $role
            );
            """;

        command.Parameters.AddWithValue(
            "$planId",
            artifact.EvidenceDevelopmentPlanId.Value);

        command.Parameters.AddWithValue(
            "$artifactId",
            artifact.ArtifactId.Value);

        command.Parameters.AddWithValue(
            "$role",
            artifact.Role);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EvidenceDevelopmentPlanArtifact>>
        GetEvidenceDevelopmentPlanArtifactsAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EvidenceDevelopmentPlanId, ArtifactId, Role
            FROM VeteransClaims_EvidenceDevelopmentPlanArtifacts
            WHERE EvidenceDevelopmentPlanId = $planId
            ORDER BY ArtifactId, Role;
            """;

        command.Parameters.AddWithValue(
            "$planId",
            planId.Value);

        var results =
            new List<EvidenceDevelopmentPlanArtifact>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new EvidenceDevelopmentPlanArtifact
                {
                    EvidenceDevelopmentPlanId =
                        new EvidenceDevelopmentPlanId(
                            reader.GetString(0)),
                    ArtifactId =
                        new EMF.Core.Models.Identities.ArtifactId(
                            reader.GetString(1)),
                    Role =
                        reader.GetString(2)
                });
        }

        return results;
    }





    public async Task AddEvidenceDevelopmentPlanEvidenceGapAsync(
        EvidenceDevelopmentPlanEvidenceGap evidenceGap,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(evidenceGap);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidenceDevelopmentPlanEvidenceGaps (
                EvidenceDevelopmentPlanId,
                EvidenceGapId
            )
            VALUES (
                $planId,
                $evidenceGapId
            );
            """;

        command.Parameters.AddWithValue(
            "$planId",
            evidenceGap.EvidenceDevelopmentPlanId.Value);

        command.Parameters.AddWithValue(
            "$evidenceGapId",
            evidenceGap.EvidenceGapId.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EvidenceDevelopmentPlanEvidenceGap>>
        GetEvidenceDevelopmentPlanEvidenceGapsAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EvidenceDevelopmentPlanId, EvidenceGapId
            FROM VeteransClaims_EvidenceDevelopmentPlanEvidenceGaps
            WHERE EvidenceDevelopmentPlanId = $planId
            ORDER BY EvidenceGapId;
            """;

        command.Parameters.AddWithValue(
            "$planId",
            planId.Value);

        var results =
            new List<EvidenceDevelopmentPlanEvidenceGap>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new EvidenceDevelopmentPlanEvidenceGap
                {
                    EvidenceDevelopmentPlanId =
                        new EvidenceDevelopmentPlanId(
                            reader.GetString(0)),
                    EvidenceGapId =
                        new EvidenceGapId(
                            reader.GetString(1))
                });
        }

        return results;
    }


    public async Task AddEvidenceDevelopmentPlanRequirementAsync(
        EvidenceDevelopmentPlanRequirement requirement,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requirement);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidenceDevelopmentPlanRequirements (
                EvidenceDevelopmentPlanId,
                RequirementId
            )
            VALUES (
                $planId,
                $requirementId
            );
            """;

        command.Parameters.AddWithValue(
            "$planId",
            requirement.EvidenceDevelopmentPlanId.Value);

        command.Parameters.AddWithValue(
            "$requirementId",
            requirement.RequirementId.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EvidenceDevelopmentPlanRequirement>>
        GetEvidenceDevelopmentPlanRequirementsAsync(
            EvidenceDevelopmentPlanId planId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EvidenceDevelopmentPlanId, RequirementId
            FROM VeteransClaims_EvidenceDevelopmentPlanRequirements
            WHERE EvidenceDevelopmentPlanId = $planId
            ORDER BY RequirementId;
            """;

        command.Parameters.AddWithValue(
            "$planId",
            planId.Value);

        var results =
            new List<EvidenceDevelopmentPlanRequirement>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            results.Add(
                new EvidenceDevelopmentPlanRequirement
                {
                    EvidenceDevelopmentPlanId =
                        new EvidenceDevelopmentPlanId(
                            reader.GetString(0)),
                    RequirementId =
                        new RequirementId(
                            reader.GetString(1))
                });
        }

        return results;
    }


    public async Task<IReadOnlyList<EvidenceDevelopmentPlan>>
        GetEvidenceDevelopmentPlansAsync(
            ClaimIssueId claimIssueId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT Id, ClaimIssueId, Description
            FROM VeteransClaims_EvidenceDevelopmentPlans
            WHERE ClaimIssueId = $claimIssueId
            ORDER BY Id;
            """;

        command.Parameters.AddWithValue(
            "$claimIssueId",
            claimIssueId.Value);

        var plans = new List<EvidenceDevelopmentPlan>();

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            plans.Add(
                new EvidenceDevelopmentPlan
                {
                    Id = new EvidenceDevelopmentPlanId(
                        reader.GetString(0)),
                    ClaimIssueId = new ClaimIssueId(
                        reader.GetString(1)),
                    Description = reader.GetString(2)
                });
        }

        return plans;
    }

    public async Task AddEvidenceDevelopmentExecutionAsync(
        EvidenceDevelopmentExecution execution,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(execution);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            INSERT INTO VeteransClaims_EvidenceDevelopmentExecutions (
                EvidenceDevelopmentPlanId,
                EvidenceGapId,
                WorkflowId
            )
            VALUES (
                $planId,
                $evidenceGapId,
                $workflowId
            );
            """;

        command.Parameters.AddWithValue(
            "$planId",
            execution.EvidenceDevelopmentPlanId.Value);

        command.Parameters.AddWithValue(
            "$evidenceGapId",
            execution.EvidenceGapId.Value);

        command.Parameters.AddWithValue(
            "$workflowId",
            execution.WorkflowId.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<EvidenceDevelopmentExecution?>
        GetEvidenceDevelopmentExecutionAsync(
            EvidenceDevelopmentPlanId planId,
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT
                EvidenceDevelopmentPlanId,
                EvidenceGapId,
                WorkflowId
            FROM VeteransClaims_EvidenceDevelopmentExecutions
            WHERE EvidenceDevelopmentPlanId = $planId
              AND EvidenceGapId = $evidenceGapId;
            """;

        command.Parameters.AddWithValue(
            "$planId",
            planId.Value);

        command.Parameters.AddWithValue(
            "$evidenceGapId",
            evidenceGapId.Value);

        await using var reader =
            await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new EvidenceDevelopmentExecution
        {
            EvidenceDevelopmentPlanId =
                new EvidenceDevelopmentPlanId(reader.GetString(0)),
            EvidenceGapId =
                new EvidenceGapId(reader.GetString(1)),
            WorkflowId =
                new EMF.Core.Models.Identities.WorkflowId(
                    reader.GetString(2))
        };
    }


    public async Task AddEvidenceDevelopmentResultAsync(
        EvidenceDevelopmentResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        using var transaction =
            connection.BeginTransaction();

        await using (var command = connection.CreateCommand())
        {
            command.Transaction = transaction;
            command.CommandText =
                """
                INSERT INTO VeteransClaims_EvidenceDevelopmentResults (
                    EvidenceGapId,
                    RequirementId
                )
                VALUES (
                    $evidenceGapId,
                    $requirementId
                );
                """;

            command.Parameters.AddWithValue(
                "$evidenceGapId",
                result.EvidenceGapId.Value);

            command.Parameters.AddWithValue(
                "$requirementId",
                result.RequirementId.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var guidance in result.EvidenceGuidance)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText =
                """
                INSERT INTO VeteransClaims_EvidenceDevelopmentResultGuidance (
                    EvidenceGapId,
                    GuidanceId
                )
                VALUES (
                    $evidenceGapId,
                    $guidanceId
                );
                """;

            command.Parameters.AddWithValue(
                "$evidenceGapId",
                result.EvidenceGapId.Value);

            command.Parameters.AddWithValue(
                "$guidanceId",
                guidance.Id.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var recognition in result.RecognitionMatches)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText =
                """
                INSERT INTO VeteransClaims_EvidenceDevelopmentResultRecognitionMatches (
                    EvidenceGapId,
                    RecognitionTermId
                )
                VALUES (
                    $evidenceGapId,
                    $recognitionTermId
                );
                """;

            command.Parameters.AddWithValue(
                "$evidenceGapId",
                result.EvidenceGapId.Value);

            command.Parameters.AddWithValue(
                "$recognitionTermId",
                recognition.TermId.Value);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var link in result.RecognitionMatchArtifacts)
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;

            command.CommandText =
                """
                INSERT INTO VeteransClaims_EvidenceDevelopmentResultRecognitionMatchArtifacts (
                    EvidenceGapId,
                    RecognitionTermId,
                    ArtifactId,
                    Role
                )
                VALUES (
                    $evidenceGapId,
                    $recognitionTermId,
                    $artifactId,
                    $role
                );
                """;

            command.Parameters.AddWithValue(
                "$evidenceGapId",
                result.EvidenceGapId.Value);

            command.Parameters.AddWithValue(
                "$recognitionTermId",
                link.RecognitionTermId.Value);

            command.Parameters.AddWithValue(
                "$artifactId",
                link.ArtifactId.Value);

            command.Parameters.AddWithValue(
                "$role",
                link.Role);

            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }


    public async Task<EvidenceDevelopmentResult?>
        GetEvidenceDevelopmentResultAsync(
            EvidenceGapId evidenceGapId,
            CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();
        await connection.OpenAsync(cancellationToken);

        RequirementId? requirementId = null;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT RequirementId
                FROM VeteransClaims_EvidenceDevelopmentResults
                WHERE EvidenceGapId = $evidenceGapId;
                """;

            command.Parameters.AddWithValue(
                "$evidenceGapId",
                evidenceGapId.Value);

            var value =
                await command.ExecuteScalarAsync(cancellationToken);

            if (value is null)
            {
                return null;
            }

            requirementId =
                new RequirementId((string)value);
        }

        var guidance =
            new List<EvidenceRequirementGuidance>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT
                    g.Id,
                    g.RequirementId,
                    g.EvidenceClassification,
                    g.GuidanceRole,
                    g.Description
                FROM VeteransClaims_EvidenceDevelopmentResultGuidance rg
                JOIN VeteransClaims_EvidenceRequirementGuidance g
                  ON g.Id = rg.GuidanceId
                WHERE rg.EvidenceGapId = $evidenceGapId
                ORDER BY g.Id;
                """;

            command.Parameters.AddWithValue(
                "$evidenceGapId",
                evidenceGapId.Value);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                guidance.Add(
                    new EvidenceRequirementGuidance
                    {
                        Id =
                            new EvidenceRequirementGuidanceId(
                                reader.GetString(0)),
                        RequirementId =
                            new RequirementId(
                                reader.GetString(1)),
                        EvidenceClassification =
                            reader.GetString(2),
                        GuidanceRole =
                            reader.GetString(3),
                        Description =
                            reader.GetString(4)
                    });
            }
        }

        var recognitions =
            new List<EvidenceRecognitionMatch>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT
                    t.Id,
                    t.Term,
                    t.RecognitionRole,
                    t.AuthoritySource
                FROM VeteransClaims_EvidenceDevelopmentResultRecognitionMatches rm
                JOIN VeteransClaims_EvidenceRecognitionTerms t
                  ON t.Id = rm.RecognitionTermId
                WHERE rm.EvidenceGapId = $evidenceGapId
                ORDER BY t.Id;
                """;

            command.Parameters.AddWithValue(
                "$evidenceGapId",
                evidenceGapId.Value);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                recognitions.Add(
                    new EvidenceRecognitionMatch
                    {
                        TermId =
                            new EvidenceRecognitionTermId(
                                reader.GetString(0)),
                        Term =
                            reader.GetString(1),
                        RecognitionRole =
                            reader.GetString(2),
                        AuthoritySource =
                            reader.GetString(3)
                    });
            }
        }

        var recognitionArtifacts =
            new List<EvidenceRecognitionMatchArtifact>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText =
                """
                SELECT
                    RecognitionTermId,
                    ArtifactId,
                    Role
                FROM VeteransClaims_EvidenceDevelopmentResultRecognitionMatchArtifacts
                WHERE EvidenceGapId = $evidenceGapId
                ORDER BY RecognitionTermId, ArtifactId, Role;
                """;

            command.Parameters.AddWithValue(
                "$evidenceGapId",
                evidenceGapId.Value);

            await using var reader =
                await command.ExecuteReaderAsync(cancellationToken);

            while (await reader.ReadAsync(cancellationToken))
            {
                recognitionArtifacts.Add(
                    new EvidenceRecognitionMatchArtifact
                    {
                        RecognitionTermId =
                            new EvidenceRecognitionTermId(
                                reader.GetString(0)),
                        ArtifactId =
                            new EMF.Core.Models.Identities.ArtifactId(
                                reader.GetString(1)),
                        Role =
                            reader.GetString(2)
                    });
            }
        }

        return new EvidenceDevelopmentResult
        {
            EvidenceGapId = evidenceGapId,
            RequirementId = requirementId.Value,
            EvidenceGuidance = guidance,
            RecognitionMatches = recognitions,
            RecognitionMatchArtifacts =
                recognitionArtifacts
        };
    }

}

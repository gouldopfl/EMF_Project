namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite;

internal static class VeteransClaimsSqliteMigrations
{
    public static IReadOnlyList<
        VeteransClaimsSqliteMigration> All
    { get; } =
        new[]
        {
            new VeteransClaimsSqliteMigration(
                1,
                "InitialVeteransClaimsSchema",
                """
            CREATE TABLE IF NOT EXISTS VeteransClaims_Veterans (
                Id TEXT PRIMARY KEY
            );

            CREATE TABLE IF NOT EXISTS VeteransClaims_Claims (
                Id TEXT PRIMARY KEY,
                VeteranId TEXT NOT NULL,
                FOREIGN KEY (VeteranId)
                    REFERENCES VeteransClaims_Veterans (Id)
            );

            CREATE INDEX IF NOT EXISTS
                IX_VeteransClaims_Claims_VeteranId
            ON VeteransClaims_Claims (VeteranId);

            CREATE TABLE IF NOT EXISTS VeteransClaims_ClaimIssues (
                Id TEXT PRIMARY KEY,
                ClaimId TEXT NOT NULL,
                ClaimIssueType TEXT NOT NULL,
                FOREIGN KEY (ClaimId)
                    REFERENCES VeteransClaims_Claims (Id)
            );

            CREATE INDEX IF NOT EXISTS
                IX_VeteransClaims_ClaimIssues_ClaimId
            ON VeteransClaims_ClaimIssues (ClaimId);

            CREATE TABLE IF NOT EXISTS VeteransClaims_Submissions (
                Id TEXT PRIMARY KEY,
                ClaimId TEXT NOT NULL,
                SubmissionType TEXT NOT NULL,
                FOREIGN KEY (ClaimId)
                    REFERENCES VeteransClaims_Claims (Id)
            );

            CREATE INDEX IF NOT EXISTS
                IX_VeteransClaims_Submissions_ClaimId
            ON VeteransClaims_Submissions (ClaimId);

            CREATE TABLE IF NOT EXISTS
                VeteransClaims_SubmissionClaimIssues (
                    SubmissionId TEXT NOT NULL,
                    ClaimIssueId TEXT NOT NULL,
                    PRIMARY KEY (
                        SubmissionId,
                        ClaimIssueId
                    ),
                    FOREIGN KEY (SubmissionId)
                        REFERENCES VeteransClaims_Submissions (Id),
                    FOREIGN KEY (ClaimIssueId)
                        REFERENCES VeteransClaims_ClaimIssues (Id)
                );

            CREATE INDEX IF NOT EXISTS
                IX_VeteransClaims_SubmissionClaimIssues_Issue
            ON VeteransClaims_SubmissionClaimIssues (
                ClaimIssueId
            );

            CREATE TABLE IF NOT EXISTS VeteransClaims_VaDecisions (
                Id TEXT PRIMARY KEY,
                DecisionDate TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS VeteransClaims_IssueDecisions (
                Id TEXT PRIMARY KEY,
                VaDecisionId TEXT NOT NULL,
                ClaimIssueId TEXT NOT NULL,
                Outcome TEXT NOT NULL,
                FOREIGN KEY (VaDecisionId)
                    REFERENCES VeteransClaims_VaDecisions (Id),
                FOREIGN KEY (ClaimIssueId)
                    REFERENCES VeteransClaims_ClaimIssues (Id)
            );

            CREATE INDEX IF NOT EXISTS
                IX_VeteransClaims_IssueDecisions_VaDecisionId
            ON VeteransClaims_IssueDecisions (VaDecisionId);

            CREATE INDEX IF NOT EXISTS
                IX_VeteransClaims_IssueDecisions_ClaimIssueId
            ON VeteransClaims_IssueDecisions (ClaimIssueId);

            CREATE TABLE IF NOT EXISTS
                VeteransClaims_IssueDecisionSubmissions (
                    IssueDecisionId TEXT NOT NULL,
                    SubmissionId TEXT NOT NULL,
                    PRIMARY KEY (
                        IssueDecisionId,
                        SubmissionId
                    ),
                    FOREIGN KEY (IssueDecisionId)
                        REFERENCES VeteransClaims_IssueDecisions (Id),
                    FOREIGN KEY (SubmissionId)
                        REFERENCES VeteransClaims_Submissions (Id)
                );

            CREATE INDEX IF NOT EXISTS
                IX_VeteransClaims_IssueDecisionSubmissions_Submission
            ON VeteransClaims_IssueDecisionSubmissions (
                SubmissionId
            );

            CREATE TABLE IF NOT EXISTS
                VeteransClaims_DisabilityEvaluations (
                    Id TEXT PRIMARY KEY,
                    IssueDecisionId TEXT NOT NULL,
                    Evaluation TEXT NOT NULL,
                    FOREIGN KEY (IssueDecisionId)
                        REFERENCES VeteransClaims_IssueDecisions (Id)
                );

            CREATE INDEX IF NOT EXISTS
                IX_VeteransClaims_DisabilityEvaluations_IssueDecision
            ON VeteransClaims_DisabilityEvaluations (
                IssueDecisionId
            );

            CREATE TABLE IF NOT EXISTS
                VeteransClaims_EffectiveDates (
                    Id TEXT PRIMARY KEY,
                    DisabilityEvaluationId TEXT NOT NULL UNIQUE,
                    EffectiveDate TEXT NOT NULL,
                    FOREIGN KEY (DisabilityEvaluationId)
                        REFERENCES VeteransClaims_DisabilityEvaluations (Id)
                );
            """),
            new VeteransClaimsSqliteMigration(
                2,
                "AddServiceEventsAndExposures",
                """
                CREATE TABLE VeteransClaims_ServiceEvents (
                    Id TEXT PRIMARY KEY,
                    VeteranId TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    FOREIGN KEY (VeteranId)
                        REFERENCES VeteransClaims_Veterans (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_ServiceEvents_VeteranId
                ON VeteransClaims_ServiceEvents (VeteranId);

                CREATE TABLE VeteransClaims_Exposures (
                    Id TEXT PRIMARY KEY,
                    VeteranId TEXT NOT NULL,
                    ExposureType TEXT NOT NULL,
                    FOREIGN KEY (VeteranId)
                        REFERENCES VeteransClaims_Veterans (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_Exposures_VeteranId
                ON VeteransClaims_Exposures (VeteranId);

                CREATE TABLE
                    VeteransClaims_ServiceEventExposures (
                        ServiceEventId TEXT NOT NULL,
                        ExposureId TEXT NOT NULL,
                        PRIMARY KEY (
                            ServiceEventId,
                            ExposureId
                        ),
                        FOREIGN KEY (ServiceEventId)
                            REFERENCES VeteransClaims_ServiceEvents (Id),
                        FOREIGN KEY (ExposureId)
                            REFERENCES VeteransClaims_Exposures (Id)
                    );

                CREATE INDEX
                    IX_VeteransClaims_ServiceEventExposures_Exposure
                ON VeteransClaims_ServiceEventExposures (
                    ExposureId
                );
                """),
            new VeteransClaimsSqliteMigration(
                3,
                "AddClaimedAndMedicalConditions",
                """
                CREATE TABLE VeteransClaims_ClaimedConditions (
                    Id TEXT PRIMARY KEY,
                    ClaimIssueId TEXT NOT NULL,
                    Name TEXT NOT NULL,
                    FOREIGN KEY (ClaimIssueId)
                        REFERENCES VeteransClaims_ClaimIssues (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_ClaimedConditions_ClaimIssueId
                ON VeteransClaims_ClaimedConditions (
                    ClaimIssueId
                );

                CREATE TABLE VeteransClaims_MedicalConditions (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL
                );
                """),
            new VeteransClaimsSqliteMigration(
                4,
                "AddVeteranMedicalConditions",
                """
                CREATE TABLE
                    VeteransClaims_VeteranMedicalConditions (
                        VeteranId TEXT NOT NULL,
                        MedicalConditionId TEXT NOT NULL,
                        PRIMARY KEY (
                            VeteranId,
                            MedicalConditionId
                        ),
                        FOREIGN KEY (VeteranId)
                            REFERENCES VeteransClaims_Veterans (Id),
                        FOREIGN KEY (MedicalConditionId)
                            REFERENCES VeteransClaims_MedicalConditions (Id)
                    );

                CREATE INDEX
                    IX_VeteransClaims_VeteranMedicalConditions_Condition
                ON VeteransClaims_VeteranMedicalConditions (
                    MedicalConditionId
                );
                """),
            new VeteransClaimsSqliteMigration(
                5,
                "AddClaimedConditionMedicalConditions",
                """
                CREATE TABLE
                    VeteransClaims_ClaimedConditionMedicalConditions (
                        ClaimedConditionId TEXT NOT NULL,
                        MedicalConditionId TEXT NOT NULL,
                        PRIMARY KEY (
                            ClaimedConditionId,
                            MedicalConditionId
                        ),
                        FOREIGN KEY (ClaimedConditionId)
                            REFERENCES VeteransClaims_ClaimedConditions (Id),
                        FOREIGN KEY (MedicalConditionId)
                            REFERENCES VeteransClaims_MedicalConditions (Id)
                    );

                CREATE INDEX
                    IX_VeteransClaims_ClaimedConditionMedicalConditions_Condition
                ON VeteransClaims_ClaimedConditionMedicalConditions (
                    MedicalConditionId
                );
                """),
            new VeteransClaimsSqliteMigration(
                6,
                "AddServiceConnectionTheoriesAndBases",
                """
                CREATE TABLE
                    VeteransClaims_ServiceConnectionTheories (
                        Id TEXT PRIMARY KEY,
                        ClaimIssueId TEXT NOT NULL,
                        TheoryType TEXT NOT NULL,
                        UNIQUE (
                            Id,
                            ClaimIssueId
                        ),
                        FOREIGN KEY (ClaimIssueId)
                            REFERENCES VeteransClaims_ClaimIssues (Id)
                    );

                CREATE INDEX
                    IX_VeteransClaims_ServiceConnectionTheories_Issue
                ON VeteransClaims_ServiceConnectionTheories (
                    ClaimIssueId
                );

                CREATE TABLE
                    VeteransClaims_ServiceConnectionBases (
                        Id TEXT PRIMARY KEY,
                        ClaimIssueId TEXT NOT NULL,
                        ServiceConnectionTheoryId TEXT NOT NULL,
                        FOREIGN KEY (ClaimIssueId)
                            REFERENCES VeteransClaims_ClaimIssues (Id),
                        FOREIGN KEY (
                            ServiceConnectionTheoryId,
                            ClaimIssueId
                        )
                            REFERENCES
                                VeteransClaims_ServiceConnectionTheories (
                                    Id,
                                    ClaimIssueId
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_ServiceConnectionBases_Issue
                ON VeteransClaims_ServiceConnectionBases (
                    ClaimIssueId
                );

                CREATE INDEX
                    IX_VeteransClaims_ServiceConnectionBases_Theory
                ON VeteransClaims_ServiceConnectionBases (
                    ServiceConnectionTheoryId
                );
                """),
            new VeteransClaimsSqliteMigration(
                7,
                "AddBasisClaimedConditions",
                """
                CREATE TABLE
                    VeteransClaims_BasisClaimedConditions (
                        ServiceConnectionBasisId TEXT NOT NULL,
                        ClaimedConditionId TEXT NOT NULL,
                        PRIMARY KEY (
                            ServiceConnectionBasisId,
                            ClaimedConditionId
                        ),
                        FOREIGN KEY (ServiceConnectionBasisId)
                            REFERENCES
                                VeteransClaims_ServiceConnectionBases (
                                    Id
                                ),
                        FOREIGN KEY (ClaimedConditionId)
                            REFERENCES VeteransClaims_ClaimedConditions (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_BasisClaimedConditions_Condition
                ON VeteransClaims_BasisClaimedConditions (
                    ClaimedConditionId
                );
                """),
            new VeteransClaimsSqliteMigration(
                8,
                "AddBasisServiceEvents",
                """
                CREATE TABLE
                    VeteransClaims_BasisServiceEvents (
                        ServiceConnectionBasisId TEXT NOT NULL,
                        ServiceEventId TEXT NOT NULL,
                        PRIMARY KEY (
                            ServiceConnectionBasisId,
                            ServiceEventId
                        ),
                        FOREIGN KEY (ServiceConnectionBasisId)
                            REFERENCES
                                VeteransClaims_ServiceConnectionBases (
                                    Id
                                ),
                        FOREIGN KEY (ServiceEventId)
                            REFERENCES VeteransClaims_ServiceEvents (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_BasisServiceEvents_Event
                ON VeteransClaims_BasisServiceEvents (
                    ServiceEventId
                );
                """),
            new VeteransClaimsSqliteMigration(
                9,
                "AddBasisExposures",
                """
                CREATE TABLE
                    VeteransClaims_BasisExposures (
                        ServiceConnectionBasisId TEXT NOT NULL,
                        ExposureId TEXT NOT NULL,
                        PRIMARY KEY (
                            ServiceConnectionBasisId,
                            ExposureId
                        ),
                        FOREIGN KEY (ServiceConnectionBasisId)
                            REFERENCES
                                VeteransClaims_ServiceConnectionBases (
                                    Id
                                ),
                        FOREIGN KEY (ExposureId)
                            REFERENCES VeteransClaims_Exposures (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_BasisExposures_Exposure
                ON VeteransClaims_BasisExposures (
                    ExposureId
                );
                """),
            new VeteransClaimsSqliteMigration(
                10,
                "AddBasisServiceConnectedConditions",
                """
                CREATE TABLE
                    VeteransClaims_BasisServiceConnectedConditions (
                        ServiceConnectionBasisId TEXT NOT NULL,
                        ServiceConnectedConditionId TEXT NOT NULL,
                        PRIMARY KEY (
                            ServiceConnectionBasisId,
                            ServiceConnectedConditionId
                        ),
                        FOREIGN KEY (ServiceConnectionBasisId)
                            REFERENCES
                                VeteransClaims_ServiceConnectionBases (
                                    Id
                                ),
                        FOREIGN KEY (ServiceConnectedConditionId)
                            REFERENCES
                                VeteransClaims_MedicalConditions (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_BasisServiceConnectedConditions_Condition
                ON
                    VeteransClaims_BasisServiceConnectedConditions (
                        ServiceConnectedConditionId
                    );
                """),
            new VeteransClaimsSqliteMigration(
                11,
                "AddBasisPreexistingConditions",
                """
                CREATE TABLE
                    VeteransClaims_BasisPreexistingConditions (
                        ServiceConnectionBasisId TEXT NOT NULL,
                        PreexistingConditionId TEXT NOT NULL,
                        PRIMARY KEY (
                            ServiceConnectionBasisId,
                            PreexistingConditionId
                        ),
                        FOREIGN KEY (ServiceConnectionBasisId)
                            REFERENCES
                                VeteransClaims_ServiceConnectionBases (
                                    Id
                                ),
                        FOREIGN KEY (PreexistingConditionId)
                            REFERENCES
                                VeteransClaims_MedicalConditions (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_BasisPreexistingConditions_Condition
                ON VeteransClaims_BasisPreexistingConditions (
                    PreexistingConditionId
                );
                """),
            new VeteransClaimsSqliteMigration(
                12,
                "AddRegulatoryFoundation",
                """
                CREATE TABLE
                    VeteransClaims_RegulatoryAuthorities (
                        Id TEXT PRIMARY KEY,
                        AuthorityType TEXT NOT NULL,
                        Citation TEXT NOT NULL,
                        Title TEXT NOT NULL
                    );

                CREATE TABLE
                    VeteransClaims_RegulatoryProvisions (
                        Id TEXT PRIMARY KEY,
                        RegulatoryAuthorityId TEXT NOT NULL,
                        ProvisionType TEXT NOT NULL,
                        Citation TEXT NOT NULL,
                        FOREIGN KEY (RegulatoryAuthorityId)
                            REFERENCES
                                VeteransClaims_RegulatoryAuthorities (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_RegulatoryProvisions_Authority
                ON VeteransClaims_RegulatoryProvisions (
                    RegulatoryAuthorityId
                );

                CREATE TABLE
                    VeteransClaims_Requirements (
                        Id TEXT PRIMARY KEY,
                        RegulatoryProvisionId TEXT NOT NULL,
                        Description TEXT NOT NULL,
                        FOREIGN KEY (RegulatoryProvisionId)
                            REFERENCES
                                VeteransClaims_RegulatoryProvisions (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_Requirements_Provision
                ON VeteransClaims_Requirements (
                    RegulatoryProvisionId
                );
                """),
            new VeteransClaimsSqliteMigration(
                13,
                "AddIssueDecisionRegulatoryProvisions",
                """
                CREATE TABLE
                    VeteransClaims_IssueDecisionRegulatoryProvisions (
                        IssueDecisionId TEXT NOT NULL,
                        RegulatoryProvisionId TEXT NOT NULL,
                        PRIMARY KEY (
                            IssueDecisionId,
                            RegulatoryProvisionId
                        ),
                        FOREIGN KEY (IssueDecisionId)
                            REFERENCES
                                VeteransClaims_IssueDecisions (
                                    Id
                                ),
                        FOREIGN KEY (RegulatoryProvisionId)
                            REFERENCES
                                VeteransClaims_RegulatoryProvisions (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_IssueDecisionRegulatoryProvisions_Provision
                ON
                    VeteransClaims_IssueDecisionRegulatoryProvisions (
                        RegulatoryProvisionId
                    );
                """),
            new VeteransClaimsSqliteMigration(
                14,
                "AddDisabilityEvaluationRegulatoryProvisions",
                """
                CREATE TABLE
                    VeteransClaims_DisabilityEvaluationRegulatoryProvisions (
                        DisabilityEvaluationId TEXT NOT NULL,
                        RegulatoryProvisionId TEXT NOT NULL,
                        PRIMARY KEY (
                            DisabilityEvaluationId,
                            RegulatoryProvisionId
                        ),
                        FOREIGN KEY (DisabilityEvaluationId)
                            REFERENCES
                                VeteransClaims_DisabilityEvaluations (
                                    Id
                                ),
                        FOREIGN KEY (RegulatoryProvisionId)
                            REFERENCES
                                VeteransClaims_RegulatoryProvisions (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_DisabilityEvaluationRegulatoryProvisions_Provision
                ON
                    VeteransClaims_DisabilityEvaluationRegulatoryProvisions (
                        RegulatoryProvisionId
                    );
                """),
            new VeteransClaimsSqliteMigration(
                15,
                "AddEffectiveDateRegulatoryProvisions",
                """
                CREATE TABLE
                    VeteransClaims_EffectiveDateRegulatoryProvisions (
                        EffectiveDateId TEXT NOT NULL,
                        RegulatoryProvisionId TEXT NOT NULL,
                        PRIMARY KEY (
                            EffectiveDateId,
                            RegulatoryProvisionId
                        ),
                        FOREIGN KEY (EffectiveDateId)
                            REFERENCES
                                VeteransClaims_EffectiveDates (
                                    Id
                                ),
                        FOREIGN KEY (RegulatoryProvisionId)
                            REFERENCES
                                VeteransClaims_RegulatoryProvisions (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EffectiveDateRegulatoryProvisions_Provision
                ON
                    VeteransClaims_EffectiveDateRegulatoryProvisions (
                        RegulatoryProvisionId
                    );
                """),
            new VeteransClaimsSqliteMigration(
                16,
                "AddExposureRegulatoryProvisions",
                """
                CREATE TABLE
                    VeteransClaims_ExposureRegulatoryProvisions (
                        ExposureId TEXT NOT NULL,
                        RegulatoryProvisionId TEXT NOT NULL,
                        PRIMARY KEY (
                            ExposureId,
                            RegulatoryProvisionId
                        ),
                        FOREIGN KEY (ExposureId)
                            REFERENCES
                                VeteransClaims_Exposures (
                                    Id
                                ),
                        FOREIGN KEY (RegulatoryProvisionId)
                            REFERENCES
                                VeteransClaims_RegulatoryProvisions (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_ExposureRegulatoryProvisions_Provision
                ON
                    VeteransClaims_ExposureRegulatoryProvisions (
                        RegulatoryProvisionId
                    );
                """),
            new VeteransClaimsSqliteMigration(
                17,
                "AddExposureRequirements",
                """
                CREATE TABLE
                    VeteransClaims_ExposureRequirements (
                        ExposureId TEXT NOT NULL,
                        RequirementId TEXT NOT NULL,
                        PRIMARY KEY (
                            ExposureId,
                            RequirementId
                        ),
                        FOREIGN KEY (ExposureId)
                            REFERENCES
                                VeteransClaims_Exposures (
                                    Id
                                ),
                        FOREIGN KEY (RequirementId)
                            REFERENCES
                                VeteransClaims_Requirements (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_ExposureRequirements_Requirement
                ON
                    VeteransClaims_ExposureRequirements (
                        RequirementId
                    );
                """),
            new VeteransClaimsSqliteMigration(
                18,
                "AddBasisPresumptions",
                """
                CREATE TABLE
                    VeteransClaims_BasisPresumptions (
                        ServiceConnectionBasisId TEXT NOT NULL,
                        PresumptionProvisionId TEXT NOT NULL,
                        PRIMARY KEY (
                            ServiceConnectionBasisId,
                            PresumptionProvisionId
                        ),
                        FOREIGN KEY (ServiceConnectionBasisId)
                            REFERENCES
                                VeteransClaims_ServiceConnectionBases (
                                    Id
                                ),
                        FOREIGN KEY (PresumptionProvisionId)
                            REFERENCES
                                VeteransClaims_RegulatoryProvisions (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_BasisPresumptions_Provision
                ON VeteransClaims_BasisPresumptions (
                    PresumptionProvisionId
                );
                """),
            new VeteransClaimsSqliteMigration(
                19,
                "AddMedicalOpinions",
                """
                CREATE TABLE
                    VeteransClaims_MedicalOpinions (
                        Id TEXT PRIMARY KEY,
                        ClaimIssueId TEXT NOT NULL,
                        Question TEXT NOT NULL,
                        Opinion TEXT NOT NULL,
                        FOREIGN KEY (ClaimIssueId)
                            REFERENCES
                                VeteransClaims_ClaimIssues (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_MedicalOpinions_ClaimIssue
                ON VeteransClaims_MedicalOpinions (
                    ClaimIssueId
                );
                """),
            new VeteransClaimsSqliteMigration(
                20,
                "AddBasisMedicalOpinions",
                """
                CREATE TABLE
                    VeteransClaims_BasisMedicalOpinions (
                        ServiceConnectionBasisId TEXT NOT NULL,
                        MedicalOpinionId TEXT NOT NULL,
                        Role TEXT NOT NULL,
                        PRIMARY KEY (
                            ServiceConnectionBasisId,
                            MedicalOpinionId,
                            Role
                        ),
                        FOREIGN KEY (ServiceConnectionBasisId)
                            REFERENCES
                                VeteransClaims_ServiceConnectionBases (
                                    Id
                                ),
                        FOREIGN KEY (MedicalOpinionId)
                            REFERENCES
                                VeteransClaims_MedicalOpinions (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_BasisMedicalOpinions_Opinion
                ON VeteransClaims_BasisMedicalOpinions (
                    MedicalOpinionId
                );
                """),
            new VeteransClaimsSqliteMigration(
                21,
                "AddClaimedConditionMedicalConditionMedicalOpinions",
                """
                CREATE TABLE
                    VeteransClaims_ClaimedConditionMedicalConditionMedicalOpinions (
                        ClaimedConditionId TEXT NOT NULL,
                        MedicalConditionId TEXT NOT NULL,
                        MedicalOpinionId TEXT NOT NULL,
                        Role TEXT NOT NULL,
                        PRIMARY KEY (
                            ClaimedConditionId,
                            MedicalConditionId,
                            MedicalOpinionId,
                            Role
                        ),
                        FOREIGN KEY (
                            ClaimedConditionId,
                            MedicalConditionId
                        )
                            REFERENCES
                                VeteransClaims_ClaimedConditionMedicalConditions (
                                    ClaimedConditionId,
                                    MedicalConditionId
                                ),
                        FOREIGN KEY (MedicalOpinionId)
                            REFERENCES VeteransClaims_MedicalOpinions (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_ClaimedConditionMedicalConditionMedicalOpinions_Opinion
                ON
                    VeteransClaims_ClaimedConditionMedicalConditionMedicalOpinions (
                        MedicalOpinionId
                    );
                """),
            new VeteransClaimsSqliteMigration(
                22,
                "AddVeteranMedicalConditionMedicalOpinions",
                """
                CREATE TABLE
                    VeteransClaims_VeteranMedicalConditionMedicalOpinions (
                        VeteranId TEXT NOT NULL,
                        MedicalConditionId TEXT NOT NULL,
                        MedicalOpinionId TEXT NOT NULL,
                        Role TEXT NOT NULL,
                        PRIMARY KEY (
                            VeteranId,
                            MedicalConditionId,
                            MedicalOpinionId,
                            Role
                        ),
                        FOREIGN KEY (
                            VeteranId,
                            MedicalConditionId
                        )
                            REFERENCES
                                VeteransClaims_VeteranMedicalConditions (
                                    VeteranId,
                                    MedicalConditionId
                                ),
                        FOREIGN KEY (MedicalOpinionId)
                            REFERENCES VeteransClaims_MedicalOpinions (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_VeteranMedicalConditionMedicalOpinions_Opinion
                ON
                    VeteransClaims_VeteranMedicalConditionMedicalOpinions (
                        MedicalOpinionId
                    );
                """),
            new VeteransClaimsSqliteMigration(
                23,
                "AddEvidenceClassifications",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceClassifications (
                        Id TEXT PRIMARY KEY,
                        ArtifactId TEXT NOT NULL,
                        ClaimIssueId TEXT NULL,
                        Classification TEXT NOT NULL,
                        FOREIGN KEY (ClaimIssueId)
                            REFERENCES VeteransClaims_ClaimIssues (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceClassifications_ClaimIssue
                ON VeteransClaims_EvidenceClassifications (
                    ClaimIssueId
                );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceClassifications_Artifact
                ON VeteransClaims_EvidenceClassifications (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                24,
                "AddEvidenceClassificationExposures",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceClassificationExposures (
                        EvidenceClassificationId TEXT NOT NULL,
                        ExposureId TEXT NOT NULL,
                        PRIMARY KEY (
                            EvidenceClassificationId,
                            ExposureId
                        ),
                        FOREIGN KEY (EvidenceClassificationId)
                            REFERENCES
                                VeteransClaims_EvidenceClassifications (
                                    Id
                                ),
                        FOREIGN KEY (ExposureId)
                            REFERENCES VeteransClaims_Exposures (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceClassificationExposures_Exposure
                ON VeteransClaims_EvidenceClassificationExposures (
                    ExposureId
                );
                """),
            new VeteransClaimsSqliteMigration(
                25,
                "AddEvidenceClassificationMedicalConditions",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceClassificationMedicalConditions (
                        EvidenceClassificationId TEXT NOT NULL,
                        MedicalConditionId TEXT NOT NULL,
                        PRIMARY KEY (
                            EvidenceClassificationId,
                            MedicalConditionId
                        ),
                        FOREIGN KEY (EvidenceClassificationId)
                            REFERENCES
                                VeteransClaims_EvidenceClassifications (
                                    Id
                                ),
                        FOREIGN KEY (MedicalConditionId)
                            REFERENCES VeteransClaims_MedicalConditions (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceClassificationMedicalConditions_Condition
                ON VeteransClaims_EvidenceClassificationMedicalConditions (
                    MedicalConditionId
                );
                """),
            new VeteransClaimsSqliteMigration(
                26,
                "AddEvidenceClassificationMedicalOpinions",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceClassificationMedicalOpinions (
                        EvidenceClassificationId TEXT NOT NULL,
                        MedicalOpinionId TEXT NOT NULL,
                        PRIMARY KEY (
                            EvidenceClassificationId,
                            MedicalOpinionId
                        ),
                        FOREIGN KEY (EvidenceClassificationId)
                            REFERENCES
                                VeteransClaims_EvidenceClassifications (
                                    Id
                                ),
                        FOREIGN KEY (MedicalOpinionId)
                            REFERENCES VeteransClaims_MedicalOpinions (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceClassificationMedicalOpinions_Opinion
                ON VeteransClaims_EvidenceClassificationMedicalOpinions (
                    MedicalOpinionId
                );
                """),
            new VeteransClaimsSqliteMigration(
                27,
                "AddEvidenceClassificationRequirements",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceClassificationRequirements (
                        EvidenceClassificationId TEXT NOT NULL,
                        RequirementId TEXT NOT NULL,
                        PRIMARY KEY (
                            EvidenceClassificationId,
                            RequirementId
                        ),
                        FOREIGN KEY (EvidenceClassificationId)
                            REFERENCES
                                VeteransClaims_EvidenceClassifications (
                                    Id
                                ),
                        FOREIGN KEY (RequirementId)
                            REFERENCES VeteransClaims_Requirements (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceClassificationRequirements_Requirement
                ON VeteransClaims_EvidenceClassificationRequirements (
                    RequirementId
                );
                """),
            new VeteransClaimsSqliteMigration(
                28,
                "AddEvidenceClassificationServiceEvents",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceClassificationServiceEvents (
                        EvidenceClassificationId TEXT NOT NULL,
                        ServiceEventId TEXT NOT NULL,
                        PRIMARY KEY (
                            EvidenceClassificationId,
                            ServiceEventId
                        ),
                        FOREIGN KEY (EvidenceClassificationId)
                            REFERENCES
                                VeteransClaims_EvidenceClassifications (
                                    Id
                                ),
                        FOREIGN KEY (ServiceEventId)
                            REFERENCES VeteransClaims_ServiceEvents (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceClassificationServiceEvents_ServiceEvent
                ON VeteransClaims_EvidenceClassificationServiceEvents (
                    ServiceEventId
                );
                """),
            new VeteransClaimsSqliteMigration(
                29,
                "AddFindings",
                """
                CREATE TABLE
                    VeteransClaims_Findings (
                        Id TEXT PRIMARY KEY,
                        ClaimIssueId TEXT NOT NULL,
                        RequirementId TEXT NULL,
                        Outcome TEXT NOT NULL,
                        Description TEXT NOT NULL,
                        FOREIGN KEY (ClaimIssueId)
                            REFERENCES VeteransClaims_ClaimIssues (
                                Id
                            ),
                        FOREIGN KEY (RequirementId)
                            REFERENCES VeteransClaims_Requirements (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_Findings_ClaimIssue
                ON VeteransClaims_Findings (
                    ClaimIssueId
                );

                CREATE INDEX
                    IX_VeteransClaims_Findings_Requirement
                ON VeteransClaims_Findings (
                    RequirementId
                );
                """),
            new VeteransClaimsSqliteMigration(
                30,
                "AddEvidenceClassificationFindings",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceClassificationFindings (
                        EvidenceClassificationId TEXT NOT NULL,
                        FindingId TEXT NOT NULL,
                        PRIMARY KEY (
                            EvidenceClassificationId,
                            FindingId
                        ),
                        FOREIGN KEY (EvidenceClassificationId)
                            REFERENCES VeteransClaims_EvidenceClassifications (
                                Id
                            ),
                        FOREIGN KEY (FindingId)
                            REFERENCES VeteransClaims_Findings (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceClassificationFindings_Finding
                ON VeteransClaims_EvidenceClassificationFindings (
                    FindingId
                );
                """),
            new VeteransClaimsSqliteMigration(
                31,
                "AddFindingRegulatoryProvisions",
                """
                CREATE TABLE
                    VeteransClaims_FindingRegulatoryProvisions (
                        FindingId TEXT NOT NULL,
                        RegulatoryProvisionId TEXT NOT NULL,
                        Role TEXT NOT NULL,
                        PRIMARY KEY (
                            FindingId,
                            RegulatoryProvisionId,
                            Role
                        ),
                        FOREIGN KEY (FindingId)
                            REFERENCES VeteransClaims_Findings (
                                Id
                            ),
                        FOREIGN KEY (RegulatoryProvisionId)
                            REFERENCES
                                VeteransClaims_RegulatoryProvisions (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_FindingRegulatoryProvisions_Provision
                ON VeteransClaims_FindingRegulatoryProvisions (
                    RegulatoryProvisionId
                );
                """),
            new VeteransClaimsSqliteMigration(
                32,
                "AddFindingArtifacts",
                """
                CREATE TABLE
                    VeteransClaims_FindingArtifacts (
                        FindingId TEXT NOT NULL,
                        ArtifactId TEXT NOT NULL,
                        Role TEXT NOT NULL,
                        PRIMARY KEY (
                            FindingId,
                            ArtifactId,
                            Role
                        ),
                        FOREIGN KEY (FindingId)
                            REFERENCES VeteransClaims_Findings (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_FindingArtifacts_Artifact
                ON VeteransClaims_FindingArtifacts (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                33,
                "AddEvidenceGaps",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceGaps (
                        Id TEXT PRIMARY KEY,
                        ClaimIssueId TEXT NOT NULL,
                        RequirementId TEXT NOT NULL,
                        Description TEXT NOT NULL,
                        FOREIGN KEY (ClaimIssueId)
                            REFERENCES VeteransClaims_ClaimIssues (
                                Id
                            ),
                        FOREIGN KEY (RequirementId)
                            REFERENCES VeteransClaims_Requirements (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceGaps_ClaimIssue
                ON VeteransClaims_EvidenceGaps (
                    ClaimIssueId
                );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceGaps_Requirement
                ON VeteransClaims_EvidenceGaps (
                    RequirementId
                );
                """),
            new VeteransClaimsSqliteMigration(
                34,
                "AddEvidenceDevelopmentPlans",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceDevelopmentPlans (
                        Id TEXT PRIMARY KEY,
                        ClaimIssueId TEXT NOT NULL,
                        Description TEXT NOT NULL,
                        FOREIGN KEY (ClaimIssueId)
                            REFERENCES VeteransClaims_ClaimIssues (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceDevelopmentPlans_ClaimIssue
                ON VeteransClaims_EvidenceDevelopmentPlans (
                    ClaimIssueId
                );
                """),
            new VeteransClaimsSqliteMigration(
                35,
                "AddEvidenceDevelopmentPlanEvidenceGaps",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceDevelopmentPlanEvidenceGaps (
                        EvidenceDevelopmentPlanId TEXT NOT NULL,
                        EvidenceGapId TEXT NOT NULL,
                        PRIMARY KEY (
                            EvidenceDevelopmentPlanId,
                            EvidenceGapId
                        ),
                        FOREIGN KEY (EvidenceDevelopmentPlanId)
                            REFERENCES
                                VeteransClaims_EvidenceDevelopmentPlans (
                                    Id
                                ),
                        FOREIGN KEY (EvidenceGapId)
                            REFERENCES VeteransClaims_EvidenceGaps (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceDevelopmentPlanEvidenceGaps_Gap
                ON VeteransClaims_EvidenceDevelopmentPlanEvidenceGaps (
                    EvidenceGapId
                );
                """),
            new VeteransClaimsSqliteMigration(
                36,
                "AddEvidenceDevelopmentPlanRequirements",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceDevelopmentPlanRequirements (
                        EvidenceDevelopmentPlanId TEXT NOT NULL,
                        RequirementId TEXT NOT NULL,
                        PRIMARY KEY (
                            EvidenceDevelopmentPlanId,
                            RequirementId
                        ),
                        FOREIGN KEY (EvidenceDevelopmentPlanId)
                            REFERENCES
                                VeteransClaims_EvidenceDevelopmentPlans (
                                    Id
                                ),
                        FOREIGN KEY (RequirementId)
                            REFERENCES VeteransClaims_Requirements (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceDevelopmentPlanRequirements_Requirement
                ON VeteransClaims_EvidenceDevelopmentPlanRequirements (
                    RequirementId
                );
                """),
            new VeteransClaimsSqliteMigration(
                37,
                "AddEvidenceDevelopmentPlanArtifacts",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceDevelopmentPlanArtifacts (
                        EvidenceDevelopmentPlanId TEXT NOT NULL,
                        ArtifactId TEXT NOT NULL,
                        Role TEXT NOT NULL,
                        PRIMARY KEY (
                            EvidenceDevelopmentPlanId,
                            ArtifactId,
                            Role
                        ),
                        FOREIGN KEY (EvidenceDevelopmentPlanId)
                            REFERENCES
                                VeteransClaims_EvidenceDevelopmentPlans (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceDevelopmentPlanArtifacts_Artifact
                ON VeteransClaims_EvidenceDevelopmentPlanArtifacts (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                38,
                "AddEvidencePackages",
                """
                CREATE TABLE
                    VeteransClaims_EvidencePackages (
                        Id TEXT PRIMARY KEY,
                        ClaimIssueId TEXT NOT NULL,
                        Purpose TEXT NOT NULL,
                        ReviewerRole TEXT NOT NULL,
                        FOREIGN KEY (ClaimIssueId)
                            REFERENCES VeteransClaims_ClaimIssues (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidencePackages_ClaimIssue
                ON VeteransClaims_EvidencePackages (
                    ClaimIssueId
                );
                """),
            new VeteransClaimsSqliteMigration(
                39,
                "AddEvidencePackageArtifacts",
                """
                CREATE TABLE
                    VeteransClaims_EvidencePackageArtifacts (
                        EvidencePackageId TEXT NOT NULL,
                        ArtifactId TEXT NOT NULL,
                        ContentRole TEXT NOT NULL,
                        PRIMARY KEY (
                            EvidencePackageId,
                            ArtifactId,
                            ContentRole
                        ),
                        FOREIGN KEY (EvidencePackageId)
                            REFERENCES VeteransClaims_EvidencePackages (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidencePackageArtifacts_Artifact
                ON VeteransClaims_EvidencePackageArtifacts (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                40,
                "AddRegulatoryProvisionProvenance",
                """
                ALTER TABLE
                    VeteransClaims_RegulatoryProvisions
                ADD COLUMN Version TEXT NULL;

                ALTER TABLE
                    VeteransClaims_RegulatoryProvisions
                ADD COLUMN EffectiveFrom TEXT NULL;

                ALTER TABLE
                    VeteransClaims_RegulatoryProvisions
                ADD COLUMN EffectiveTo TEXT NULL;

                ALTER TABLE
                    VeteransClaims_RegulatoryProvisions
                ADD COLUMN SourceUri TEXT NULL;

                ALTER TABLE
                    VeteransClaims_RegulatoryProvisions
                ADD COLUMN SourceHash TEXT NULL;

                ALTER TABLE
                    VeteransClaims_RegulatoryProvisions
                ADD COLUMN RetrievedUtc TEXT NULL;
                """),
            new VeteransClaimsSqliteMigration(
                41,
                "AddEvidenceRequirementGuidance",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceRequirementGuidance (
                        Id TEXT PRIMARY KEY,
                        RequirementId TEXT NOT NULL,
                        EvidenceClassification TEXT NOT NULL,
                        GuidanceRole TEXT NOT NULL,
                        Description TEXT NOT NULL,
                        FOREIGN KEY (RequirementId)
                            REFERENCES VeteransClaims_Requirements (
                                Id
                            )
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceRequirementGuidance_Requirement
                ON VeteransClaims_EvidenceRequirementGuidance (
                    RequirementId
                );
                """),
            new VeteransClaimsSqliteMigration(
                42,
                "AddEvidenceDevelopmentExecutions",
                """
                CREATE TABLE
                    VeteransClaims_EvidenceDevelopmentExecutions (
                        EvidenceDevelopmentPlanId TEXT NOT NULL,
                        EvidenceGapId TEXT NOT NULL,
                        WorkflowId TEXT NOT NULL,
                        PRIMARY KEY (
                            EvidenceDevelopmentPlanId,
                            EvidenceGapId
                        ),
                        FOREIGN KEY (EvidenceDevelopmentPlanId)
                            REFERENCES VeteransClaims_EvidenceDevelopmentPlans (Id),
                        FOREIGN KEY (EvidenceGapId)
                            REFERENCES VeteransClaims_EvidenceGaps (Id)
                    );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceDevelopmentExecutions_Workflow
                ON VeteransClaims_EvidenceDevelopmentExecutions (
                    WorkflowId
                );
                """),
            new VeteransClaimsSqliteMigration(
                43,
                "AddEvidenceDevelopmentResults",
                """
                CREATE TABLE VeteransClaims_EvidenceDevelopmentResults (
                    EvidenceGapId TEXT PRIMARY KEY,
                    RequirementId TEXT NOT NULL,
                    FOREIGN KEY (EvidenceGapId)
                        REFERENCES VeteransClaims_EvidenceGaps (Id),
                    FOREIGN KEY (RequirementId)
                        REFERENCES VeteransClaims_Requirements (Id)
                );

                CREATE TABLE VeteransClaims_EvidenceDevelopmentResultGuidance (
                    EvidenceGapId TEXT NOT NULL,
                    GuidanceId TEXT NOT NULL,
                    PRIMARY KEY (EvidenceGapId, GuidanceId),
                    FOREIGN KEY (EvidenceGapId)
                        REFERENCES VeteransClaims_EvidenceDevelopmentResults (EvidenceGapId),
                    FOREIGN KEY (GuidanceId)
                        REFERENCES VeteransClaims_EvidenceRequirementGuidance (Id)
                );
                """),
            new VeteransClaimsSqliteMigration(
                44,
                "AddEvidenceGapArtifacts",
                """
                CREATE TABLE VeteransClaims_EvidenceGapArtifacts (
                    EvidenceGapId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    PRIMARY KEY (
                        EvidenceGapId,
                        ArtifactId,
                        Role
                    ),
                    FOREIGN KEY (EvidenceGapId)
                        REFERENCES VeteransClaims_EvidenceGaps (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceGapArtifacts_Artifact
                ON VeteransClaims_EvidenceGapArtifacts (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                45,
                "AddEvidenceRecognitionTerms",
                """
                CREATE TABLE VeteransClaims_EvidenceRecognitionTerms (
                    Id TEXT PRIMARY KEY,
                    RequirementId TEXT NOT NULL,
                    Term TEXT NOT NULL,
                    TermType TEXT NOT NULL,
                    RecognitionRole TEXT NOT NULL,
                    AuthoritySource TEXT NOT NULL,
                    FOREIGN KEY (RequirementId)
                        REFERENCES VeteransClaims_Requirements (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_EvidenceRecognitionTerms_Requirement
                ON VeteransClaims_EvidenceRecognitionTerms (
                    RequirementId
                );
                """),
            new VeteransClaimsSqliteMigration(
                46,
                "AddEvidenceDevelopmentResultRecognitionMatches",
                """
                CREATE TABLE VeteransClaims_EvidenceDevelopmentResultRecognitionMatches (
                    EvidenceGapId TEXT NOT NULL,
                    RecognitionTermId TEXT NOT NULL,
                    PRIMARY KEY (
                        EvidenceGapId,
                        RecognitionTermId
                    ),
                    FOREIGN KEY (EvidenceGapId)
                        REFERENCES VeteransClaims_EvidenceDevelopmentResults (EvidenceGapId),
                    FOREIGN KEY (RecognitionTermId)
                        REFERENCES VeteransClaims_EvidenceRecognitionTerms (Id)
                );
                """),
            new VeteransClaimsSqliteMigration(
                47,
                "AddEvidenceRecognitionMatchArtifacts",
                """
                CREATE TABLE VeteransClaims_EvidenceDevelopmentResultRecognitionMatchArtifacts (
                    EvidenceGapId TEXT NOT NULL,
                    RecognitionTermId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    PRIMARY KEY (
                        EvidenceGapId,
                        RecognitionTermId,
                        ArtifactId,
                        Role
                    ),
                    FOREIGN KEY (
                        EvidenceGapId,
                        RecognitionTermId
                    )
                        REFERENCES VeteransClaims_EvidenceDevelopmentResultRecognitionMatches (
                            EvidenceGapId,
                            RecognitionTermId
                        ),
                    FOREIGN KEY (
                        EvidenceGapId,
                        ArtifactId,
                        Role
                    )
                        REFERENCES VeteransClaims_EvidenceGapArtifacts (
                            EvidenceGapId,
                            ArtifactId,
                            Role
                        )
                );
                """),
            new VeteransClaimsSqliteMigration(
                48,
                "EnforceEvidenceClassificationUniqueness",
                """
                CREATE UNIQUE INDEX
                    UX_VeteransClaims_EvidenceClassifications_Unique
                ON VeteransClaims_EvidenceClassifications (
                    ArtifactId,
                    IFNULL(ClaimIssueId, ''),
                    Classification
                );
                """),
            new VeteransClaimsSqliteMigration(
                49,
                "AddEvidenceRecognitionTermClassification",
                """
                ALTER TABLE VeteransClaims_EvidenceRecognitionTerms
                ADD COLUMN EvidenceClassification TEXT NULL;
                """)
,
            new VeteransClaimsSqliteMigration(
                50,
                "AddBasisRequirements",
                """
                CREATE TABLE VeteransClaims_BasisRequirements (
                    ServiceConnectionBasisId TEXT NOT NULL,
                    RequirementId TEXT NOT NULL,
                    PRIMARY KEY (
                        ServiceConnectionBasisId,
                        RequirementId
                    ),
                    FOREIGN KEY (ServiceConnectionBasisId)
                        REFERENCES VeteransClaims_ServiceConnectionBases (Id),
                    FOREIGN KEY (RequirementId)
                        REFERENCES VeteransClaims_Requirements (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_BasisRequirements_Requirement
                ON VeteransClaims_BasisRequirements (
                    RequirementId
                );
                """)
,
            new VeteransClaimsSqliteMigration(
                51,
                "AddEvidenceGapStatus",
                """
                ALTER TABLE VeteransClaims_EvidenceGaps
                ADD COLUMN Status TEXT NOT NULL DEFAULT 'Open';
                """),
            new VeteransClaimsSqliteMigration(
                52,
                "AddEvidenceDevelopmentResultAssessment",
                """
                ALTER TABLE VeteransClaims_EvidenceDevelopmentResults
                    ADD COLUMN MatchingGuidanceItemCount INTEGER NULL;

                ALTER TABLE VeteransClaims_EvidenceDevelopmentResults
                    ADD COLUMN MissingGuidanceItemCount INTEGER NULL;

                ALTER TABLE VeteransClaims_EvidenceDevelopmentResults
                    ADD COLUMN ResultingGapStatus TEXT NULL;
                """)
,
            new VeteransClaimsSqliteMigration(
                53,
                "AddVaDecisionArtifacts",
                """
                CREATE TABLE VeteransClaims_VaDecisionArtifacts (
                    VaDecisionId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    PRIMARY KEY (
                        VaDecisionId,
                        ArtifactId
                    ),
                    FOREIGN KEY (VaDecisionId)
                        REFERENCES VeteransClaims_VaDecisions (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_VaDecisionArtifacts_Artifact
                ON VeteransClaims_VaDecisionArtifacts (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                54,
                "AddVaDecisionDocumentProcessingAttempts",
                """
                CREATE TABLE VeteransClaims_VaDecisionDocumentProcessingAttempts (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ClaimId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    ProcessedAt TEXT NOT NULL,
                    VaDecisionId TEXT NULL,
                    FOREIGN KEY (ClaimId)
                        REFERENCES VeteransClaims_Claims (Id),
                    FOREIGN KEY (VaDecisionId)
                        REFERENCES VeteransClaims_VaDecisions (Id)
                );

                CREATE TABLE VeteransClaims_VaDecisionDocumentIssueMatches (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ProcessingAttemptId INTEGER NOT NULL,
                    MatchOrdinal INTEGER NOT NULL,
                    Status TEXT NOT NULL,
                    ClaimIssueId TEXT NULL,
                    IssueDescription TEXT NOT NULL,
                    Outcome TEXT NOT NULL,
                    Rationale TEXT NOT NULL,
                    UNIQUE (ProcessingAttemptId, MatchOrdinal),
                    FOREIGN KEY (ProcessingAttemptId)
                        REFERENCES VeteransClaims_VaDecisionDocumentProcessingAttempts (Id)
                        ON DELETE CASCADE
                );

                CREATE TABLE VeteransClaims_VaDecisionDocumentMatchValues (
                    IssueMatchId INTEGER NOT NULL,
                    ValueKind TEXT NOT NULL,
                    ValueOrdinal INTEGER NOT NULL,
                    Value TEXT NOT NULL,
                    PRIMARY KEY (IssueMatchId, ValueKind, ValueOrdinal),
                    FOREIGN KEY (IssueMatchId)
                        REFERENCES VeteransClaims_VaDecisionDocumentIssueMatches (Id)
                        ON DELETE CASCADE
                );

                CREATE TABLE VeteransClaims_VaDecisionDocumentSourceExcerpts (
                    IssueMatchId INTEGER NOT NULL,
                    ExcerptOrdinal INTEGER NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    Text TEXT NOT NULL,
                    StartOffset INTEGER NULL,
                    Length INTEGER NULL,
                    PRIMARY KEY (IssueMatchId, ExcerptOrdinal),
                    FOREIGN KEY (IssueMatchId)
                        REFERENCES VeteransClaims_VaDecisionDocumentIssueMatches (Id)
                        ON DELETE CASCADE
                );

                CREATE INDEX IX_VeteransClaims_VaDecisionDocumentProcessingAttempts_Claim
                ON VeteransClaims_VaDecisionDocumentProcessingAttempts (
                    ClaimId,
                    ProcessedAt
                );
                """),
            new VeteransClaimsSqliteMigration(
                55,
                "AddClaimIssueCourtAppeals",
                """
                CREATE TABLE VeteransClaims_ClaimIssueCourtAppeals (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ClaimIssueId TEXT NOT NULL,
                    Court TEXT NOT NULL,
                    FiledAt TEXT NOT NULL,
                    DocketNumber TEXT NULL,
                    Outcome TEXT NULL,
                    DecidedAt TEXT NULL
                );

                CREATE INDEX
                    IX_VeteransClaims_ClaimIssueCourtAppeals_Issue
                ON VeteransClaims_ClaimIssueCourtAppeals (
                    ClaimIssueId,
                    FiledAt
                );
                """),
            new VeteransClaimsSqliteMigration(
                56,
                "AddSubmissionDates",
                """
                ALTER TABLE VeteransClaims_Submissions
                ADD COLUMN SubmittedAt TEXT NULL;

                ALTER TABLE VeteransClaims_Submissions
                ADD COLUMN ReceivedAt TEXT NULL;
                """),
            new VeteransClaimsSqliteMigration(
                57,
                "EnforceUniqueVaDecisionArtifacts",
                """
                CREATE UNIQUE INDEX
                    UX_VeteransClaims_VaDecisionArtifacts_Artifact
                ON VeteransClaims_VaDecisionArtifacts (
                    ArtifactId
                );
                """)
,
            new VeteransClaimsSqliteMigration(
                58,
                "AddBasisPrescribedMedications",
                """
                CREATE TABLE
                    VeteransClaims_BasisPrescribedMedications (
                        ServiceConnectionBasisId TEXT NOT NULL,
                        MedicationName TEXT NOT NULL,
                        PRIMARY KEY (
                            ServiceConnectionBasisId,
                            MedicationName
                        ),
                        FOREIGN KEY (ServiceConnectionBasisId)
                            REFERENCES
                                VeteransClaims_ServiceConnectionBases (
                                    Id
                                )
                    );

                CREATE INDEX
                    IX_VeteransClaims_BasisPrescribedMedications_Medication
                ON VeteransClaims_BasisPrescribedMedications (
                    MedicationName
                );
                """)
            ,
            new VeteransClaimsSqliteMigration(
                59,
                "AddBasisArtifacts",
                """
                CREATE TABLE VeteransClaims_BasisArtifacts (
                    ServiceConnectionBasisId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    PRIMARY KEY (
                        ServiceConnectionBasisId,
                        ArtifactId,
                        Role
                    ),
                    FOREIGN KEY (ServiceConnectionBasisId)
                        REFERENCES VeteransClaims_ServiceConnectionBases (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_BasisArtifacts_Artifact
                ON VeteransClaims_BasisArtifacts (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                60,
                "AddExposureArtifacts",
                """
                CREATE TABLE VeteransClaims_ExposureArtifacts (
                    ExposureId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    Role TEXT NOT NULL,
                    PRIMARY KEY (
                        ExposureId,
                        ArtifactId,
                        Role
                    ),
                    FOREIGN KEY (ExposureId)
                        REFERENCES VeteransClaims_Exposures (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_ExposureArtifacts_Artifact
                ON VeteransClaims_ExposureArtifacts (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                61,
                "AddClaimIssueExposures",
                """
                CREATE TABLE VeteransClaims_ClaimIssueExposures (
                    ClaimIssueId TEXT NOT NULL,
                    ExposureId TEXT NOT NULL,
                    PRIMARY KEY (
                        ClaimIssueId,
                        ExposureId
                    ),
                    FOREIGN KEY (ClaimIssueId)
                        REFERENCES VeteransClaims_ClaimIssues (Id),
                    FOREIGN KEY (ExposureId)
                        REFERENCES VeteransClaims_Exposures (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_ClaimIssueExposures_Exposure
                ON VeteransClaims_ClaimIssueExposures (
                    ExposureId
                );
                """),
            new VeteransClaimsSqliteMigration(
                62,
                "AddIssueDecisionFindings",
                """
                CREATE TABLE VeteransClaims_IssueDecisionFindings (
                    IssueDecisionId TEXT NOT NULL,
                    FindingId TEXT NOT NULL,
                    PRIMARY KEY (
                        IssueDecisionId,
                        FindingId
                    ),
                    FOREIGN KEY (IssueDecisionId)
                        REFERENCES VeteransClaims_IssueDecisions (Id),
                    FOREIGN KEY (FindingId)
                        REFERENCES VeteransClaims_Findings (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_IssueDecisionFindings_Finding
                ON VeteransClaims_IssueDecisionFindings (
                    FindingId
                );
                """),
            new VeteransClaimsSqliteMigration(
                63,
                "AddIssueDecisionArtifacts",
                """
                CREATE TABLE VeteransClaims_IssueDecisionArtifacts (
                    IssueDecisionId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    PRIMARY KEY (
                        IssueDecisionId,
                        ArtifactId
                    ),
                    FOREIGN KEY (IssueDecisionId)
                        REFERENCES VeteransClaims_IssueDecisions (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_IssueDecisionArtifacts_Artifact
                ON VeteransClaims_IssueDecisionArtifacts (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                64,
                "AddDisabilityEvaluationArtifacts",
                """
                CREATE TABLE VeteransClaims_DisabilityEvaluationArtifacts (
                    DisabilityEvaluationId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    PRIMARY KEY (
                        DisabilityEvaluationId,
                        ArtifactId
                    ),
                    FOREIGN KEY (DisabilityEvaluationId)
                        REFERENCES VeteransClaims_DisabilityEvaluations (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_DisabilityEvaluationArtifacts_Artifact
                ON VeteransClaims_DisabilityEvaluationArtifacts (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                65,
                "AddEffectiveDateArtifacts",
                """
                CREATE TABLE VeteransClaims_EffectiveDateArtifacts (
                    EffectiveDateId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    PRIMARY KEY (
                        EffectiveDateId,
                        ArtifactId
                    ),
                    FOREIGN KEY (EffectiveDateId)
                        REFERENCES VeteransClaims_EffectiveDates (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_EffectiveDateArtifacts_Artifact
                ON VeteransClaims_EffectiveDateArtifacts (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                66,
                "AddMedicalOpinionArtifacts",
                """
                CREATE TABLE VeteransClaims_MedicalOpinionArtifacts (
                    MedicalOpinionId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    PRIMARY KEY (
                        MedicalOpinionId,
                        ArtifactId
                    ),
                    FOREIGN KEY (MedicalOpinionId)
                        REFERENCES VeteransClaims_MedicalOpinions (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicalOpinionArtifacts_Artifact
                ON VeteransClaims_MedicalOpinionArtifacts (
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                67,
                "AddMedicalLiteratureSources",
                """
                CREATE TABLE VeteransClaims_MedicalLiteratureSources (
                    Id TEXT PRIMARY KEY,
                    Title TEXT NOT NULL,
                    Authors TEXT NOT NULL,
                    Publication TEXT NOT NULL,
                    PublicationYear INTEGER NULL,
                    VaAffiliated INTEGER NOT NULL,
                    VaFunded INTEGER NOT NULL,
                    PeerReviewed INTEGER NOT NULL,
                    FundingSource TEXT NULL,
                    ResearchOrganization TEXT NULL,
                    Doi TEXT NULL,
                    Pmid TEXT NULL,
                    SourceUri TEXT NULL,
                    SourceHash TEXT NULL,
                    RetrievedUtc TEXT NULL
                );

                CREATE TABLE VeteransClaims_RequirementMedicalLiterature (
                    RequirementId TEXT NOT NULL,
                    MedicalLiteratureSourceId TEXT NOT NULL,
                    GuidanceRole TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    PRIMARY KEY (
                        RequirementId,
                        MedicalLiteratureSourceId,
                        GuidanceRole
                    ),
                    FOREIGN KEY (RequirementId)
                        REFERENCES VeteransClaims_Requirements (Id),
                    FOREIGN KEY (MedicalLiteratureSourceId)
                        REFERENCES VeteransClaims_MedicalLiteratureSources (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_RequirementMedicalLiterature_Source
                ON VeteransClaims_RequirementMedicalLiterature (
                    MedicalLiteratureSourceId
                );
                """),
            new VeteransClaimsSqliteMigration(
                68,
                "AddMedicalLiteratureSourceArtifacts",
                """
                CREATE TABLE VeteransClaims_MedicalLiteratureSourceArtifacts (
                    MedicalLiteratureSourceId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    PRIMARY KEY (
                        MedicalLiteratureSourceId,
                        ArtifactId
                    ),
                    FOREIGN KEY (MedicalLiteratureSourceId)
                        REFERENCES VeteransClaims_MedicalLiteratureSources (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicalLiteratureSourceArtifacts_Artifact
                ON VeteransClaims_MedicalLiteratureSourceArtifacts (ArtifactId);
                """),
            new VeteransClaimsSqliteMigration(
                69,
                "AddReviewedMedicalLiteratureClassifications",
                """
                CREATE TABLE
                    VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    RequirementId TEXT NOT NULL,
                    MedicalLiteratureSourceId TEXT NOT NULL,
                    GuidanceRole TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    PromotedBy TEXT NOT NULL,
                    PromotedUtc TEXT NOT NULL,
                    ReviewedBy TEXT NOT NULL,
                    ReviewedUtc TEXT NOT NULL,
                    IntelligenceOutput TEXT NOT NULL,
                    CapabilityId TEXT NOT NULL,
                    ProviderId TEXT NOT NULL,
                    CorrelationId TEXT NOT NULL,
                    EngineName TEXT NOT NULL,
                    EngineVersion TEXT NULL,
                    ProviderOperationId TEXT NULL,
                    StartedUtc TEXT NOT NULL,
                    CompletedUtc TEXT NOT NULL,
                    RequiresReview INTEGER NOT NULL,
                    WarningsJson TEXT NOT NULL,
                    PRIMARY KEY (
                        RequirementId,
                        MedicalLiteratureSourceId,
                        GuidanceRole,
                        ArtifactId,
                        CorrelationId
                    ),
                    FOREIGN KEY (
                        RequirementId,
                        MedicalLiteratureSourceId,
                        GuidanceRole
                    )
                        REFERENCES VeteransClaims_RequirementMedicalLiterature (
                            RequirementId,
                            MedicalLiteratureSourceId,
                            GuidanceRole
                        ),
                    FOREIGN KEY (
                        MedicalLiteratureSourceId,
                        ArtifactId
                    )
                        REFERENCES VeteransClaims_MedicalLiteratureSourceArtifacts (
                            MedicalLiteratureSourceId,
                            ArtifactId
                        )
                );

                CREATE TABLE
                    VeteransClaims_ReviewedMedicalLiteratureExcerpts (
                    RequirementId TEXT NOT NULL,
                    MedicalLiteratureSourceId TEXT NOT NULL,
                    GuidanceRole TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    CorrelationId TEXT NOT NULL,
                    ExcerptOrdinal INTEGER NOT NULL,
                    Text TEXT NOT NULL,
                    StartOffset INTEGER NULL,
                    Length INTEGER NULL,
                    PRIMARY KEY (
                        RequirementId,
                        MedicalLiteratureSourceId,
                        GuidanceRole,
                        ArtifactId,
                        CorrelationId,
                        ExcerptOrdinal
                    ),
                    FOREIGN KEY (
                        RequirementId,
                        MedicalLiteratureSourceId,
                        GuidanceRole,
                        ArtifactId,
                        CorrelationId
                    )
                        REFERENCES
                            VeteransClaims_ReviewedMedicalLiteratureClassifications (
                            RequirementId,
                            MedicalLiteratureSourceId,
                            GuidanceRole,
                            ArtifactId,
                            CorrelationId
                        )
                );

                CREATE INDEX
                    IX_VeteransClaims_ReviewedMedicalLiterature_Artifact
                ON VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    ArtifactId
                );

                CREATE INDEX
                    IX_VeteransClaims_ReviewedMedicalLiterature_Correlation
                ON VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    CorrelationId
                );
                """),
            new VeteransClaimsSqliteMigration(
                70,
                "EnforceReviewedMedicalLiteratureLogicalUniqueness",
                """
                CREATE UNIQUE INDEX
                    UX_VeteransClaims_ReviewedMedicalLiterature_LogicalDecision
                ON VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    RequirementId,
                    MedicalLiteratureSourceId,
                    GuidanceRole,
                    ArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                71,
                "AddReviewedMedicalLiteratureSupersession",
                """
                ALTER TABLE
                    VeteransClaims_ReviewedMedicalLiteratureClassifications
                ADD COLUMN SupersededByCorrelationId TEXT NULL;

                ALTER TABLE
                    VeteransClaims_ReviewedMedicalLiteratureClassifications
                ADD COLUMN SupersededUtc TEXT NULL;

                DROP INDEX
                    UX_VeteransClaims_ReviewedMedicalLiterature_LogicalDecision;

                CREATE UNIQUE INDEX
                    UX_VeteransClaims_ReviewedMedicalLiterature_LogicalDecision
                ON VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    RequirementId,
                    MedicalLiteratureSourceId,
                    GuidanceRole,
                    ArtifactId
                )
                WHERE SupersededUtc IS NULL;

                CREATE INDEX
                    IX_VeteransClaims_ReviewedMedicalLiterature_SupersededBy
                ON VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    SupersededByCorrelationId
                );
                """),
            new VeteransClaimsSqliteMigration(
                72,
                "AddReviewerPageSelection",
                """
                ALTER TABLE VeteransClaims_EvidencePackageArtifacts
                ADD COLUMN ReviewerPageSelection TEXT NULL;
                """),
            new VeteransClaimsSqliteMigration(
                73,
                "AddMedicationRecords",
                """
                CREATE TABLE VeteransClaims_MedicationRecords (
                    Id TEXT PRIMARY KEY,
                    VeteranId TEXT NOT NULL,
                    SourceArtifactId TEXT NOT NULL,
                    RecordDate TEXT NOT NULL,
                    SourcePage INTEGER NOT NULL CHECK (SourcePage > 0),
                    MedicationName TEXT NOT NULL,
                    Strength TEXT NULL,
                    Directions TEXT NULL,
                    Indication TEXT NULL,
                    Status TEXT NOT NULL,
                    SourceDesignation TEXT NULL,
                    FOREIGN KEY (VeteranId)
                        REFERENCES VeteransClaims_Veterans (Id)
                );

                CREATE INDEX IX_VeteransClaims_MedicationRecords_Veteran
                ON VeteransClaims_MedicationRecords (
                    VeteranId, RecordDate DESC
                );

                CREATE INDEX IX_VeteransClaims_MedicationRecords_Medication
                ON VeteransClaims_MedicationRecords (
                    VeteranId, MedicationName, RecordDate DESC
                );

                CREATE INDEX IX_VeteransClaims_MedicationRecords_Artifact
                ON VeteransClaims_MedicationRecords (SourceArtifactId);
                """),
            new VeteransClaimsSqliteMigration(
                74,
                "AddEvidencePackageBasisScope",
                """
                ALTER TABLE VeteransClaims_EvidencePackages
                ADD COLUMN ServiceConnectionBasisId TEXT NULL
                    REFERENCES VeteransClaims_ServiceConnectionBases (Id);

                CREATE INDEX
                    IX_VeteransClaims_EvidencePackages_Basis
                ON VeteransClaims_EvidencePackages (
                    ServiceConnectionBasisId
                );
                """),
            new VeteransClaimsSqliteMigration(
                75,
                "AddMedicationHistoryEvents",
                """
                CREATE TABLE VeteransClaims_MedicationHistoryEvents (
                    Id TEXT PRIMARY KEY,
                    VeteranId TEXT NOT NULL,
                    SourceArtifactId TEXT NOT NULL,
                    EventDate TEXT NOT NULL,
                    SourcePage INTEGER NOT NULL CHECK (SourcePage > 0),
                    MedicationName TEXT NOT NULL,
                    EventType TEXT NOT NULL,
                    Strength TEXT NULL,
                    Directions TEXT NULL,
                    PharmacyIndication TEXT NULL,
                    PrescriptionNumber TEXT NULL,
                    FOREIGN KEY (VeteranId)
                        REFERENCES VeteransClaims_Veterans (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicationHistoryEvents_Veteran
                ON VeteransClaims_MedicationHistoryEvents (
                    VeteranId, EventDate
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicationHistoryEvents_Medication
                ON VeteransClaims_MedicationHistoryEvents (
                    VeteranId, MedicationName, EventDate
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicationHistoryEvents_Artifact
                ON VeteransClaims_MedicationHistoryEvents (
                    SourceArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                76,
                "AddReviewedBoundedEvidenceInterpretations",
                """
                CREATE TABLE
                    VeteransClaims_ReviewedBoundedEvidenceInterpretations (
                    ClaimIssueId TEXT NOT NULL,
                    ServiceConnectionBasisId TEXT NOT NULL,
                    RequirementId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    Direction TEXT NOT NULL,
                    OpinionStandard TEXT NOT NULL,
                    MedicalConclusion TEXT NOT NULL,
                    RationaleSummary TEXT NOT NULL,
                    PromotedBy TEXT NOT NULL,
                    PromotedUtc TEXT NOT NULL,
                    ReviewedBy TEXT NOT NULL,
                    ReviewedUtc TEXT NOT NULL,
                    CapabilityId TEXT NOT NULL,
                    ProviderId TEXT NOT NULL,
                    CorrelationId TEXT NOT NULL,
                    EngineName TEXT NOT NULL,
                    EngineVersion TEXT NULL,
                    ProviderOperationId TEXT NULL,
                    InputTokenCount INTEGER NULL,
                    OutputTokenCount INTEGER NULL,
                    TotalTokenCount INTEGER NULL,
                    EstimatedCostUsd TEXT NULL,
                    StartedUtc TEXT NOT NULL,
                    CompletedUtc TEXT NOT NULL,
                    RequiresReview INTEGER NOT NULL,
                    WarningsJson TEXT NOT NULL,
                    SupersededByCorrelationId TEXT NULL,
                    SupersededUtc TEXT NULL,
                    PRIMARY KEY (
                        ClaimIssueId,
                        ServiceConnectionBasisId,
                        RequirementId,
                        ArtifactId,
                        CorrelationId
                    )
                );

                CREATE TABLE
                    VeteransClaims_ReviewedBoundedEvidenceExcerpts (
                    ClaimIssueId TEXT NOT NULL,
                    ServiceConnectionBasisId TEXT NOT NULL,
                    RequirementId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    CorrelationId TEXT NOT NULL,
                    ExcerptOrdinal INTEGER NOT NULL,
                    Text TEXT NOT NULL,
                    StartOffset INTEGER NOT NULL CHECK (StartOffset >= 0),
                    Length INTEGER NOT NULL CHECK (Length > 0),
                    PRIMARY KEY (
                        ClaimIssueId,
                        ServiceConnectionBasisId,
                        RequirementId,
                        ArtifactId,
                        CorrelationId,
                        ExcerptOrdinal
                    ),
                    FOREIGN KEY (
                        ClaimIssueId,
                        ServiceConnectionBasisId,
                        RequirementId,
                        ArtifactId,
                        CorrelationId
                    )
                        REFERENCES
                            VeteransClaims_ReviewedBoundedEvidenceInterpretations (
                            ClaimIssueId,
                            ServiceConnectionBasisId,
                            RequirementId,
                            ArtifactId,
                            CorrelationId
                        )
                );

                CREATE UNIQUE INDEX
                    UX_VeteransClaims_ReviewedBoundedEvidence_LogicalDecision
                ON VeteransClaims_ReviewedBoundedEvidenceInterpretations (
                    ClaimIssueId,
                    ServiceConnectionBasisId,
                    RequirementId,
                    ArtifactId
                )
                WHERE SupersededUtc IS NULL;

                CREATE INDEX
                    IX_VeteransClaims_ReviewedBoundedEvidence_Requirement
                ON VeteransClaims_ReviewedBoundedEvidenceInterpretations (
                    RequirementId
                );

                CREATE INDEX
                    IX_VeteransClaims_ReviewedBoundedEvidence_Artifact
                ON VeteransClaims_ReviewedBoundedEvidenceInterpretations (
                    ArtifactId
                );

                CREATE INDEX
                    IX_VeteransClaims_ReviewedBoundedEvidence_Correlation
                ON VeteransClaims_ReviewedBoundedEvidenceInterpretations (
                    CorrelationId
                );

                CREATE INDEX
                    IX_VeteransClaims_ReviewedBoundedEvidence_SupersededBy
                ON VeteransClaims_ReviewedBoundedEvidenceInterpretations (
                    SupersededByCorrelationId
                );
                """),
            new VeteransClaimsSqliteMigration(
                77,
                "AddServiceConnectionBasisReviewerLabel",
                """
                ALTER TABLE VeteransClaims_ServiceConnectionBases
                ADD COLUMN ReviewerLabel TEXT NULL;
                """),
            new VeteransClaimsSqliteMigration(
                78,
                "AddMedicationLedgers",
                """
                CREATE TABLE VeteransClaims_MedicationLedgers (
                    Id TEXT PRIMARY KEY,
                    VeteranId TEXT NOT NULL,
                    SourceArtifactId TEXT NOT NULL,
                    ReportDate TEXT NOT NULL,
                    SourceStartPage INTEGER NOT NULL
                        CHECK (SourceStartPage > 0),
                    SourceEndPage INTEGER NOT NULL
                        CHECK (SourceEndPage >= SourceStartPage),
                    ReportedEntryCount INTEGER NULL
                        CHECK (
                            ReportedEntryCount IS NULL OR
                            ReportedEntryCount >= 0
                        ),
                    ParsedEntryCount INTEGER NOT NULL
                        CHECK (ParsedEntryCount >= 0),
                    IsComplete INTEGER NOT NULL
                        CHECK (IsComplete IN (0, 1)),
                    FOREIGN KEY (VeteranId)
                        REFERENCES VeteransClaims_Veterans (Id)
                );

                CREATE UNIQUE INDEX
                    UX_VeteransClaims_MedicationLedgers_Source
                ON VeteransClaims_MedicationLedgers (
                    SourceArtifactId,
                    SourceStartPage,
                    SourceEndPage
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicationLedgers_Veteran
                ON VeteransClaims_MedicationLedgers (
                    VeteranId,
                    ReportDate DESC
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicationLedgers_Artifact
                ON VeteransClaims_MedicationLedgers (
                    SourceArtifactId
                );

                CREATE TABLE VeteransClaims_MedicationLedgerEntries (
                    Id TEXT PRIMARY KEY,
                    MedicationLedgerId TEXT NOT NULL,
                    EntryOrdinal INTEGER NOT NULL
                        CHECK (EntryOrdinal > 0),
                    SourceStartPage INTEGER NOT NULL
                        CHECK (SourceStartPage > 0),
                    SourceEndPage INTEGER NOT NULL
                        CHECK (SourceEndPage >= SourceStartPage),
                    MedicationName TEXT NOT NULL,
                    Strength TEXT NULL,
                    Status TEXT NOT NULL,
                    PrescriptionNumber TEXT NULL,
                    PrescribedDate TEXT NULL,
                    LastFilledDate TEXT NULL,
                    LastFilledOnText TEXT NULL,
                    ExpirationDate TEXT NULL,
                    RefillsLeft INTEGER NULL
                        CHECK (
                            RefillsLeft IS NULL OR
                            RefillsLeft >= 0
                        ),
                    Directions TEXT NULL,
                    Indication TEXT NULL,
                    Prescriber TEXT NULL,
                    Facility TEXT NULL,
                    Quantity TEXT NULL,
                    FOREIGN KEY (MedicationLedgerId)
                        REFERENCES VeteransClaims_MedicationLedgers (Id)
                );

                CREATE UNIQUE INDEX
                    UX_VeteransClaims_MedicationLedgerEntries_Ordinal
                ON VeteransClaims_MedicationLedgerEntries (
                    MedicationLedgerId,
                    EntryOrdinal
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicationLedgerEntries_Ledger
                ON VeteransClaims_MedicationLedgerEntries (
                    MedicationLedgerId
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicationLedgerEntries_Prescription
                ON VeteransClaims_MedicationLedgerEntries (
                    PrescriptionNumber
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicationLedgerEntries_Status
                ON VeteransClaims_MedicationLedgerEntries (
                    MedicationLedgerId,
                    Status
                );
                """),
            new VeteransClaimsSqliteMigration(
                79,
                "AddMedicationClinicalContexts",
                """
                CREATE TABLE VeteransClaims_MedicationClinicalContexts (
                    Id TEXT PRIMARY KEY,
                    VeteranId TEXT NOT NULL,
                    SourceArtifactId TEXT NOT NULL,
                    EventDate TEXT NOT NULL,
                    SourceStartPage INTEGER NOT NULL
                        CHECK (SourceStartPage > 0),
                    SourceEndPage INTEGER NOT NULL
                        CHECK (SourceEndPage >= SourceStartPage),
                    MedicationName TEXT NOT NULL,
                    PrescriptionNumber TEXT NOT NULL,
                    ContextType TEXT NOT NULL,
                    Summary TEXT NOT NULL,
                    FOREIGN KEY (VeteranId)
                        REFERENCES VeteransClaims_Veterans (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicationClinicalContexts_VeteranRx
                ON VeteransClaims_MedicationClinicalContexts (
                    VeteranId,
                    PrescriptionNumber,
                    EventDate
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicationClinicalContexts_Artifact
                ON VeteransClaims_MedicationClinicalContexts (
                    SourceArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                80,
                "AddMedicationClinicalContextRecordTitles",
                """
                ALTER TABLE VeteransClaims_MedicationClinicalContexts
                ADD COLUMN RecordTitle TEXT NULL;
                """),
            new VeteransClaimsSqliteMigration(
                81,
                "AddSourceClarifications",
                """
                CREATE TABLE VeteransClaims_SourceClarifications (
                    Id TEXT PRIMARY KEY,
                    ClaimIssueId TEXT NOT NULL,
                    SourceArtifactId TEXT NOT NULL,
                    EvidenceDate TEXT NOT NULL,
                    SourceStartPage INTEGER NOT NULL
                        CHECK (SourceStartPage > 0),
                    SourceEndPage INTEGER NOT NULL
                        CHECK (SourceEndPage >= SourceStartPage),
                    RecordTitle TEXT NOT NULL,
                    Category TEXT NOT NULL
                        CHECK (Category IN (
                            'ImpossibleMagnitude',
                            'BlankTemplate',
                            'MalformedValueOrUnit',
                            'InternalConflict'
                        )),
                    OriginalText TEXT NOT NULL,
                    Clarification TEXT NOT NULL,
                    FOREIGN KEY (ClaimIssueId)
                        REFERENCES VeteransClaims_ClaimIssues (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_SourceClarifications_ClaimIssueDate
                ON VeteransClaims_SourceClarifications (
                    ClaimIssueId,
                    EvidenceDate,
                    SourceStartPage
                );

                CREATE INDEX
                    IX_VeteransClaims_SourceClarifications_Artifact
                ON VeteransClaims_SourceClarifications (
                    SourceArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                82,
                "AddClinicalProgressionEvents",
                """
                CREATE TABLE VeteransClaims_ClinicalProgressionEvents (
                    Id TEXT PRIMARY KEY,
                    ClaimIssueId TEXT NOT NULL,
                    SourceArtifactId TEXT NOT NULL,
                    EventDate TEXT NOT NULL,
                    SourceStartPage INTEGER NULL
                        CHECK (SourceStartPage IS NULL OR SourceStartPage > 0),
                    SourceEndPage INTEGER NULL,
                    RecordTitle TEXT NOT NULL,
                    EventType TEXT NOT NULL
                        CHECK (EventType IN (
                            'TreatmentUse',
                            'TreatmentProblem',
                            'TreatmentAdjustment',
                            'DiagnosticFinding',
                            'TreatmentTransition',
                            'TreatmentResponse'
                        )),
                    Summary TEXT NOT NULL,
                    CHECK (
                        (SourceStartPage IS NULL AND SourceEndPage IS NULL) OR
                        (SourceStartPage IS NOT NULL AND
                         SourceEndPage IS NOT NULL AND
                         SourceEndPage >= SourceStartPage)
                    ),
                    FOREIGN KEY (ClaimIssueId)
                        REFERENCES VeteransClaims_ClaimIssues (Id)
                );

                CREATE INDEX
                    IX_VeteransClaims_ClinicalProgressionEvents_ClaimIssueDate
                ON VeteransClaims_ClinicalProgressionEvents (
                    ClaimIssueId,
                    EventDate,
                    SourceStartPage
                );

                CREATE INDEX
                    IX_VeteransClaims_ClinicalProgressionEvents_Artifact
                ON VeteransClaims_ClinicalProgressionEvents (
                    SourceArtifactId
                );
                """),
            new VeteransClaimsSqliteMigration(
                83,
                "AddMedicationCurrentUseReconciliations",
                """
                CREATE TABLE VeteransClaims_MedicationCurrentUseReconciliations (
                    Id TEXT PRIMARY KEY,
                    VeteranId TEXT NOT NULL,
                    MedicationLedgerEntryId TEXT NOT NULL,
                    ReconciliationDate TEXT NOT NULL,
                    CurrentUseStatus TEXT NOT NULL
                        CHECK (CurrentUseStatus IN (
                            'CurrentlyUsed',
                            'NotCurrentlyUsed'
                        )),
                    Source TEXT NOT NULL,
                    Note TEXT NULL,
                    FOREIGN KEY (VeteranId)
                        REFERENCES VeteransClaims_Veterans (Id),
                    FOREIGN KEY (MedicationLedgerEntryId)
                        REFERENCES VeteransClaims_MedicationLedgerEntries (Id),
                    UNIQUE (MedicationLedgerEntryId, ReconciliationDate)
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicationCurrentUseReconciliations_VeteranEntryDate
                ON VeteransClaims_MedicationCurrentUseReconciliations (
                    VeteranId,
                    MedicationLedgerEntryId,
                    ReconciliationDate
                );
                """),
            new VeteransClaimsSqliteMigration(
                84,
                "AddSourceClarificationReviewerCorrections",
                """
                ALTER TABLE VeteransClaims_SourceClarifications
                ADD COLUMN ReviewerMatchText TEXT NULL;

                ALTER TABLE VeteransClaims_SourceClarifications
                ADD COLUMN ReviewerReplacementText TEXT NULL;
                """),
            new VeteransClaimsSqliteMigration(
                85,
                "AddEvidencePackageCreationOrdinal",
                """
                ALTER TABLE VeteransClaims_EvidencePackages
                ADD COLUMN CreationOrdinal INTEGER NOT NULL DEFAULT 0;

                UPDATE VeteransClaims_EvidencePackages
                SET CreationOrdinal = rowid;

                CREATE UNIQUE INDEX
                    UX_VeteransClaims_EvidencePackages_CreationOrdinal
                ON VeteransClaims_EvidencePackages (CreationOrdinal);
                """),
            new VeteransClaimsSqliteMigration(
                86,
                "AddMedicationClinicalContextSupersession",
                """
                ALTER TABLE VeteransClaims_MedicationClinicalContexts
                ADD COLUMN SupersededByMedicationClinicalContextId TEXT NULL;

                ALTER TABLE VeteransClaims_MedicationClinicalContexts
                ADD COLUMN SupersededUtc TEXT NULL;

                ALTER TABLE VeteransClaims_MedicationClinicalContexts
                ADD COLUMN SupersessionReason TEXT NULL;

                CREATE INDEX
                    IX_VeteransClaims_MedicationClinicalContexts_Active
                ON VeteransClaims_MedicationClinicalContexts (
                    VeteranId,
                    SupersededUtc
                );
                """),
            new VeteransClaimsSqliteMigration(
                87,
                "ScopeMedicalLiteratureToServiceConnectionBasis",
                """
                CREATE TABLE VeteransClaims_M87LiteratureMigrationGuard (
                    OrphanCount INTEGER NOT NULL
                        CHECK (OrphanCount = 0)
                );

                INSERT INTO VeteransClaims_M87LiteratureMigrationGuard (
                    OrphanCount
                )
                SELECT COUNT(*)
                FROM VeteransClaims_RequirementMedicalLiterature AS literature
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM VeteransClaims_BasisRequirements AS basisRequirement
                    WHERE basisRequirement.RequirementId =
                          literature.RequirementId
                );

                INSERT INTO VeteransClaims_M87LiteratureMigrationGuard (OrphanCount)
                SELECT COUNT(*)
                FROM VeteransClaims_ReviewedMedicalLiteratureClassifications AS reviewed
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM VeteransClaims_BasisRequirements AS basisRequirement
                    WHERE basisRequirement.RequirementId = reviewed.RequirementId
                );

                INSERT INTO VeteransClaims_M87LiteratureMigrationGuard (OrphanCount)
                SELECT COUNT(*)
                FROM VeteransClaims_ReviewedMedicalLiteratureExcerpts AS excerpt
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM VeteransClaims_BasisRequirements AS basisRequirement
                    WHERE basisRequirement.RequirementId = excerpt.RequirementId
                );

                CREATE TABLE VeteransClaims_M87RequirementMedicalLiterature AS
                SELECT basisRequirement.ServiceConnectionBasisId,
                       literature.RequirementId,
                       literature.MedicalLiteratureSourceId,
                       literature.GuidanceRole,
                       literature.Description
                FROM VeteransClaims_RequirementMedicalLiterature AS literature
                INNER JOIN VeteransClaims_BasisRequirements AS basisRequirement
                    ON basisRequirement.RequirementId = literature.RequirementId;

                CREATE TABLE VeteransClaims_M87ReviewedMedicalLiterature AS
                SELECT basisRequirement.ServiceConnectionBasisId,
                       reviewed.RequirementId,
                       reviewed.MedicalLiteratureSourceId,
                       reviewed.GuidanceRole,
                       reviewed.ArtifactId,
                       reviewed.Description,
                       reviewed.PromotedBy,
                       reviewed.PromotedUtc,
                       reviewed.ReviewedBy,
                       reviewed.ReviewedUtc,
                       reviewed.IntelligenceOutput,
                       reviewed.CapabilityId,
                       reviewed.ProviderId,
                       reviewed.CorrelationId,
                       reviewed.EngineName,
                       reviewed.EngineVersion,
                       reviewed.ProviderOperationId,
                       reviewed.StartedUtc,
                       reviewed.CompletedUtc,
                       reviewed.RequiresReview,
                       reviewed.WarningsJson,
                       reviewed.SupersededByCorrelationId,
                       reviewed.SupersededUtc
                FROM VeteransClaims_ReviewedMedicalLiteratureClassifications
                     AS reviewed
                INNER JOIN VeteransClaims_BasisRequirements AS basisRequirement
                    ON basisRequirement.RequirementId = reviewed.RequirementId;

                CREATE TABLE VeteransClaims_M87ReviewedMedicalLiteratureExcerpts AS
                SELECT basisRequirement.ServiceConnectionBasisId,
                       excerpt.RequirementId,
                       excerpt.MedicalLiteratureSourceId,
                       excerpt.GuidanceRole,
                       excerpt.ArtifactId,
                       excerpt.CorrelationId,
                       excerpt.ExcerptOrdinal,
                       excerpt.Text,
                       excerpt.StartOffset,
                       excerpt.Length
                FROM VeteransClaims_ReviewedMedicalLiteratureExcerpts AS excerpt
                INNER JOIN VeteransClaims_BasisRequirements AS basisRequirement
                    ON basisRequirement.RequirementId = excerpt.RequirementId;

                DROP TABLE VeteransClaims_ReviewedMedicalLiteratureExcerpts;
                DROP TABLE VeteransClaims_ReviewedMedicalLiteratureClassifications;
                DROP TABLE VeteransClaims_RequirementMedicalLiterature;

                CREATE TABLE VeteransClaims_RequirementMedicalLiterature (
                    ServiceConnectionBasisId TEXT NOT NULL,
                    RequirementId TEXT NOT NULL,
                    MedicalLiteratureSourceId TEXT NOT NULL,
                    GuidanceRole TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    PRIMARY KEY (
                        ServiceConnectionBasisId,
                        RequirementId,
                        MedicalLiteratureSourceId,
                        GuidanceRole
                    ),
                    FOREIGN KEY (
                        ServiceConnectionBasisId,
                        RequirementId
                    )
                        REFERENCES VeteransClaims_BasisRequirements (
                            ServiceConnectionBasisId,
                            RequirementId
                        ),
                    FOREIGN KEY (MedicalLiteratureSourceId)
                        REFERENCES VeteransClaims_MedicalLiteratureSources (Id)
                );

                CREATE TABLE
                    VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    ServiceConnectionBasisId TEXT NOT NULL,
                    RequirementId TEXT NOT NULL,
                    MedicalLiteratureSourceId TEXT NOT NULL,
                    GuidanceRole TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    Description TEXT NOT NULL,
                    PromotedBy TEXT NOT NULL,
                    PromotedUtc TEXT NOT NULL,
                    ReviewedBy TEXT NOT NULL,
                    ReviewedUtc TEXT NOT NULL,
                    IntelligenceOutput TEXT NOT NULL,
                    CapabilityId TEXT NOT NULL,
                    ProviderId TEXT NOT NULL,
                    CorrelationId TEXT NOT NULL,
                    EngineName TEXT NOT NULL,
                    EngineVersion TEXT NULL,
                    ProviderOperationId TEXT NULL,
                    StartedUtc TEXT NOT NULL,
                    CompletedUtc TEXT NOT NULL,
                    RequiresReview INTEGER NOT NULL,
                    WarningsJson TEXT NOT NULL,
                    SupersededByCorrelationId TEXT NULL,
                    SupersededUtc TEXT NULL,
                    PRIMARY KEY (
                        ServiceConnectionBasisId,
                        RequirementId,
                        MedicalLiteratureSourceId,
                        GuidanceRole,
                        ArtifactId,
                        CorrelationId
                    ),
                    FOREIGN KEY (
                        ServiceConnectionBasisId,
                        RequirementId,
                        MedicalLiteratureSourceId,
                        GuidanceRole
                    )
                        REFERENCES VeteransClaims_RequirementMedicalLiterature (
                            ServiceConnectionBasisId,
                            RequirementId,
                            MedicalLiteratureSourceId,
                            GuidanceRole
                        ),
                    FOREIGN KEY (
                        MedicalLiteratureSourceId,
                        ArtifactId
                    )
                        REFERENCES VeteransClaims_MedicalLiteratureSourceArtifacts (
                            MedicalLiteratureSourceId,
                            ArtifactId
                        )
                );

                CREATE TABLE
                    VeteransClaims_ReviewedMedicalLiteratureExcerpts (
                    ServiceConnectionBasisId TEXT NOT NULL,
                    RequirementId TEXT NOT NULL,
                    MedicalLiteratureSourceId TEXT NOT NULL,
                    GuidanceRole TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    CorrelationId TEXT NOT NULL,
                    ExcerptOrdinal INTEGER NOT NULL,
                    Text TEXT NOT NULL,
                    StartOffset INTEGER NULL,
                    Length INTEGER NULL,
                    PRIMARY KEY (
                        ServiceConnectionBasisId,
                        RequirementId,
                        MedicalLiteratureSourceId,
                        GuidanceRole,
                        ArtifactId,
                        CorrelationId,
                        ExcerptOrdinal
                    ),
                    FOREIGN KEY (
                        ServiceConnectionBasisId,
                        RequirementId,
                        MedicalLiteratureSourceId,
                        GuidanceRole,
                        ArtifactId,
                        CorrelationId
                    )
                        REFERENCES
                            VeteransClaims_ReviewedMedicalLiteratureClassifications (
                            ServiceConnectionBasisId,
                            RequirementId,
                            MedicalLiteratureSourceId,
                            GuidanceRole,
                            ArtifactId,
                            CorrelationId
                        )
                );

                INSERT INTO VeteransClaims_RequirementMedicalLiterature (
                    ServiceConnectionBasisId, RequirementId,
                    MedicalLiteratureSourceId, GuidanceRole, Description
                )
                SELECT ServiceConnectionBasisId, RequirementId,
                       MedicalLiteratureSourceId, GuidanceRole, Description
                FROM VeteransClaims_M87RequirementMedicalLiterature;

                INSERT INTO
                    VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    ServiceConnectionBasisId, RequirementId,
                    MedicalLiteratureSourceId, GuidanceRole, ArtifactId,
                    Description, PromotedBy, PromotedUtc, ReviewedBy,
                    ReviewedUtc, IntelligenceOutput, CapabilityId, ProviderId,
                    CorrelationId, EngineName, EngineVersion,
                    ProviderOperationId, StartedUtc, CompletedUtc,
                    RequiresReview, WarningsJson, SupersededByCorrelationId,
                    SupersededUtc
                )
                SELECT ServiceConnectionBasisId, RequirementId,
                       MedicalLiteratureSourceId, GuidanceRole, ArtifactId,
                       Description, PromotedBy, PromotedUtc, ReviewedBy,
                       ReviewedUtc, IntelligenceOutput, CapabilityId, ProviderId,
                       CorrelationId, EngineName, EngineVersion,
                       ProviderOperationId, StartedUtc, CompletedUtc,
                       RequiresReview, WarningsJson, SupersededByCorrelationId,
                       SupersededUtc
                FROM VeteransClaims_M87ReviewedMedicalLiterature;

                INSERT INTO VeteransClaims_ReviewedMedicalLiteratureExcerpts (
                    ServiceConnectionBasisId, RequirementId,
                    MedicalLiteratureSourceId, GuidanceRole, ArtifactId,
                    CorrelationId, ExcerptOrdinal, Text, StartOffset, Length
                )
                SELECT ServiceConnectionBasisId, RequirementId,
                       MedicalLiteratureSourceId, GuidanceRole, ArtifactId,
                       CorrelationId, ExcerptOrdinal, Text, StartOffset, Length
                FROM VeteransClaims_M87ReviewedMedicalLiteratureExcerpts;

                DROP TABLE VeteransClaims_M87ReviewedMedicalLiteratureExcerpts;
                DROP TABLE VeteransClaims_M87ReviewedMedicalLiterature;
                DROP TABLE VeteransClaims_M87RequirementMedicalLiterature;
                DROP TABLE VeteransClaims_M87LiteratureMigrationGuard;

                CREATE INDEX
                    IX_VeteransClaims_RequirementMedicalLiterature_Source
                ON VeteransClaims_RequirementMedicalLiterature (
                    MedicalLiteratureSourceId
                );

                CREATE INDEX
                    IX_VeteransClaims_RequirementMedicalLiterature_BasisRequirement
                ON VeteransClaims_RequirementMedicalLiterature (
                    ServiceConnectionBasisId,
                    RequirementId
                );

                CREATE UNIQUE INDEX
                    UX_VeteransClaims_ReviewedMedicalLiterature_LogicalDecision
                ON VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    ServiceConnectionBasisId,
                    RequirementId,
                    MedicalLiteratureSourceId,
                    GuidanceRole,
                    ArtifactId
                )
                WHERE SupersededUtc IS NULL;

                CREATE INDEX
                    IX_VeteransClaims_ReviewedMedicalLiterature_Artifact
                ON VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    ArtifactId
                );

                CREATE INDEX
                    IX_VeteransClaims_ReviewedMedicalLiterature_Correlation
                ON VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    CorrelationId
                );

                CREATE INDEX
                    IX_VeteransClaims_ReviewedMedicalLiterature_SupersededBy
                ON VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    SupersededByCorrelationId
                );

                CREATE INDEX
                    IX_VeteransClaims_ReviewedMedicalLiterature_BasisRequirement
                ON VeteransClaims_ReviewedMedicalLiteratureClassifications (
                    ServiceConnectionBasisId,
                    RequirementId
                );
                """),
            new VeteransClaimsSqliteMigration(
                88,
                "AddMedicalLiteratureReviewerText",
                """
                CREATE TABLE VeteransClaims_MedicalLiteratureReviewerText (
                    MedicalLiteratureSourceId TEXT NOT NULL,
                    ArtifactId TEXT NOT NULL,
                    Text TEXT NOT NULL,
                    SourceHash TEXT NULL,
                    ExtractionMethod TEXT NOT NULL,
                    ExtractedUtc TEXT NOT NULL,
                    PRIMARY KEY (MedicalLiteratureSourceId, ArtifactId),
                    FOREIGN KEY (MedicalLiteratureSourceId, ArtifactId)
                        REFERENCES VeteransClaims_MedicalLiteratureSourceArtifacts (
                            MedicalLiteratureSourceId, ArtifactId
                        )
                );

                CREATE INDEX
                    IX_VeteransClaims_MedicalLiteratureReviewerText_Artifact
                ON VeteransClaims_MedicalLiteratureReviewerText (ArtifactId);
                """),
            new VeteransClaimsSqliteMigration(
                89,
                "AddMedicationIndicationReconciliation",
                """
                CREATE TABLE VeteransClaims_MedicationIndicationReconciliations (
                    Id TEXT PRIMARY KEY,
                    VeteranId TEXT NOT NULL,
                    MedicationName TEXT NOT NULL COLLATE NOCASE,
                    ReconciliationDate TEXT NOT NULL,
                    Indication TEXT NOT NULL,
                    Source TEXT NOT NULL,
                    FOREIGN KEY (VeteranId) REFERENCES VeteransClaims_Veterans(Id),
                    UNIQUE (VeteranId, MedicationName, ReconciliationDate)
                );
                """),
            new VeteransClaimsSqliteMigration(
                90,
                "AddReviewerPackageSnapshots",
                """
                ALTER TABLE VeteransClaims_EvidencePackages
                    ADD COLUMN ReviewerSnapshotVersion INTEGER NOT NULL DEFAULT 0
                    CHECK (ReviewerSnapshotVersion IN (0, 1));
                ALTER TABLE VeteransClaims_EvidencePackages
                    ADD COLUMN ReviewerSnapshotSealed INTEGER NOT NULL DEFAULT 0
                    CHECK (ReviewerSnapshotSealed IN (0, 1));

                CREATE TABLE VeteransClaims_ReviewerPackageSnapshots (
                    EvidencePackageId TEXT PRIMARY KEY NOT NULL,
                    Version INTEGER NOT NULL CHECK (Version = 1),
                    Payload TEXT NOT NULL CHECK (length(Payload) > 0),
                    Sha256 TEXT NOT NULL CHECK (length(Sha256) = 64),
                    FOREIGN KEY (EvidencePackageId) REFERENCES VeteransClaims_EvidencePackages(Id)
                );

                CREATE TRIGGER ReviewerSnapshot_NoReplace BEFORE INSERT ON VeteransClaims_ReviewerPackageSnapshots
                WHEN EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPackageSnapshots WHERE EvidencePackageId = NEW.EvidencePackageId)
                BEGIN SELECT RAISE(ABORT, 'Reviewer snapshot already exists'); END;
                CREATE TRIGGER ReviewerSnapshot_PackageNoReplace BEFORE INSERT ON VeteransClaims_EvidencePackages
                WHEN EXISTS (SELECT 1 FROM VeteransClaims_EvidencePackages WHERE Id = NEW.Id)
                BEGIN SELECT RAISE(ABORT, 'Reviewer package already exists'); END;
                CREATE TRIGGER ReviewerSnapshot_NoUpdate BEFORE UPDATE ON VeteransClaims_ReviewerPackageSnapshots
                BEGIN SELECT RAISE(ABORT, 'Reviewer snapshot is immutable'); END;
                CREATE TRIGGER ReviewerSnapshot_NoDelete BEFORE DELETE ON VeteransClaims_ReviewerPackageSnapshots
                BEGIN SELECT RAISE(ABORT, 'Reviewer snapshot is immutable'); END;
                CREATE TRIGGER ReviewerSnapshot_OnlyNew BEFORE INSERT ON VeteransClaims_ReviewerPackageSnapshots
                WHEN NOT EXISTS (SELECT 1 FROM VeteransClaims_EvidencePackages WHERE Id = NEW.EvidencePackageId AND ReviewerSnapshotVersion = 1 AND ReviewerSnapshotSealed = 0)
                BEGIN SELECT RAISE(ABORT, 'Legacy package cannot be snapshotted'); END;
                CREATE TRIGGER ReviewerSnapshot_MarkSealed AFTER INSERT ON VeteransClaims_ReviewerPackageSnapshots
                BEGIN
                    UPDATE VeteransClaims_EvidencePackages SET ReviewerSnapshotSealed = 1 WHERE Id = NEW.EvidencePackageId;
                END;
                CREATE TRIGGER ReviewerSnapshot_PackageUpdate BEFORE UPDATE ON VeteransClaims_EvidencePackages
                WHEN OLD.Id != NEW.Id OR OLD.ReviewerSnapshotVersion != NEW.ReviewerSnapshotVersion OR OLD.ReviewerSnapshotSealed = 1
                    OR (OLD.ReviewerSnapshotSealed != NEW.ReviewerSnapshotSealed AND NOT
                        (OLD.ReviewerSnapshotSealed = 0 AND NEW.ReviewerSnapshotSealed = 1 AND EXISTS
                            (SELECT 1 FROM VeteransClaims_ReviewerPackageSnapshots WHERE EvidencePackageId = OLD.Id)))
                BEGIN SELECT RAISE(ABORT, 'Reviewer snapshot package is immutable'); END;
                CREATE TRIGGER ReviewerSnapshot_PackageDelete BEFORE DELETE ON VeteransClaims_EvidencePackages
                WHEN OLD.ReviewerSnapshotSealed = 1
                BEGIN SELECT RAISE(ABORT, 'Reviewer snapshot package is immutable'); END;
                CREATE TRIGGER ReviewerSnapshot_MemberInsert BEFORE INSERT ON VeteransClaims_EvidencePackageArtifacts
                WHEN EXISTS (SELECT 1 FROM VeteransClaims_EvidencePackages WHERE Id = NEW.EvidencePackageId AND ReviewerSnapshotSealed = 1)
                BEGIN SELECT RAISE(ABORT, 'Reviewer snapshot members are immutable'); END;
                CREATE TRIGGER ReviewerSnapshot_MemberUpdate BEFORE UPDATE ON VeteransClaims_EvidencePackageArtifacts
                WHEN EXISTS (SELECT 1 FROM VeteransClaims_EvidencePackages WHERE Id IN (OLD.EvidencePackageId, NEW.EvidencePackageId) AND ReviewerSnapshotSealed = 1)
                BEGIN SELECT RAISE(ABORT, 'Reviewer snapshot members are immutable'); END;
                CREATE TRIGGER ReviewerSnapshot_MemberDelete BEFORE DELETE ON VeteransClaims_EvidencePackageArtifacts
                WHEN EXISTS (SELECT 1 FROM VeteransClaims_EvidencePackages WHERE Id = OLD.EvidencePackageId AND ReviewerSnapshotSealed = 1)
                BEGIN SELECT RAISE(ABORT, 'Reviewer snapshot members are immutable'); END;
                """),
            new VeteransClaimsSqliteMigration(
                91,
                "AddReviewerPackageOutputProvenance",
                """
                CREATE TABLE VeteransClaims_ReviewerPackageOutputProvenance (
                    ProvenanceId TEXT PRIMARY KEY NOT NULL CHECK (length(ProvenanceId) = 64),
                    EvidencePackageId TEXT NOT NULL,
                    Version INTEGER NOT NULL CHECK (Version = 1),
                    Format TEXT NOT NULL CHECK (Format IN ('docx', 'pdf')),
                    SnapshotSha256 TEXT NOT NULL CHECK (length(SnapshotSha256) = 64),
                    RendererContract TEXT NOT NULL CHECK (length(RendererContract) > 0),
                    RendererBuild TEXT NOT NULL CHECK (length(RendererBuild) > 0),
                    ConverterIdentity TEXT NULL,
                    ConverterVersion TEXT NULL,
                    SourceReviewDate TEXT NOT NULL CHECK (length(SourceReviewDate) = 10),
                    OutputSha256 TEXT NOT NULL CHECK (length(OutputSha256) = 64),
                    ByteLength INTEGER NOT NULL CHECK (ByteLength > 0),
                    GeneratedUtc TEXT NOT NULL CHECK (length(GeneratedUtc) > 0),
                    FOREIGN KEY (EvidencePackageId) REFERENCES VeteransClaims_EvidencePackages(Id),
                    CHECK (
                        (Format = 'docx' AND ConverterIdentity IS NULL AND ConverterVersion IS NULL) OR
                        (Format = 'pdf' AND ConverterIdentity IS NOT NULL AND length(ConverterIdentity) > 0
                            AND ConverterVersion IS NOT NULL AND length(ConverterVersion) > 0)
                    )
                );

                CREATE INDEX IX_VeteransClaims_ReviewerPackageOutputProvenance_PackageFormat
                ON VeteransClaims_ReviewerPackageOutputProvenance (
                    EvidencePackageId,
                    Format,
                    GeneratedUtc
                );

                CREATE TRIGGER ReviewerOutputProvenance_NoReplace
                BEFORE INSERT ON VeteransClaims_ReviewerPackageOutputProvenance
                WHEN EXISTS (
                    SELECT 1 FROM VeteransClaims_ReviewerPackageOutputProvenance
                    WHERE ProvenanceId = NEW.ProvenanceId
                )
                BEGIN SELECT RAISE(ABORT, 'Reviewer output provenance already exists'); END;

                CREATE TRIGGER ReviewerOutputProvenance_NoUpdate
                BEFORE UPDATE ON VeteransClaims_ReviewerPackageOutputProvenance
                BEGIN SELECT RAISE(ABORT, 'Reviewer output provenance is immutable'); END;

                CREATE TRIGGER ReviewerOutputProvenance_NoDelete
                BEFORE DELETE ON VeteransClaims_ReviewerPackageOutputProvenance
                BEGIN SELECT RAISE(ABORT, 'Reviewer output provenance is immutable'); END;

                CREATE TRIGGER ReviewerOutputProvenance_OnlySealedSnapshot
                BEFORE INSERT ON VeteransClaims_ReviewerPackageOutputProvenance
                WHEN NOT EXISTS (
                    SELECT 1
                    FROM VeteransClaims_EvidencePackages p
                    JOIN VeteransClaims_ReviewerPackageSnapshots s
                        ON s.EvidencePackageId = p.Id
                    WHERE p.Id = NEW.EvidencePackageId
                        AND p.ReviewerSnapshotVersion = 1
                        AND p.ReviewerSnapshotSealed = 1
                        AND s.Version = 1
                        AND s.Sha256 = NEW.SnapshotSha256
                )
                BEGIN SELECT RAISE(ABORT, 'Reviewer output provenance requires the matching sealed snapshot'); END;
                """),
            new VeteransClaimsSqliteMigration(
                92,
                "AddReviewerPackageOutputBuildProvenance",
                """
                CREATE TABLE VeteransClaims_ReviewerPackageOutputBuildProvenance (
                    LinkId TEXT PRIMARY KEY NOT NULL CHECK (length(LinkId) = 64),
                    ProvenanceId TEXT NOT NULL,
                    Version INTEGER NOT NULL CHECK (Version = 1),
                    BuildId TEXT NOT NULL CHECK (
                        length(BuildId) = 71 AND substr(BuildId, 1, 7) = 'sha256:'),
                    SourceRevisionId TEXT NOT NULL CHECK (
                        length(SourceRevisionId) IN (40, 64)),
                    LinkedUtc TEXT NOT NULL CHECK (length(LinkedUtc) > 0),
                    FOREIGN KEY (ProvenanceId)
                        REFERENCES VeteransClaims_ReviewerPackageOutputProvenance(ProvenanceId),
                    UNIQUE (ProvenanceId, BuildId, SourceRevisionId)
                ) WITHOUT ROWID;

                CREATE INDEX IX_ReviewerPackageOutputBuildProvenance_Provenance
                ON VeteransClaims_ReviewerPackageOutputBuildProvenance (
                    ProvenanceId,
                    LinkedUtc
                );

                CREATE TRIGGER ReviewerOutputBuildProvenance_NoReplace
                BEFORE INSERT ON VeteransClaims_ReviewerPackageOutputBuildProvenance
                WHEN EXISTS (
                    SELECT 1
                    FROM VeteransClaims_ReviewerPackageOutputBuildProvenance
                    WHERE LinkId = NEW.LinkId
                       OR (
                            ProvenanceId = NEW.ProvenanceId
                            AND BuildId = NEW.BuildId
                            AND SourceRevisionId = NEW.SourceRevisionId
                       )
                )
                BEGIN SELECT RAISE(ABORT, 'Reviewer output build provenance already exists'); END;

                CREATE TRIGGER ReviewerOutputBuildProvenance_ParentRequired
                BEFORE INSERT ON VeteransClaims_ReviewerPackageOutputBuildProvenance
                WHEN NOT EXISTS (
                    SELECT 1
                    FROM VeteransClaims_ReviewerPackageOutputProvenance
                    WHERE ProvenanceId = NEW.ProvenanceId
                )
                BEGIN SELECT RAISE(ABORT, 'Reviewer output build provenance requires existing output provenance'); END;

                CREATE TRIGGER ReviewerOutputBuildProvenance_NoUpdate
                BEFORE UPDATE ON VeteransClaims_ReviewerPackageOutputBuildProvenance
                BEGIN SELECT RAISE(ABORT, 'Reviewer output build provenance is immutable'); END;

                CREATE TRIGGER ReviewerOutputBuildProvenance_NoDelete
                BEFORE DELETE ON VeteransClaims_ReviewerPackageOutputBuildProvenance
                BEGIN SELECT RAISE(ABORT, 'Reviewer output build provenance is immutable'); END;
                """),
            new VeteransClaimsSqliteMigration(
                93,
                "ArchiveReviewerBuildManifests",
                """
                CREATE TABLE VeteransClaims_ReviewerBuildManifests (
                    BuildId TEXT PRIMARY KEY NOT NULL CHECK (
                        length(BuildId) = 71 AND substr(BuildId, 1, 7) = 'sha256:'),
                    ManifestJson TEXT NOT NULL CHECK (length(ManifestJson) > 0),
                    ArchivedUtc TEXT NOT NULL CHECK (length(ArchivedUtc) > 0)
                ) WITHOUT ROWID;

                CREATE TRIGGER ReviewerBuildManifest_NoReplace
                BEFORE INSERT ON VeteransClaims_ReviewerBuildManifests
                WHEN EXISTS (
                    SELECT 1
                    FROM VeteransClaims_ReviewerBuildManifests
                    WHERE BuildId = NEW.BuildId
                )
                BEGIN SELECT RAISE(ABORT, 'Reviewer build manifest already exists'); END;

                CREATE TRIGGER ReviewerBuildManifest_NoUpdate
                BEFORE UPDATE ON VeteransClaims_ReviewerBuildManifests
                BEGIN SELECT RAISE(ABORT, 'Reviewer build manifest is immutable'); END;

                CREATE TRIGGER ReviewerBuildManifest_NoDelete
                BEFORE DELETE ON VeteransClaims_ReviewerBuildManifests
                BEGIN SELECT RAISE(ABORT, 'Reviewer build manifest is immutable'); END;
                """),
            new VeteransClaimsSqliteMigration(
                94,
                "FreezeReviewerPackagePresentation",
                """
                CREATE TABLE VeteransClaims_ReviewerPresentations (
                    EvidencePackageId TEXT PRIMARY KEY NOT NULL,
                    SourceSnapshotSha256 TEXT NOT NULL CHECK (length(SourceSnapshotSha256) = 64 AND SourceSnapshotSha256 NOT GLOB '*[^0-9A-F]*'),
                    PackagePreparedDate TEXT NOT NULL CHECK (length(PackagePreparedDate) = 10 AND PackagePreparedDate GLOB '[0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]' AND PackagePreparedDate > '0001-01-01' AND date(PackagePreparedDate, '+0 days') IS PackagePreparedDate),
                    Payload TEXT NOT NULL CHECK (json_valid(Payload) AND json_type(Payload) = 'object'),
                    Sha256 TEXT NOT NULL CHECK (length(Sha256) = 64 AND Sha256 NOT GLOB '*[^0-9A-F]*'),
                    DocxSha256 TEXT NOT NULL CHECK (length(DocxSha256) = 64 AND DocxSha256 NOT GLOB '*[^0-9A-F]*'),
                    PreviousPackageId TEXT NULL,
                    FOREIGN KEY (PreviousPackageId) REFERENCES VeteransClaims_ReviewerPresentations(EvidencePackageId),
                    CHECK (PreviousPackageId IS NULL OR (length(trim(PreviousPackageId)) > 0 AND PreviousPackageId != EvidencePackageId)),
                    CHECK (json_type(Payload, '$.PackageId') IS 'text' AND json_extract(Payload, '$.PackageId') IS EvidencePackageId
                        AND json_type(Payload, '$.Version') IS 'integer' AND json_extract(Payload, '$.Version') IS 1
                        AND json_type(Payload, '$.SourceSnapshotSha256') IS 'text' AND json_extract(Payload, '$.SourceSnapshotSha256') IS SourceSnapshotSha256
                        AND json_type(Payload, '$.PackagePreparedDate') IS 'text' AND json_extract(Payload, '$.PackagePreparedDate') IS PackagePreparedDate
                        AND json_type(Payload, '$.Sha256') IS 'text' AND json_extract(Payload, '$.Sha256') IS Sha256
                        AND json_type(Payload, '$.DocxSha256') IS 'text' AND json_extract(Payload, '$.DocxSha256') IS DocxSha256
                        AND (json_type(Payload, '$.PreviousPackageId') IS NULL OR json_type(Payload, '$.PreviousPackageId') IN ('null', 'text'))
                        AND json_extract(Payload, '$.PreviousPackageId') IS PreviousPackageId
                        AND json_type(Payload, '$.Cover') IS 'object'
                        AND json_type(Payload, '$.Cover.ReviewerRole') IS 'text'
                        AND coalesce(length(trim(json_extract(Payload, '$.Cover.ReviewerRole'))), 0) > 0
                        AND json_type(Payload, '$.RenderProfile') IS 'text'
                        AND coalesce(length(trim(json_extract(Payload, '$.RenderProfile'))), 0) > 0
                        AND json_type(Payload, '$.PreparationRendererBuild') IS 'text'
                        AND coalesce(length(trim(json_extract(Payload, '$.PreparationRendererBuild'))), 0) > 0
                        AND json_type(Payload, '$.DocxBase64') IS 'text'
                        AND coalesce(length(json_extract(Payload, '$.DocxBase64')), 0) > 0),
                    FOREIGN KEY (EvidencePackageId) REFERENCES VeteransClaims_ReviewerPackageSnapshots(EvidencePackageId)
                ) WITHOUT ROWID;
                CREATE TABLE VeteransClaims_ReviewerFrozenPdfs (
                    EvidencePackageId TEXT PRIMARY KEY NOT NULL,
                    PresentationSha256 TEXT NOT NULL CHECK (length(PresentationSha256) = 64 AND PresentationSha256 NOT GLOB '*[^0-9A-F]*'),
                    Payload TEXT NOT NULL CHECK (json_valid(Payload) AND json_type(Payload) = 'object'),
                    Sha256 TEXT NOT NULL CHECK (length(Sha256) = 64 AND Sha256 NOT GLOB '*[^0-9A-F]*'),
                    PdfSha256 TEXT NOT NULL CHECK (length(PdfSha256) = 64 AND PdfSha256 NOT GLOB '*[^0-9A-F]*'),
                    ConverterIdentity TEXT NOT NULL CHECK (length(trim(ConverterIdentity)) > 0),
                    ConverterVersion TEXT NOT NULL CHECK (length(trim(ConverterVersion)) > 0),
                    CHECK (json_type(Payload, '$.PackageId') IS 'text' AND json_extract(Payload, '$.PackageId') IS EvidencePackageId
                        AND json_type(Payload, '$.Version') IS 'integer' AND json_extract(Payload, '$.Version') IS 1
                        AND json_type(Payload, '$.PresentationSha256') IS 'text' AND json_extract(Payload, '$.PresentationSha256') IS PresentationSha256
                        AND json_type(Payload, '$.Sha256') IS 'text' AND json_extract(Payload, '$.Sha256') IS Sha256
                        AND json_type(Payload, '$.PdfSha256') IS 'text' AND json_extract(Payload, '$.PdfSha256') IS PdfSha256
                        AND json_type(Payload, '$.ConverterIdentity') IS 'text' AND json_extract(Payload, '$.ConverterIdentity') IS ConverterIdentity
                        AND json_type(Payload, '$.ConverterVersion') IS 'text' AND json_extract(Payload, '$.ConverterVersion') IS ConverterVersion
                        AND json_type(Payload, '$.PdfBase64') IS 'text'
                        AND coalesce(length(json_extract(Payload, '$.PdfBase64')), 0) > 0),
                    FOREIGN KEY (EvidencePackageId) REFERENCES VeteransClaims_ReviewerPresentations(EvidencePackageId)
                ) WITHOUT ROWID;
                -- Require an already-frozen predecessor in the same claim issue even when
                -- foreign_keys is disabled. Insert-only lineage cannot form a cycle.
                CREATE TRIGGER ReviewerPresentation_LineageRequired BEFORE INSERT ON VeteransClaims_ReviewerPresentations
                WHEN NEW.PreviousPackageId IS NOT NULL AND NOT EXISTS (
                    SELECT 1 FROM VeteransClaims_ReviewerPresentations prior
                    JOIN VeteransClaims_EvidencePackages old ON old.Id = prior.EvidencePackageId
                    JOIN VeteransClaims_EvidencePackages current ON current.Id = NEW.EvidencePackageId
                    WHERE prior.EvidencePackageId = NEW.PreviousPackageId
                        AND old.ClaimIssueId = current.ClaimIssueId
                        AND prior.EvidencePackageId != NEW.EvidencePackageId)
                BEGIN SELECT RAISE(ABORT, 'Reviewer version requires an existing predecessor in the same claim issue'); END;
                -- SQLite and .NET resolve duplicate JSON properties differently.
                CREATE TRIGGER ReviewerPresentation_UniqueJsonKeys BEFORE INSERT ON VeteransClaims_ReviewerPresentations
                WHEN EXISTS (
                    SELECT 1 FROM json_tree(CASE WHEN json_valid(NEW.Payload) THEN NEW.Payload ELSE '{}' END) child
                    JOIN json_tree(CASE WHEN json_valid(NEW.Payload) THEN NEW.Payload ELSE '{}' END) parent
                        ON parent.id = child.parent
                    WHERE parent.type = 'object'
                    GROUP BY child.parent, child.key HAVING count(*) > 1)
                BEGIN SELECT RAISE(ABORT, 'Frozen reviewer JSON contains duplicate object keys'); END;
                CREATE TRIGGER ReviewerPresentation_NoReplace BEFORE INSERT ON VeteransClaims_ReviewerPresentations
                WHEN EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPresentations WHERE EvidencePackageId = NEW.EvidencePackageId)
                BEGIN SELECT RAISE(ABORT, 'Reviewer presentation already exists'); END;
                CREATE TRIGGER ReviewerPresentation_NoUpdate BEFORE UPDATE ON VeteransClaims_ReviewerPresentations
                BEGIN SELECT RAISE(ABORT, 'Reviewer presentation is immutable'); END;
                CREATE TRIGGER ReviewerPresentation_NoDelete BEFORE DELETE ON VeteransClaims_ReviewerPresentations
                BEGIN SELECT RAISE(ABORT, 'Reviewer presentation is immutable'); END;
                CREATE TRIGGER ReviewerPresentation_SourceRequired BEFORE INSERT ON VeteransClaims_ReviewerPresentations
                WHEN NOT EXISTS (
                    SELECT 1 FROM VeteransClaims_ReviewerPackageSnapshots s
                    JOIN VeteransClaims_EvidencePackages p ON p.Id = s.EvidencePackageId
                    WHERE s.EvidencePackageId = NEW.EvidencePackageId AND s.Version = 1
                    AND p.ReviewerSnapshotVersion = 1 AND p.ReviewerSnapshotSealed = 1
                    AND s.Sha256 = NEW.SourceSnapshotSha256)
                OR EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPackageOutputProvenance o
                    WHERE o.EvidencePackageId = NEW.EvidencePackageId AND (o.SourceReviewDate != NEW.PackagePreparedDate OR (o.Format = 'docx' AND o.OutputSha256 != NEW.DocxSha256)))
                BEGIN SELECT RAISE(ABORT, 'Reviewer presentation requires the sealed source and preserved date'); END;
                -- SQLite and .NET resolve duplicate JSON properties differently.
                CREATE TRIGGER ReviewerFrozenPdf_UniqueJsonKeys BEFORE INSERT ON VeteransClaims_ReviewerFrozenPdfs
                WHEN EXISTS (
                    SELECT 1 FROM json_tree(CASE WHEN json_valid(NEW.Payload) THEN NEW.Payload ELSE '{}' END) child
                    JOIN json_tree(CASE WHEN json_valid(NEW.Payload) THEN NEW.Payload ELSE '{}' END) parent
                        ON parent.id = child.parent
                    WHERE parent.type = 'object'
                    GROUP BY child.parent, child.key HAVING count(*) > 1)
                BEGIN SELECT RAISE(ABORT, 'Frozen reviewer JSON contains duplicate object keys'); END;
                CREATE TRIGGER ReviewerFrozenPdf_NoReplace BEFORE INSERT ON VeteransClaims_ReviewerFrozenPdfs
                WHEN EXISTS (SELECT 1 FROM VeteransClaims_ReviewerFrozenPdfs WHERE EvidencePackageId = NEW.EvidencePackageId)
                BEGIN SELECT RAISE(ABORT, 'Reviewer PDF already exists'); END;
                CREATE TRIGGER ReviewerFrozenPdf_NoUpdate BEFORE UPDATE ON VeteransClaims_ReviewerFrozenPdfs
                BEGIN SELECT RAISE(ABORT, 'Reviewer PDF is immutable'); END;
                CREATE TRIGGER ReviewerFrozenPdf_NoDelete BEFORE DELETE ON VeteransClaims_ReviewerFrozenPdfs
                BEGIN SELECT RAISE(ABORT, 'Reviewer PDF is immutable'); END;
                CREATE TRIGGER ReviewerFrozenPdf_PresentationRequired BEFORE INSERT ON VeteransClaims_ReviewerFrozenPdfs
                WHEN NOT EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPresentations
                    WHERE EvidencePackageId = NEW.EvidencePackageId AND Sha256 = NEW.PresentationSha256)
                OR EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPackageOutputProvenance o
                    WHERE o.EvidencePackageId = NEW.EvidencePackageId AND o.Format = 'pdf'
                        AND (o.OutputSha256 != NEW.PdfSha256 OR o.ConverterIdentity != NEW.ConverterIdentity
                            OR o.ConverterVersion != NEW.ConverterVersion))
                BEGIN SELECT RAISE(ABORT, 'Reviewer PDF requires the frozen presentation and matching historical outputs'); END;
                CREATE TRIGGER ReviewerOutput_FrozenPresentationRequired BEFORE INSERT ON VeteransClaims_ReviewerPackageOutputProvenance
                WHEN EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPresentations p WHERE p.EvidencePackageId = NEW.EvidencePackageId
                    AND (p.SourceSnapshotSha256 != NEW.SnapshotSha256 OR p.PackagePreparedDate != NEW.SourceReviewDate
                        OR (NEW.Format = 'docx' AND p.DocxSha256 != NEW.OutputSha256)))
                OR (NEW.Format = 'pdf' AND EXISTS (SELECT 1 FROM VeteransClaims_ReviewerPresentations WHERE EvidencePackageId = NEW.EvidencePackageId)
                    AND NOT EXISTS (SELECT 1 FROM VeteransClaims_ReviewerFrozenPdfs WHERE EvidencePackageId = NEW.EvidencePackageId))
                OR EXISTS (SELECT 1 FROM VeteransClaims_ReviewerFrozenPdfs p WHERE p.EvidencePackageId = NEW.EvidencePackageId
                    AND NEW.Format = 'pdf' AND (p.PdfSha256 != NEW.OutputSha256
                        OR p.ConverterIdentity != NEW.ConverterIdentity OR p.ConverterVersion != NEW.ConverterVersion))
                BEGIN SELECT RAISE(ABORT, 'Reviewer output differs from frozen presentation'); END;
                """),
            new VeteransClaimsSqliteMigration(
                95,
                "AddReviewerOperationSnapshots",
                """
                CREATE TABLE VeteransClaims_ReviewerOperationSnapshots (
                    OperationSnapshotId TEXT NOT NULL PRIMARY KEY CHECK (
                        typeof(OperationSnapshotId) = 'text'
                        AND length(OperationSnapshotId) BETWEEN 1 AND 128
                        AND length(CAST(OperationSnapshotId AS BLOB)) = length(OperationSnapshotId)
                        AND OperationSnapshotId NOT GLOB '*[^A-Za-z0-9_.:-]*'),
                    ReviewerOperationId TEXT NOT NULL CHECK (
                        typeof(ReviewerOperationId) = 'text'
                        AND length(ReviewerOperationId) BETWEEN 1 AND 128
                        AND length(CAST(ReviewerOperationId AS BLOB)) = length(ReviewerOperationId)
                        AND ReviewerOperationId NOT GLOB '*[^A-Za-z0-9_.:-]*'),
                    State TEXT NOT NULL CHECK (State IN ('Capturing', 'Materializing', 'Ready')),
                    Revision INTEGER NOT NULL CHECK (typeof(Revision) = 'integer' AND Revision > 0),
                    OwnerToken TEXT NOT NULL CHECK (
                        typeof(OwnerToken) = 'text'
                        AND length(OwnerToken) BETWEEN 1 AND 128
                        AND length(CAST(OwnerToken AS BLOB)) = length(OwnerToken)
                        AND OwnerToken NOT GLOB '*[^A-Za-z0-9_.:-]*'),
                    Profile TEXT NOT NULL CHECK (Profile = 'Reviewer.AdoptedUtf8.ContractProof.v1'),
                    RepresentationVersion INTEGER NOT NULL CHECK (
                        typeof(RepresentationVersion) = 'integer' AND RepresentationVersion = 1),
                    BundleSha256 TEXT NULL CHECK (
                        BundleSha256 IS NULL OR (typeof(BundleSha256) = 'text'
                        AND length(BundleSha256) = 64 AND length(CAST(BundleSha256 AS BLOB)) = 64
                        AND BundleSha256 NOT GLOB '*[^0-9A-F]*')),
                    ReadyValidationVersion INTEGER NULL CHECK (
                        ReadyValidationVersion IS NULL OR (typeof(ReadyValidationVersion) = 'integer'
                        AND ReadyValidationVersion = 1)),
                    Disposition TEXT NOT NULL CHECK (Disposition IN ('Active', 'RequiresReview', 'Failed')),
                    FailureCategory INTEGER NULL CHECK (
                        FailureCategory IS NULL OR (typeof(FailureCategory) = 'integer'
                        AND FailureCategory IN (1, 2, 3))),
                    CHECK (
                        (State = 'Capturing' AND BundleSha256 IS NULL AND ReadyValidationVersion IS NULL)
                        OR (State = 'Materializing' AND BundleSha256 IS NOT NULL AND ReadyValidationVersion IS NULL)
                        OR (State = 'Ready' AND BundleSha256 IS NOT NULL AND ReadyValidationVersion IS 1)),
                    CHECK (
                        (Disposition = 'Active' AND FailureCategory IS NULL)
                        OR (State = 'Capturing' AND Disposition = 'RequiresReview' AND FailureCategory IS 1)
                        OR (State = 'Capturing' AND Disposition = 'Failed' AND FailureCategory IS 2)
                        OR (State IN ('Materializing', 'Ready') AND Disposition = 'RequiresReview' AND FailureCategory IS 3))
                );

                CREATE UNIQUE INDEX UX_VeteransClaims_ReviewerOperationSnapshots_ReviewerOperationId
                ON VeteransClaims_ReviewerOperationSnapshots (ReviewerOperationId);

                CREATE TRIGGER ReviewerOperationSnapshot_NoDuplicateInsert
                BEFORE INSERT ON VeteransClaims_ReviewerOperationSnapshots
                WHEN EXISTS (
                    SELECT 1 FROM VeteransClaims_ReviewerOperationSnapshots
                    WHERE OperationSnapshotId = NEW.OperationSnapshotId
                       OR ReviewerOperationId = NEW.ReviewerOperationId)
                BEGIN SELECT RAISE(ABORT, 'Reviewer operation snapshot identity already exists'); END;

                CREATE TRIGGER ReviewerOperationSnapshot_InitialInsert
                BEFORE INSERT ON VeteransClaims_ReviewerOperationSnapshots
                WHEN NOT (
                    NEW.State IS 'Capturing' AND NEW.Revision IS 1
                    AND NEW.Disposition IS 'Active' AND NEW.BundleSha256 IS NULL
                    AND NEW.ReadyValidationVersion IS NULL AND NEW.FailureCategory IS NULL)
                BEGIN SELECT RAISE(ABORT, 'Reviewer operation snapshot requires initial Capturing authority'); END;

                CREATE TRIGGER ReviewerOperationSnapshot_NoDelete
                BEFORE DELETE ON VeteransClaims_ReviewerOperationSnapshots
                BEGIN SELECT RAISE(ABORT, 'Reviewer operation snapshot authority cannot be deleted'); END;

                CREATE TRIGGER ReviewerOperationSnapshot_AllowedUpdate
                BEFORE UPDATE ON VeteransClaims_ReviewerOperationSnapshots
                WHEN NOT (
                    NEW.OperationSnapshotId IS OLD.OperationSnapshotId
                    AND NEW.ReviewerOperationId IS OLD.ReviewerOperationId
                    AND NEW.Profile IS OLD.Profile
                    AND NEW.RepresentationVersion IS OLD.RepresentationVersion
                    AND typeof(OLD.Revision) = 'integer' AND typeof(NEW.Revision) = 'integer'
                    AND OLD.Revision < 9223372036854775807
                    AND NEW.Revision = OLD.Revision + 1
                    AND OLD.Disposition IS 'Active'
                    AND (
                        (
                            OLD.State IS 'Capturing' AND NEW.State IS 'Materializing'
                            AND NEW.OwnerToken IS OLD.OwnerToken
                            AND OLD.BundleSha256 IS NULL AND NEW.BundleSha256 IS NOT NULL
                            AND NEW.ReadyValidationVersion IS OLD.ReadyValidationVersion
                            AND NEW.Disposition IS OLD.Disposition
                            AND NEW.FailureCategory IS OLD.FailureCategory
                        )
                        OR (
                            OLD.State IS 'Materializing' AND NEW.State IS 'Ready'
                            AND NEW.OwnerToken IS OLD.OwnerToken
                            AND NEW.BundleSha256 IS OLD.BundleSha256
                            AND OLD.ReadyValidationVersion IS NULL AND NEW.ReadyValidationVersion IS 1
                            AND NEW.Disposition IS OLD.Disposition
                            AND NEW.FailureCategory IS OLD.FailureCategory
                        )
                        OR (
                            OLD.State IN ('Capturing', 'Materializing') AND NEW.State IS OLD.State
                            AND NEW.OwnerToken IS NOT OLD.OwnerToken
                            AND NEW.BundleSha256 IS OLD.BundleSha256
                            AND NEW.ReadyValidationVersion IS OLD.ReadyValidationVersion
                            AND NEW.Disposition IS OLD.Disposition
                            AND NEW.FailureCategory IS OLD.FailureCategory
                        )
                        OR (
                            NEW.State IS OLD.State AND NEW.OwnerToken IS OLD.OwnerToken
                            AND NEW.BundleSha256 IS OLD.BundleSha256
                            AND NEW.ReadyValidationVersion IS OLD.ReadyValidationVersion
                            AND OLD.FailureCategory IS NULL
                            AND (
                                (OLD.State IS 'Capturing' AND NEW.Disposition IS 'RequiresReview' AND NEW.FailureCategory IS 1)
                                OR (OLD.State IS 'Capturing' AND NEW.Disposition IS 'Failed' AND NEW.FailureCategory IS 2)
                                OR (OLD.State IN ('Materializing', 'Ready') AND NEW.Disposition IS 'RequiresReview' AND NEW.FailureCategory IS 3)
                            )
                        )
                    )
                )
                BEGIN SELECT RAISE(ABORT, 'Invalid reviewer operation snapshot authority mutation'); END;
                """)
        };
}

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
                """)
        };
}

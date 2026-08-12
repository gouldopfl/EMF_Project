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
                """)
        };
}

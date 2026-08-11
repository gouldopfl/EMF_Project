namespace EMF.Extensions.VeteransClaims.Persistence.Sqlite;

internal static class VeteransClaimsSqliteMigrations
{
    public static IReadOnlyList<
        VeteransClaimsSqliteMigration> All { get; } =
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
                """)
        };
}

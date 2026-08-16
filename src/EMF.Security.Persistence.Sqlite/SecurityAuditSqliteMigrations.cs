namespace EMF.Security.Persistence.Sqlite;

internal static class SecurityAuditSqliteMigrations
{
    public static IReadOnlyList<
        SecurityAuditSqliteMigration> All
    { get; } =
        new[]
        {
            new SecurityAuditSqliteMigration(
                1,
                "InitialSecurityAuditSchema",
                """
                CREATE TABLE SecurityAuditRecords (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Operation TEXT NOT NULL,
                    ResourceType TEXT NOT NULL,
                    ResourceId TEXT NOT NULL,
                    SubjectId TEXT NOT NULL,
                    PolicyDecision TEXT NULL,
                    Destination TEXT NULL,
                    Outcome TEXT NOT NULL,
                    OccurredUtc TEXT NOT NULL,
                    FactsJson TEXT NOT NULL
                );

                CREATE INDEX
                    IX_SecurityAuditRecords_Resource
                ON SecurityAuditRecords (
                    ResourceType,
                    ResourceId,
                    OccurredUtc
                );

                CREATE INDEX
                    IX_SecurityAuditRecords_Subject
                ON SecurityAuditRecords (
                    SubjectId,
                    OccurredUtc
                );
                """),
            new SecurityAuditSqliteMigration(
                2,
                "AddTamperEvidentHashChain",
                """
                ALTER TABLE SecurityAuditRecords
                ADD COLUMN IntegrityVersion INTEGER NOT NULL DEFAULT 0
                    CHECK (IntegrityVersion IN (0, 1));

                ALTER TABLE SecurityAuditRecords
                ADD COLUMN PreviousRecordHash TEXT NULL;

                ALTER TABLE SecurityAuditRecords
                ADD COLUMN RecordHash TEXT NULL;

                CREATE UNIQUE INDEX
                    IX_SecurityAuditRecords_RecordHash
                ON SecurityAuditRecords (RecordHash)
                WHERE RecordHash IS NOT NULL;
                """)
        };
}

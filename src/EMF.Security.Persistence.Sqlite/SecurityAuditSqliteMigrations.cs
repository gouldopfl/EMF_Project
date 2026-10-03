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
                """),
            new SecurityAuditSqliteMigration(3, "StructuredCanonicalAuditEvents", """
                CREATE TEMP TABLE AuditSequence AS SELECT seq FROM sqlite_sequence WHERE name = 'SecurityAuditRecords';
                CREATE TABLE SecurityAuditRecordsV2 (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    Operation TEXT NOT NULL, ResourceType TEXT NOT NULL, ResourceId TEXT NOT NULL,
                    SubjectId TEXT NOT NULL, PolicyDecision TEXT NULL, Destination TEXT NULL,
                    Outcome TEXT NOT NULL, OccurredUtc TEXT NOT NULL, FactsJson TEXT NOT NULL,
                    IntegrityVersion INTEGER NOT NULL DEFAULT 0 CHECK (IntegrityVersion IN (0,1,2)),
                    PreviousRecordHash TEXT NULL, RecordHash TEXT NULL,
                    AuditEventId TEXT NULL, OperationId TEXT NULL, OriginalActorId TEXT NULL,
                    ServiceActorId TEXT NULL, RecoveryActorId TEXT NULL
                );
                INSERT INTO SecurityAuditRecordsV2 (Id, Operation, ResourceType, ResourceId, SubjectId,
                    PolicyDecision, Destination, Outcome, OccurredUtc, FactsJson, IntegrityVersion, PreviousRecordHash, RecordHash)
                SELECT Id, Operation, ResourceType, ResourceId, SubjectId, PolicyDecision, Destination, Outcome,
                    OccurredUtc, FactsJson, IntegrityVersion, PreviousRecordHash, RecordHash FROM SecurityAuditRecords;
                DROP TABLE SecurityAuditRecords;
                ALTER TABLE SecurityAuditRecordsV2 RENAME TO SecurityAuditRecords;
                UPDATE sqlite_sequence SET seq = MAX(seq, COALESCE((SELECT MAX(seq) FROM AuditSequence), 0)) WHERE name = 'SecurityAuditRecords';
                DROP TABLE AuditSequence;
                CREATE INDEX IX_SecurityAuditRecords_Resource ON SecurityAuditRecords(ResourceType, ResourceId, OccurredUtc);
                CREATE INDEX IX_SecurityAuditRecords_Subject ON SecurityAuditRecords(SubjectId, OccurredUtc);
                CREATE UNIQUE INDEX IX_SecurityAuditRecords_RecordHash ON SecurityAuditRecords(RecordHash) WHERE RecordHash IS NOT NULL;
                CREATE UNIQUE INDEX IX_SecurityAuditRecords_AuditEventId ON SecurityAuditRecords(AuditEventId) WHERE AuditEventId IS NOT NULL;
                """)
        };
}

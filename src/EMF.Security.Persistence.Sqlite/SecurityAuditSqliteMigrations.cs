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
                """)
        };
}

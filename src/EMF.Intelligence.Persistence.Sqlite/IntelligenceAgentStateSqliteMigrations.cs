namespace EMF.Intelligence.Persistence.Sqlite;

internal static class IntelligenceAgentStateSqliteMigrations
{
    public static IReadOnlyList<
        IntelligenceAgentStateSqliteMigration> All
    { get; } =
        new[]
        {
            new IntelligenceAgentStateSqliteMigration(
                1,
                "InitialIntelligenceAgentStateSchema",
                """
                CREATE TABLE IntelligenceAgentStates (
                    AgentId TEXT NOT NULL,
                    StateId TEXT NOT NULL,
                    Version INTEGER NOT NULL,
                    Payload TEXT NOT NULL,
                    UpdatedUtc TEXT NOT NULL,
                    PRIMARY KEY (
                        AgentId,
                        StateId
                    )
                );

                CREATE INDEX
                    IX_IntelligenceAgentStates_Agent
                ON IntelligenceAgentStates (
                    AgentId,
                    UpdatedUtc
                );
                """)
        };
}

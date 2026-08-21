namespace EMF.Intelligence.Persistence.Sqlite;

internal sealed record IntelligenceAgentStateSqliteMigration(
    int Version,
    string Name,
    string Sql);

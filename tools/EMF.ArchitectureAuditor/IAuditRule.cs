namespace EMF.ArchitectureAuditor;

public interface IAuditRule
{
    string Id { get; }

    string Version { get; }

    string Category { get; }

    IEnumerable<AuditFinding> Analyze(
        ParsedSourceFile source,
        CancellationToken cancellationToken = default);
}

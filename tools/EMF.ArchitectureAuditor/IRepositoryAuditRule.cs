namespace EMF.ArchitectureAuditor;

public interface IRepositoryAuditRule
{
    string Id { get; }

    string Version { get; }

    string Category { get; }

    IEnumerable<AuditFinding> Analyze(
        string repositoryRoot,
        CancellationToken cancellationToken = default);
}

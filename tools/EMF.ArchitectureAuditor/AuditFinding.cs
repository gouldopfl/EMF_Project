namespace EMF.ArchitectureAuditor;

public enum AuditSeverity
{
    Information,
    Low,
    Medium,
    High,
    Critical
}

public enum AuditConfidence
{
    Low,
    Medium,
    High
}

public enum AuditAnalysisMode
{
    Syntax,
    Semantic
}

public sealed record AuditFinding(
    string RuleId,
    string RuleVersion,
    string Category,
    AuditSeverity Severity,
    AuditConfidence Confidence,
    AuditAnalysisMode AnalysisMode,
    string RelativePath,
    int Line,
    int Column,
    string Message,
    string Recommendation);

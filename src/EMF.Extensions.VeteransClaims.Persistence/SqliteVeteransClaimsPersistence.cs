using EMF.Extensions.VeteransClaims.Contracts;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite;
using EMF.Extensions.VeteransClaims.Persistence.Sqlite.Repositories;

namespace EMF.Extensions.VeteransClaims.Persistence;

internal sealed class SqliteVeteransClaimsPersistence :
    IVeteransClaimsPersistence
{
    private readonly VeteransClaimsSqliteSchema _schema;

    public SqliteVeteransClaimsPersistence(
        string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            databasePath);

        _schema =
            new VeteransClaimsSqliteSchema(databasePath);

        Veterans =
            new SqliteVeteranRepository(databasePath);

        Claims =
            new SqliteClaimRepository(databasePath);

        ClaimIssues =
            new SqliteClaimIssueRepository(databasePath);

        Submissions =
            new SqliteSubmissionRepository(databasePath);

        Decisions =
            new SqliteVaDecisionRepository(databasePath);

        DisabilityEvaluations =
            new SqliteDisabilityEvaluationRepository(
                databasePath);
    }

    public IVeteranRepository Veterans { get; }

    public IClaimRepository Claims { get; }

    public IClaimIssueRepository ClaimIssues { get; }

    public ISubmissionRepository Submissions { get; }

    public IVaDecisionRepository Decisions { get; }

    public IDisabilityEvaluationRepository
        DisabilityEvaluations { get; }

    public Task InitializeAsync(
        CancellationToken cancellationToken = default)
    {
        return _schema.InitializeAsync(cancellationToken);
    }
}

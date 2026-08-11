namespace EMF.Extensions.VeteransClaims.Persistence;

public sealed class VeteransClaimsPersistenceOptions
{
    public required string Provider { get; init; }

    public required IReadOnlyDictionary<string, string>
        Settings { get; init; }
}

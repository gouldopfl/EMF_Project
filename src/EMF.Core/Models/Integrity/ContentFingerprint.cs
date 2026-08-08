namespace EMF.Core.Models.Integrity;

public sealed record ContentFingerprint
{
    public required string Algorithm { get; init; }

    public required string Value { get; init; }
}

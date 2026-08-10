namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct ExposureId
{
    public ExposureId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Exposure ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

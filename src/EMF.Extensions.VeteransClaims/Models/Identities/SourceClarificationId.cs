namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct SourceClarificationId
{
    public SourceClarificationId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Source Clarification ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}

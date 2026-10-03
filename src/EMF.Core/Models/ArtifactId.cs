namespace EMF.Core.Models.Identities;

public readonly record struct ArtifactId
{
    [System.Text.Json.Serialization.JsonConstructor]
    public ArtifactId(string value)
    {
        Value =
            global::EMF.Core.Models.Identities.IdentityValueValidator.Validate(
                value,
                nameof(value),
                "Artifact ID");
    }

    public string Value { get; }

    public override string ToString() => Value;
}

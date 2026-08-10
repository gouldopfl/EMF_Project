namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct DisabilityEvaluationId
{
    public DisabilityEvaluationId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Disability Evaluation ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

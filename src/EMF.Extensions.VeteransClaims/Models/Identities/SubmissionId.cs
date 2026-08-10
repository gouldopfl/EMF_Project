namespace EMF.Extensions.VeteransClaims.Models.Identities;

public readonly record struct SubmissionId
{
    public SubmissionId(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                "Submission ID cannot be empty.",
                nameof(value));
        }

        Value = value;
    }

    public string Value { get; }

    public override string ToString() => Value;
}

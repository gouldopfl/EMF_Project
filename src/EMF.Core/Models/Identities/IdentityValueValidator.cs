namespace EMF.Core.Models.Identities;

public static class IdentityValueValidator
{
    public static string Validate(
        string value,
        string parameterName,
        string displayName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(
                $"{displayName} cannot be empty.",
                parameterName);
        }

        foreach (var character in value)
        {
            if (char.IsControl(character))
            {
                throw new ArgumentException(
                    $"{displayName} cannot contain control characters.",
                    parameterName);
            }
        }

        return value;
    }
}

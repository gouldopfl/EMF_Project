using EMF.Security.Models.Identities;

namespace EMF.Intelligence.AzureOpenAI.Composition;

internal static class AzureOpenAICompositionValidator
{
    public static IReadOnlyList<
        ProtectionClassificationId> Validate(
        IEnumerable<ProtectionClassificationId>
            permittedClassifications)
    {
        ArgumentNullException.ThrowIfNull(
            permittedClassifications);

        var classifications =
            permittedClassifications.ToArray();

        if (classifications.Length == 0)
        {
            throw new ArgumentException(
                "At least one protection classification " +
                "must be configured.",
                nameof(permittedClassifications));
        }

        if (classifications.Any(
                value => string.IsNullOrWhiteSpace(
                    value.Value)) ||
            classifications.Distinct().Count() !=
                classifications.Length)
        {
            throw new ArgumentException(
                "Protection classifications must be " +
                "non-empty and unique.",
                nameof(permittedClassifications));
        }

        return classifications;
    }
}

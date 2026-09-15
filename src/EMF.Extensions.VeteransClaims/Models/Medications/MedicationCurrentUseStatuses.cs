namespace EMF.Extensions.VeteransClaims.Models.Medications;

public static class MedicationCurrentUseStatuses
{
    public const string CurrentlyUsed = "CurrentlyUsed";
    public const string NotCurrentlyUsed = "NotCurrentlyUsed";

    public static bool IsSupported(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        var value = status.Trim();

        return
            string.Equals(
                value,
                CurrentlyUsed,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                NotCurrentlyUsed,
                StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsCurrentlyUsed(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        return string.Equals(
            status.Trim(),
            CurrentlyUsed,
            StringComparison.OrdinalIgnoreCase);
    }
}

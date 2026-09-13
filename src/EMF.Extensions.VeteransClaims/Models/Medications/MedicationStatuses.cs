namespace EMF.Extensions.VeteransClaims.Models.Medications;

public static class MedicationStatuses
{
    public const string Active = "Active";
    public const string ActiveParked = "Active/Parked";
    public const string Pending = "Pending";
    public const string Suspended = "Suspended";
    public const string Hold = "Hold";
    public const string Discontinued = "Discontinued";
    public const string Expired = "Expired";

    public static bool IsCurrent(string status)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(status);

        var value = status.Trim();

        return
            string.Equals(
                value,
                Active,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                ActiveParked,
                StringComparison.OrdinalIgnoreCase);
    }
}

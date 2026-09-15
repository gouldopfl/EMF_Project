namespace EMF.Extensions.VeteransClaims.Models.Medications;

public static class MedicationLedgerStatuses
{
    public const string Active = "active";
    public const string RefillInProcess = "refillinprocess";
    public const string Transferred = "transferred";
    public const string Discontinued = "discontinued";
    public const string Expired = "expired";

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
                RefillInProcess,
                StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsKnown(string status)
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
                RefillInProcess,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                Transferred,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                Discontinued,
                StringComparison.OrdinalIgnoreCase) ||
            string.Equals(
                value,
                Expired,
                StringComparison.OrdinalIgnoreCase);
    }
}

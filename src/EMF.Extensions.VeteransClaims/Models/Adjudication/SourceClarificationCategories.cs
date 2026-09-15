namespace EMF.Extensions.VeteransClaims.Models.Adjudication;

public static class SourceClarificationCategories
{
    public const string ImpossibleMagnitude = "ImpossibleMagnitude";
    public const string BlankTemplate = "BlankTemplate";
    public const string MalformedValueOrUnit = "MalformedValueOrUnit";
    public const string InternalConflict = "InternalConflict";

    public static bool IsSupported(string value) =>
        string.Equals(value, ImpossibleMagnitude, StringComparison.Ordinal) ||
        string.Equals(value, BlankTemplate, StringComparison.Ordinal) ||
        string.Equals(value, MalformedValueOrUnit, StringComparison.Ordinal) ||
        string.Equals(value, InternalConflict, StringComparison.Ordinal);
}

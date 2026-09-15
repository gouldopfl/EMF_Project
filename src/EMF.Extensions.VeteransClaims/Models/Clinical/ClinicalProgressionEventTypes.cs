namespace EMF.Extensions.VeteransClaims.Models.Clinical;

public static class ClinicalProgressionEventTypes
{
    public const string TreatmentUse = "TreatmentUse";
    public const string TreatmentProblem = "TreatmentProblem";
    public const string TreatmentAdjustment = "TreatmentAdjustment";
    public const string DiagnosticFinding = "DiagnosticFinding";
    public const string TreatmentTransition = "TreatmentTransition";
    public const string TreatmentResponse = "TreatmentResponse";

    public static bool IsSupported(string value) =>
        string.Equals(value, TreatmentUse, StringComparison.Ordinal) ||
        string.Equals(value, TreatmentProblem, StringComparison.Ordinal) ||
        string.Equals(value, TreatmentAdjustment, StringComparison.Ordinal) ||
        string.Equals(value, DiagnosticFinding, StringComparison.Ordinal) ||
        string.Equals(value, TreatmentTransition, StringComparison.Ordinal) ||
        string.Equals(value, TreatmentResponse, StringComparison.Ordinal);
}

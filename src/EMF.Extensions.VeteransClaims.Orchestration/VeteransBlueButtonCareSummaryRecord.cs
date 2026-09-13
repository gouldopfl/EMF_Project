namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransBlueButtonCareSummaryRecord
{
    public required string Title
    { get; init; }

    public required string DateEntered
    { get; init; }

    public required int SourceStartPage
    { get; init; }

    public required int SourceEndPage
    { get; init; }

    public required IReadOnlyList<string> NoteTitles
    { get; init; }

    public required string Text
    { get; init; }
}

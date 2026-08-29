using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Capabilities;

public static class IntelligenceCapabilityIds
{
    public static IntelligenceCapabilityId
        TextSummarization
    {
        get;
    } = new("text.summarize");

    public static IntelligenceCapabilityId
        TextSegmentation
    {
        get;
    } = new("text.segment");

    public static IntelligenceCapabilityId
        TextKeywordExtraction
    {
        get;
    } = new("text.keywords.extract");

    public static IntelligenceCapabilityId
        TextStructuredExtraction
    {
        get;
    } = new("text.structured.extract");
}

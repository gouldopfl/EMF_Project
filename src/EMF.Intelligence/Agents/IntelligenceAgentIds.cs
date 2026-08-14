using EMF.Intelligence.Models.Identities;

namespace EMF.Intelligence.Agents;

public static class IntelligenceAgentIds
{
    public static AgentId TextSummarization
    {
        get;
    } = new("text.summarization");

    public static AgentId LongTextSummarization
    {
        get;
    } = new("text.summarization.long");
}

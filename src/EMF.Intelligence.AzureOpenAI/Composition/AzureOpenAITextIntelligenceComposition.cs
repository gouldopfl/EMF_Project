using EMF.Intelligence.Agents;
using EMF.Intelligence.AzureOpenAI.Configuration;
using EMF.Intelligence.AzureOpenAI.Providers;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Routing;
using EMF.Security.Auditing;
using EMF.Security.Authorization;
using EMF.Security.Models.Identities;

namespace EMF.Intelligence.AzureOpenAI.Composition;

public sealed class
    AzureOpenAITextIntelligenceComposition
{
    public AzureOpenAITextIntelligenceComposition(
        AzureOpenAIOptions options,
        IAuthorizationPolicy authorizationPolicy,
        ISecurityAuditSink auditSink,
        IEnumerable<ProtectionClassificationId>
            permittedClassifications)
        : this(
            new AzureOpenAITextSummarizationProvider(
                options),
            authorizationPolicy,
            auditSink,
            permittedClassifications)
    {
    }

    internal AzureOpenAITextIntelligenceComposition(
        IIntelligenceCapabilityProvider<
            TextSummarizationRequest,
            string> provider,
        IAuthorizationPolicy authorizationPolicy,
        ISecurityAuditSink auditSink,
        IEnumerable<ProtectionClassificationId>
            permittedClassifications)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(
            authorizationPolicy);
        ArgumentNullException.ThrowIfNull(auditSink);

        if (provider.Id !=
            IntelligenceCapabilityIds
                .TextSummarization)
        {
            throw new ArgumentException(
                "The provider must implement text " +
                "summarization.",
                nameof(provider));
        }

        var classifications =
            AzureOpenAICompositionValidator.Validate(
                permittedClassifications);

        var grants =
            classifications.Select(
                classification =>
                    new IntelligenceProviderRoutingGrant(
                        provider.ProviderId,
                        provider.Id,
                        classification));

        var routingPolicy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                grants);

        var capabilityExecutor =
            new IntelligenceCapabilityExecutor<
                TextSummarizationRequest,
                string>(
                new IntelligenceCapabilityProviderRouter<
                    TextSummarizationRequest,
                    string>(
                    [provider],
                    routingPolicy),
                authorizationPolicy,
                auditSink);

        TextSummarizationCapabilityExecutor =
            capabilityExecutor;

        var agent =
            new TextSummarizationAgent(
                capabilityExecutor);

        TextSummarizationAgentExecutor =
            new IntelligenceAgentExecutor<
                TextSummarizationRequest,
                string>(
                new IntelligenceAgentRegistry<
                    TextSummarizationRequest,
                    string>([agent]),
                auditSink);
    }

    public IIntelligenceCapabilityExecutor<
        TextSummarizationRequest,
        string>
        TextSummarizationCapabilityExecutor
    { get; }

    public IIntelligenceAgentExecutor<
        TextSummarizationRequest,
        string>
        TextSummarizationAgentExecutor
    { get; }
}

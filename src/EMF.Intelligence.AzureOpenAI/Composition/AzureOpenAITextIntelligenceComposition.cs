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
            new AzureOpenAITextStructuredExtractionProvider(
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
        IIntelligenceCapabilityProvider<
            TextStructuredExtractionRequest,
            string> structuredProvider,
        IAuthorizationPolicy authorizationPolicy,
        ISecurityAuditSink auditSink,
        IEnumerable<ProtectionClassificationId>
            permittedClassifications)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(
            structuredProvider);
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
            classifications
                .SelectMany(
                    classification =>
                        new[]
                        {
                            new IntelligenceProviderRoutingGrant(
                                provider.ProviderId,
                                provider.Id,
                                classification),
                            new IntelligenceProviderRoutingGrant(
                                structuredProvider.ProviderId,
                                structuredProvider.Id,
                                classification)
                        });

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

        TextStructuredExtractionCapabilityExecutor =
            new IntelligenceCapabilityExecutor<
                TextStructuredExtractionRequest,
                string>(
                new IntelligenceCapabilityProviderRouter<
                    TextStructuredExtractionRequest,
                    string>(
                    [structuredProvider],
                    routingPolicy),
                authorizationPolicy,
                auditSink);

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

    public IIntelligenceCapabilityExecutor<
        TextStructuredExtractionRequest,
        string>
        TextStructuredExtractionCapabilityExecutor
    { get; }

    public IIntelligenceAgentExecutor<
        TextSummarizationRequest,
        string>
        TextSummarizationAgentExecutor
    { get; }
}

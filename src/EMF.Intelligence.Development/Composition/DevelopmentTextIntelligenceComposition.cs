using EMF.Intelligence.Agents;
using EMF.Intelligence.Capabilities;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Development.Providers;
using EMF.Intelligence.Execution;
using EMF.Intelligence.Routing;
using EMF.Security.Auditing;
using EMF.Security.Authorization;
using EMF.Security.Models.Identities;

namespace EMF.Intelligence.Development.Composition;

public sealed class
    DevelopmentTextIntelligenceComposition
{
    public DevelopmentTextIntelligenceComposition(
        IAuthorizationPolicy authorizationPolicy,
        ISecurityAuditSink auditSink,
        IEnumerable<ProtectionClassificationId>
            permittedClassifications)
    {
        ArgumentNullException.ThrowIfNull(
            authorizationPolicy);
        ArgumentNullException.ThrowIfNull(auditSink);
        ArgumentNullException.ThrowIfNull(
            permittedClassifications);

        var classifications =
            permittedClassifications.ToArray();

        if (classifications.Length == 0)
        {
            throw new ArgumentException(
                "At least one protection classification " +
                "must be configured.",
                nameof(permittedClassifications));
        }

        if (classifications.Any(
                classification =>
                    string.IsNullOrWhiteSpace(
                        classification.Value)))
        {
            throw new ArgumentException(
                "Protection classifications cannot " +
                "be empty.",
                nameof(permittedClassifications));
        }

        if (classifications.Distinct().Count() !=
            classifications.Length)
        {
            throw new ArgumentException(
                "Protection classifications must " +
                "be unique.",
                nameof(permittedClassifications));
        }

        var summarizationProvider =
            new DevelopmentTextSummarizationProvider();

        var segmentationProvider =
            new DevelopmentTextSegmentationProvider();

        var keywordProvider =
            new DevelopmentTextKeywordExtractionProvider();

        var grants =
            new List<IntelligenceProviderRoutingGrant>();

        foreach (var classification in classifications)
        {
            grants.Add(
                new IntelligenceProviderRoutingGrant(
                    summarizationProvider.ProviderId,
                    summarizationProvider.Id,
                    classification));

            grants.Add(
                new IntelligenceProviderRoutingGrant(
                    segmentationProvider.ProviderId,
                    segmentationProvider.Id,
                    classification));

            grants.Add(
                new IntelligenceProviderRoutingGrant(
                    keywordProvider.ProviderId,
                    keywordProvider.Id,
                    classification));
        }

        var routingPolicy =
            new ConfiguredIntelligenceProviderRoutingPolicy(
                grants);

        var summarizationExecutor =
            new IntelligenceCapabilityExecutor<
                TextSummarizationRequest,
                string>(
                new IntelligenceCapabilityProviderRouter<
                    TextSummarizationRequest,
                    string>(
                    [summarizationProvider],
                    routingPolicy),
                authorizationPolicy,
                auditSink);

        var segmentationExecutor =
            new IntelligenceCapabilityExecutor<
                TextSegmentationRequest,
                IReadOnlyList<TextSegment>>(
                new IntelligenceCapabilityProviderRouter<
                    TextSegmentationRequest,
                    IReadOnlyList<TextSegment>>(
                    [segmentationProvider],
                    routingPolicy),
                authorizationPolicy,
                auditSink);

        var keywordExecutor =
            new IntelligenceCapabilityExecutor<
                TextKeywordExtractionRequest,
                IReadOnlyList<TextKeyword>>(
                new IntelligenceCapabilityProviderRouter<
                    TextKeywordExtractionRequest,
                    IReadOnlyList<TextKeyword>>(
                    [keywordProvider],
                    routingPolicy),
                authorizationPolicy,
                auditSink);

        TextSummarizationCapabilityExecutor =
            summarizationExecutor;
        TextSegmentationCapabilityExecutor =
            segmentationExecutor;
        TextKeywordExtractionCapabilityExecutor =
            keywordExecutor;

        var textSummarizationAgent =
            new TextSummarizationAgent(
                summarizationExecutor);

        TextSummarizationAgentExecutor =
            new IntelligenceAgentExecutor<
                TextSummarizationRequest,
                string>(
                new IntelligenceAgentRegistry<
                    TextSummarizationRequest,
                    string>(
                    [textSummarizationAgent]),
                auditSink);

        var longTextSummarizationAgent =
            new LongTextSummarizationAgent(
                segmentationExecutor,
                summarizationExecutor);

        LongTextSummarizationAgentExecutor =
            new IntelligenceAgentExecutor<
                LongTextSummarizationRequest,
                string>(
                new IntelligenceAgentRegistry<
                    LongTextSummarizationRequest,
                    string>(
                    [longTextSummarizationAgent]),
                auditSink);

        var textInsightAgent =
            new TextInsightAgent(
                summarizationExecutor,
                keywordExecutor);

        TextInsightAgentExecutor =
            new IntelligenceAgentExecutor<
                TextInsightRequest,
                TextInsight>(
                new IntelligenceAgentRegistry<
                    TextInsightRequest,
                    TextInsight>(
                    [textInsightAgent]),
                auditSink);

        var longTextInsightAgent =
            new LongTextInsightAgent(
                segmentationExecutor,
                summarizationExecutor,
                keywordExecutor);

        LongTextInsightAgentExecutor =
            new IntelligenceAgentExecutor<
                LongTextInsightRequest,
                TextInsight>(
                new IntelligenceAgentRegistry<
                    LongTextInsightRequest,
                    TextInsight>(
                    [longTextInsightAgent]),
                auditSink);
    }

    public IIntelligenceCapabilityExecutor<
        TextSummarizationRequest,
        string>
        TextSummarizationCapabilityExecutor
    { get; }

    public IIntelligenceCapabilityExecutor<
        TextSegmentationRequest,
        IReadOnlyList<TextSegment>>
        TextSegmentationCapabilityExecutor
    { get; }

    public IIntelligenceCapabilityExecutor<
        TextKeywordExtractionRequest,
        IReadOnlyList<TextKeyword>>
        TextKeywordExtractionCapabilityExecutor
    { get; }

    public IIntelligenceAgentExecutor<
        TextSummarizationRequest,
        string>
        TextSummarizationAgentExecutor
    { get; }

    public IIntelligenceAgentExecutor<
        LongTextSummarizationRequest,
        string>
        LongTextSummarizationAgentExecutor
    { get; }

    public IIntelligenceAgentExecutor<
        TextInsightRequest,
        TextInsight>
        TextInsightAgentExecutor
    { get; }

    public IIntelligenceAgentExecutor<
        LongTextInsightRequest,
        TextInsight>
        LongTextInsightAgentExecutor
    { get; }
}

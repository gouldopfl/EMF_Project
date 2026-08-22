using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;
using EMF.Intelligence.State;

namespace EMF.Tests;

public sealed class StatefulIntelligenceAgentContractTests
{
    [Fact]
    public void Agent_ExposesStateAwareExecutionOperation()
    {
        var contract =
            typeof(
                IStatefulIntelligenceAgent<
                    string,
                    string>);

        Assert.Contains(
            typeof(IStatefulIntelligenceAgent),
            contract.GetInterfaces());

        var baseContract =
            typeof(IStatefulIntelligenceAgent);

        var identifier =
            baseContract.GetProperty(
                nameof(
                    IStatefulIntelligenceAgent.Id));

        Assert.NotNull(identifier);
        Assert.Equal(
            typeof(AgentId),
            identifier!.PropertyType);

        var supportedVersion =
            baseContract.GetProperty(
                nameof(
                    IStatefulIntelligenceAgent
                        .SupportedStateVersion));

        Assert.NotNull(supportedVersion);
        Assert.Equal(
            typeof(int),
            supportedVersion!.PropertyType);

        var method =
            contract.GetMethod(
                nameof(
                    IStatefulIntelligenceAgent<
                        string,
                        string>.ExecuteAsync));

        Assert.NotNull(method);

        Assert.Equal(
            typeof(
                Task<
                    StatefulIntelligenceAgentResult<
                        string>>),
            method!.ReturnType);

        var parameters = method.GetParameters();

        Assert.Equal(4, parameters.Length);

        Assert.Equal(
            typeof(string),
            parameters[0].ParameterType);

        Assert.Equal(
            typeof(IntelligenceExecutionContext),
            parameters[1].ParameterType);

        Assert.Equal(
            typeof(IntelligenceAgentState),
            parameters[2].ParameterType);

        Assert.Equal(
            typeof(CancellationToken),
            parameters[3].ParameterType);
    }
}

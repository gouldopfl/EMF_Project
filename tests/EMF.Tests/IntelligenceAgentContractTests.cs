using EMF.Intelligence.Agents;
using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Tests;

public sealed class IntelligenceAgentContractTests
{
    [Fact]
    public void Agent_ExposesTypedObjectiveOperation()
    {
        var contract =
            typeof(
                IIntelligenceAgent<
                    string,
                    string>);

        var identifier =
            contract.GetProperty(
                nameof(
                    IIntelligenceAgent<
                        string,
                        string>.Id));

        Assert.NotNull(identifier);
        Assert.Equal(
            typeof(AgentId),
            identifier!.PropertyType);

        var method =
            contract.GetMethod(
                nameof(
                    IIntelligenceAgent<
                        string,
                        string>.ExecuteAsync));

        Assert.NotNull(method);
        Assert.Equal(
            typeof(
                Task<
                    IntelligenceAgentResult<
                        string>>),
            method!.ReturnType);

        var parameters =
            method.GetParameters();

        Assert.Equal(3, parameters.Length);
        Assert.Equal(
            typeof(string),
            parameters[0].ParameterType);
        Assert.Equal(
            typeof(IntelligenceExecutionContext),
            parameters[1].ParameterType);
        Assert.Equal(
            typeof(CancellationToken),
            parameters[2].ParameterType);
    }
}

using EMF.Intelligence.Agents;
using EMF.Security.Auditing;

namespace EMF.Tests;

public sealed partial class
    IntelligenceAgentExecutorTests
{
    [Fact]
    public void Constructor_RequiresNoAgentPersistence()
    {
        var constructor =
            typeof(
                IntelligenceAgentExecutor<
                    string,
                    string>)
            .GetConstructors()
            .Single();

        var parameters =
            constructor.GetParameters();

        Assert.Equal(2, parameters.Length);

        Assert.Equal(
            typeof(
                IntelligenceAgentRegistry<
                    string,
                    string>),
            parameters[0].ParameterType);

        Assert.Equal(
            typeof(ISecurityAuditSink),
            parameters[1].ParameterType);
    }
}

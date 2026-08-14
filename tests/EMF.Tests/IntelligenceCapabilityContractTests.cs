using EMF.Intelligence.Contracts;
using EMF.Intelligence.Models;
using EMF.Intelligence.Models.Identities;

namespace EMF.Tests;

public sealed class IntelligenceCapabilityContractTests
{
    [Fact]
    public void Capability_ExposesTypedExecutionOperation()
    {
        var contract =
            typeof(IIntelligenceCapability<,>);

        var identifier =
            contract.GetProperty(
                nameof(
                    IIntelligenceCapability<
                        object,
                        object>.Id));

        var method =
            contract.GetMethod(
                nameof(
                    IIntelligenceCapability<
                        object,
                        object>.ExecuteAsync));

        Assert.NotNull(identifier);
        Assert.Equal(
            typeof(IntelligenceCapabilityId),
            identifier!.PropertyType);

        Assert.NotNull(method);
        Assert.Equal(
            typeof(Task<>),
            method!.ReturnType
                .GetGenericTypeDefinition());

        var resultType =
            method.ReturnType
                .GetGenericArguments()[0];

        Assert.Equal(
            typeof(IntelligenceCapabilityResult<>),
            resultType.GetGenericTypeDefinition());

        Assert.True(
            resultType.GetGenericArguments()[0]
                .IsGenericParameter);

        var parameters =
            method.GetParameters();

        Assert.Equal(3, parameters.Length);
        Assert.True(
            parameters[0].ParameterType
                .IsGenericParameter);

        Assert.Equal(
            typeof(IntelligenceExecutionContext),
            parameters[1].ParameterType);

        Assert.Equal(
            typeof(CancellationToken),
            parameters[2].ParameterType);
    }
}

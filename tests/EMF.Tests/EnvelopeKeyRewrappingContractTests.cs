using EMF.Security.Encryption.Envelope;

namespace EMF.Tests;

public sealed class EnvelopeKeyRewrappingContractTests
{
    [Fact]
    public void RewrappingService_ExposesRewrapOperation()
    {
        var method =
            typeof(IEnvelopeKeyRewrappingService)
                .GetMethod(
                    nameof(
                        IEnvelopeKeyRewrappingService
                            .RewrapAsync));

        Assert.NotNull(method);
        Assert.Equal(
            typeof(Task<>),
            method!.ReturnType
                .GetGenericTypeDefinition());
    }
}

using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Tests;

public sealed class EnvelopeEncryptionContractTests
{
    [Fact]
    public void EnvelopeEncryptionService_ExposesEncryptOperation()
    {
        var method =
            typeof(IEnvelopeEncryptionService)
                .GetMethod(
                    nameof(IEnvelopeEncryptionService.EncryptAsync));

        Assert.NotNull(method);
    }

    [Fact]
    public void EnvelopeEncryptionService_ExposesDecryptOperation()
    {
        var method =
            typeof(IEnvelopeEncryptionService)
                .GetMethod(
                    nameof(IEnvelopeEncryptionService.DecryptAsync));

        Assert.NotNull(method);
    }

    [Fact]
    public void EncryptedEnvelope_ContainsRequiredEnvelopeMetadata()
    {
        var properties =
            typeof(EncryptedEnvelope)
                .GetProperties();

        Assert.Contains(
            properties,
            property => property.Name == nameof(
                EncryptedEnvelope.WrappedDataEncryptionKey));

        Assert.Contains(
            properties,
            property => property.Name == nameof(
                EncryptedEnvelope.KeyEncryptionKeyId));

        Assert.Contains(
            properties,
            property => property.Name == nameof(
                EncryptedEnvelope.Algorithm));

        Assert.Contains(
            properties,
            property => property.Name == nameof(
                EncryptedEnvelope.Nonce));

        Assert.Contains(
            properties,
            property => property.Name == nameof(
                EncryptedEnvelope.AuthenticationTag));
    }
}

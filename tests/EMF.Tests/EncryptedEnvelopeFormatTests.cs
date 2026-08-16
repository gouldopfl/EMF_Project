using System.Security.Cryptography;
using EMF.Security.Encryption.Envelope;

namespace EMF.Tests;

public sealed class EncryptedEnvelopeFormatTests
{
    [Fact]
    public void CurrentVersion_ProducesStableAuthenticatedData()
    {
        var first =
            EncryptedEnvelopeFormat.GetAuthenticatedData(
                EncryptedEnvelopeFormat.CurrentVersion,
                EncryptedEnvelopeFormat.Aes256GcmAlgorithm);

        var second =
            EncryptedEnvelopeFormat.GetAuthenticatedData(
                EncryptedEnvelopeFormat.CurrentVersion,
                EncryptedEnvelopeFormat.Aes256GcmAlgorithm);

        Assert.NotEmpty(first);
        Assert.Equal(first, second);
    }

    [Fact]
    public void LegacyVersion_UsesNoAuthenticatedData()
    {
        var data =
            EncryptedEnvelopeFormat.GetAuthenticatedData(
                EncryptedEnvelopeFormat.LegacyVersion,
                EncryptedEnvelopeFormat.Aes256GcmAlgorithm);

        Assert.Empty(data);
    }

    [Fact]
    public void UnknownVersion_IsRejected()
    {
        Assert.Throws<CryptographicException>(
            () =>
                EncryptedEnvelopeFormat.GetAuthenticatedData(
                    2,
                    EncryptedEnvelopeFormat
                        .Aes256GcmAlgorithm));
    }

    [Fact]
    public void UnknownAlgorithm_IsRejected()
    {
        Assert.Throws<CryptographicException>(
            () =>
                EncryptedEnvelopeFormat.GetAuthenticatedData(
                    EncryptedEnvelopeFormat.CurrentVersion,
                    "AES-128-GCM"));
    }
}

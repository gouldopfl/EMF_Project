using System.Security.Cryptography;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;

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

    [Fact]
    public void Validate_AcceptsValidStructure()
    {
        var envelope = CreateEnvelope(
            hasCiphertext: true,
            nonceLength: 12,
            tagLength: 16,
            wrappedKeyLength: 32,
            keyId: "key/v1");

        EncryptedEnvelopeFormat.Validate(envelope);
    }

    [Theory]
    [InlineData(false, 12, 16, 32, "key/v1")]
    [InlineData(true, 11, 16, 32, "key/v1")]
    [InlineData(true, 12, 15, 32, "key/v1")]
    [InlineData(true, 12, 16, 0, "key/v1")]
    [InlineData(true, 12, 16, 32, "")]
    [InlineData(true, 12, 16, 32, null)]
    public void Validate_RejectsInvalidStructure(
        bool hasCiphertext,
        int nonceLength,
        int tagLength,
        int wrappedKeyLength,
        string? keyId)
    {
        var envelope = CreateEnvelope(
            hasCiphertext,
            nonceLength,
            tagLength,
            wrappedKeyLength,
            keyId);

        Assert.Throws<CryptographicException>(
            () => EncryptedEnvelopeFormat.Validate(
                envelope));
    }

    private static EncryptedEnvelope CreateEnvelope(
        bool hasCiphertext,
        int nonceLength,
        int tagLength,
        int wrappedKeyLength,
        string? keyId)
    {
        return new EncryptedEnvelope
        {
            FormatVersion =
                EncryptedEnvelopeFormat.CurrentVersion,
            Ciphertext =
                hasCiphertext
                    ? [1]
                    : null!,
            Nonce = new byte[nonceLength],
            AuthenticationTag = new byte[tagLength],
            WrappedDataEncryptionKey =
                new byte[wrappedKeyLength],
            KeyEncryptionKeyId = keyId!,
            Algorithm =
                EncryptedEnvelopeFormat
                    .Aes256GcmAlgorithm
        };
    }
}

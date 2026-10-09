using System.Text;
using System.Text.Json;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
namespace EMF.Tests;

public sealed class BoundedEncryptedEnvelopeCodecTests
{
    internal static EncryptedEnvelope Envelope(int length = 3) => new()
    {
        FormatVersion = 2, Ciphertext = Enumerable.Repeat((byte)7, length).ToArray(), Nonce = new byte[12],
        AuthenticationTag = new byte[16], WrappedDataEncryptionKey = new byte[] { 1 },
        KeyEncryptionKeyId = "key-001", Algorithm = "AES-256-GCM"
    };
    private static string Json() => Encoding.UTF8.GetString(BoundedEncryptedEnvelopeCodec.Write(Envelope(), new(3)));
    [Fact]
    public void ExactWriterAndReaderPreserveSchema()
    {
        var envelope = Envelope();
        var bytes = BoundedEncryptedEnvelopeCodec.Write(envelope, new(3));
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(envelope), bytes);
        Assert.Equal(149 + 4 + 16 + 24 + 4 + 7, bytes.Length);
        var decoded = BoundedEncryptedEnvelopeCodec.Read(bytes, new(3));
        Assert.Equal(envelope.Ciphertext, decoded.Ciphertext);
        Assert.Equal(envelope.WrappedDataEncryptionKey, decoded.WrappedDataEncryptionKey);
        Assert.Equal(envelope.KeyEncryptionKeyId, decoded.KeyEncryptionKeyId);
    }
    [Theory]
    [InlineData("missing")] [InlineData("duplicate")] [InlineData("unknown")]
    [InlineData("nested")] [InlineData("trailing")] [InlineData("format")]
    [InlineData("escaped-name")] [InlineData("escaped-binary")] [InlineData("algorithm")]
    [InlineData("nonce")] [InlineData("tag")] [InlineData("ciphertext")]
    [InlineData("padding")] [InlineData("pad-bits")] [InlineData("pad-bits-one")]
    [InlineData("whitespace")] [InlineData("malformed")] [InlineData("key-utf8")]
    [InlineData("key-escaped")] [InlineData("wrapped")] [InlineData("key-surrogate")]
    public void InvalidEnvelopePreflightAllocatesNoBinaryPayload(string kind)
    {
        var json = Json();
        json = kind switch
        {
            "missing" => json.Replace("\"FormatVersion\":2,", ""),
            "duplicate" => json.Replace("{", "{\"FormatVersion\":2,"),
            "unknown" => json.Replace("{", "{\"Other\":0,"),
            "nested" => json.Replace("\"Ciphertext\":\"BwcH\"", "\"Ciphertext\":{}"),
            "trailing" => json + " {}", "format" => json.Replace(":2,", ":2.0,"),
            "escaped-name" => json.Replace("FormatVersion", "Format\\u0056ersion"),
            "escaped-binary" => json.Replace("BwcH", "Bwc\\u0048"),
            "algorithm" => json.Replace("AES-256-GCM", "AES-128-GCM"),
            "nonce" => json.Replace(Convert.ToBase64String(new byte[12]), "AAAA"),
            "tag" => json.Replace(Convert.ToBase64String(new byte[16]), "AAAA"),
            "ciphertext" => json.Replace("BwcH", "AAAAAAA="),
            "padding" => json.Replace("BwcH", "A==="),
            "pad-bits" => json.Replace("BwcH", "/x=="),
            "pad-bits-one" => json.Replace("BwcH", "AAB="),
            "whitespace" => json.Replace("BwcH", "AA A"),
            "malformed" => json[..^1],
            "key-utf8" => json.Replace("key-001", new string('é', 513)),
            "key-escaped" => json.Replace("key-001", string.Concat(Enumerable.Repeat("\\u0041", 1025))),
            "wrapped" => json.Replace("\"WrappedDataEncryptionKey\":\"AQ==\"", "\"WrappedDataEncryptionKey\":\"" + Convert.ToBase64String(new byte[16385]) + "\""),
            "key-surrogate" => json.Replace("key-001", "\\uD800"),
            _ => throw new Exception()
        };
        int allocated = 0;
        Assert.ThrowsAny<Exception>(() => BoundedEncryptedEnvelopeCodec.Read(Encoding.UTF8.GetBytes(json), new(3), default,
            (_, _) => allocated++));
        Assert.Equal(0, allocated);
    }
    [Fact]
    public void DecodedCeilingCheckedIndependentlyOfEncodedCeiling()
    {
        // Five decoded bytes and four allowed bytes both have eight encoded bytes.
        var bytes = Encoding.UTF8.GetBytes(Json().Replace("BwcH", "AAAAAAA="));
        int allocations = 0;
        Assert.Throws<InvalidDataException>(() => BoundedEncryptedEnvelopeCodec.Read(bytes, new(4), default, (_, _) => allocations++));
        Assert.Equal(0, allocations);
    }
    [Fact]
    public void PartialDecodeFailureClearsAllAllocatedArrays()
    {
        var captured = new List<byte[]>();
        Assert.Throws<IOException>(() => BoundedEncryptedEnvelopeCodec.Read(Encoding.UTF8.GetBytes(Json()), new(3), default,
            (_, bytes) => { captured.Add(bytes); if (captured.Count == 2) throw new IOException("Injected decode failure"); }));
        Assert.Equal(2, captured.Count);
        foreach (var bytes in captured) Assert.All(bytes, b => Assert.Equal(0, b));
    }
    [Fact]
    public void CancellationAfterDecodeAllocationClearsBuffers()
    {
        using var cts = new CancellationTokenSource(); byte[]? captured = null;
        Assert.ThrowsAny<OperationCanceledException>(() => BoundedEncryptedEnvelopeCodec.Read(Encoding.UTF8.GetBytes(Json()), new(3), cts.Token,
            (_, bytes) => { captured = bytes; cts.Cancel(); }));
        Assert.NotNull(captured); Assert.All(captured!, b => Assert.Equal(0, b));
    }
    [Fact]
    public void FailedWriterClearsItsExactDestination()
    {
        byte[]? captured = null;
        Assert.Throws<IOException>(() => BoundedEncryptedEnvelopeCodec.Write(Envelope(), new(3), default,
            (_, bytes) => { captured = bytes; Array.Fill(bytes, (byte)7); throw new IOException(); }));
        Assert.NotNull(captured); Assert.All(captured!, b => Assert.Equal(0, b));
    }
    [Fact]
    public void WriterValidatesCeilingBeforeAllocationAndArithmeticIsChecked()
    {
        int allocations = 0;
        Assert.Throws<InvalidDataException>(() => BoundedEncryptedEnvelopeCodec.Write(Envelope(4), new(3), default, (_, _) => allocations++));
        Assert.Equal(0, allocations);
        Assert.Throws<OverflowException>(() => BoundedEncryptedEnvelopeCodec.Base64Length(long.MaxValue));
        Assert.Throws<OverflowException>(() => BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(long.MaxValue / 4 * 3));
        Assert.Throws<ArgumentOutOfRangeException>(() => new EnvelopeDecryptionLimits(long.MaxValue));
    }
    [Fact]
    public void LargestMetadataAndEscapedKeyRoundTripWithinExactCapacity()
    {
        var original = Envelope(1);
        var envelope = new EncryptedEnvelope { FormatVersion = 2, Ciphertext = original.Ciphertext, Nonce = original.Nonce,
            AuthenticationTag = original.AuthenticationTag, WrappedDataEncryptionKey = new byte[16384],
            KeyEncryptionKeyId = new string('\'', 1024), Algorithm = original.Algorithm };
        var serialized = BoundedEncryptedEnvelopeCodec.Write(envelope, new(1));
        Assert.Equal(envelope.WrappedDataEncryptionKey, BoundedEncryptedEnvelopeCodec.Read(serialized, new(1)).WrappedDataEncryptionKey);
        Assert.Equal(BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(1), serialized.Length);
        Assert.Equal(JsonSerializer.SerializeToUtf8Bytes(envelope), serialized);
        Assert.Equal(envelope.KeyEncryptionKeyId, BoundedEncryptedEnvelopeCodec.Read(serialized, new(1)).KeyEncryptionKeyId);
        using var document = JsonDocument.Parse(serialized);
        Assert.Equal(6144 + 2, document.RootElement.GetProperty("KeyEncryptionKeyId").GetRawText().Length);
    }
}

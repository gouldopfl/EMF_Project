using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Security.Encryption.Envelope.Models;

namespace EMF.Security.Encryption.Envelope;

/// <summary>Fixed format-2 codec. Large destinations are allocated only after complete preflight.</summary>
public static class BoundedEncryptedEnvelopeCodec
{
    public const long FixedMaximumOverheadBytes = 28181;
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private static readonly string[] Names = ["FormatVersion", "Ciphertext", "Nonce", "AuthenticationTag",
        "WrappedDataEncryptionKey", "KeyEncryptionKeyId", "Algorithm"];
    public static long Base64Length(long length)
    {
        if (length < 0) throw new ArgumentOutOfRangeException(nameof(length));
        return checked(4L * ((length + 2L) / 3L));
    }
    public static long MaximumProtectedBytes(long maximumPlaintextBytes) =>
        checked(Base64Length(maximumPlaintextBytes) + FixedMaximumOverheadBytes);

    public static EncryptedEnvelope Read(ReadOnlySpan<byte> source, EnvelopeDecryptionLimits limits,
        CancellationToken cancellationToken = default) => Read(source, limits, cancellationToken, null);

    private readonly record struct Field(int Start, int Length, int DecodedLength);
    internal static EncryptedEnvelope Read(ReadOnlySpan<byte> source, EnvelopeDecryptionLimits limits,
        CancellationToken ct, Action<string, byte[]>? allocated)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ct.ThrowIfCancellationRequested();
        if (source.Length > MaximumProtectedBytes(limits.MaximumPlaintextBytes))
            throw new InvalidDataException("Protected envelope exceeds its representation ceiling.");
        var fields = new Field[7]; // Fixed metadata only; never proportional to payload size.
        var reader = new Utf8JsonReader(source, new JsonReaderOptions { MaxDepth = 1 });
        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject) throw Invalid();
        int seen = 0, tokens = 1;
        string? keyId = null;
        while (reader.Read())
        {
            ct.ThrowIfCancellationRequested();
            tokens++;
            if (reader.TokenType == JsonTokenType.EndObject) break;
            if (reader.TokenType != JsonTokenType.PropertyName || reader.ValueIsEscaped || reader.ValueSpan.Length > 24)
                throw Invalid();
            var index = -1;
            for (int i = 0; i < Names.Length; i++) if (reader.ValueTextEquals(Names[i])) { index = i; break; }
            if (index < 0 || (seen & (1 << index)) != 0) throw Invalid();
            seen |= 1 << index;
            if (!reader.Read()) throw Invalid();
            tokens++;
            if (index == 0)
            {
                if (reader.TokenType != JsonTokenType.Number || !reader.ValueSpan.SequenceEqual("2"u8)) throw Invalid();
                continue;
            }
            if (reader.TokenType != JsonTokenType.String) throw Invalid();
            var raw = reader.ValueSpan;
            if (index == 6)
            {
                if (reader.ValueIsEscaped || !raw.SequenceEqual("AES-256-GCM"u8)) throw Invalid();
            }
            else if (index == 5)
            {
                if (raw.Length > 6144) throw Invalid();
                // Both raw UTF-8 and decoded Unicode must be valid; metadata allocation is capped.
                _ = StrictUtf8.GetCharCount(raw);
                keyId = reader.GetString() ?? throw Invalid();
                if (string.IsNullOrWhiteSpace(keyId) || keyId.Any(char.IsControl) || StrictUtf8.GetByteCount(keyId) > 1024)
                    throw Invalid();
            }
            else
            {
                if (reader.ValueIsEscaped) throw Invalid();
                var maximum = index switch { 1 => limits.MaximumPlaintextBytes, 2 => 12, 3 => 16, _ => 16384 };
                if (raw.Length > Base64Length(maximum)) throw Invalid();
                int decoded = CanonicalDecodedLength(raw, ct);
                if (decoded > maximum || (index == 2 && decoded != 12) ||
                    (index == 3 && decoded != 16) || (index == 4 && decoded == 0)) throw Invalid();
                fields[index] = new(checked((int)reader.TokenStartIndex + 1), raw.Length, decoded);
            }
        }
        if (reader.TokenType != JsonTokenType.EndObject || seen != 127 || tokens != 16 || reader.Read()) throw Invalid();
        // Second pass: schema, all encoded ceilings and decoded sizes are already established.
        var owned = new byte[4][];
        try
        {
            for (int i = 1; i <= 4; i++)
            {
                ct.ThrowIfCancellationRequested();
                var field = fields[i];
                var bytes = new byte[field.DecodedLength];
                owned[i - 1] = bytes;
                allocated?.Invoke(Names[i], bytes);
                if (Base64.DecodeFromUtf8(source.Slice(field.Start, field.Length), bytes,
                    out var consumed, out var written) != OperationStatus.Done || consumed != field.Length || written != bytes.Length)
                    throw Invalid();
            }
            ct.ThrowIfCancellationRequested();
            return new EncryptedEnvelope { FormatVersion = 2, Ciphertext = owned[0], Nonce = owned[1],
                AuthenticationTag = owned[2], WrappedDataEncryptionKey = owned[3],
                KeyEncryptionKeyId = keyId!, Algorithm = "AES-256-GCM" };
        }
        catch { foreach (var bytes in owned) if (bytes is not null) CryptographicOperations.ZeroMemory(bytes); throw; }
    }
    private static InvalidDataException Invalid() => new("Invalid bounded format-2 envelope.");
    private static int Sextet(byte c) => c is >= (byte)'A' and <= (byte)'Z' ? c - 'A' :
        c is >= (byte)'a' and <= (byte)'z' ? c - 'a' + 26 :
        c is >= (byte)'0' and <= (byte)'9' ? c - '0' + 52 : c == '+' ? 62 : c == '/' ? 63 : -1;
    private static int CanonicalDecodedLength(ReadOnlySpan<byte> value, CancellationToken ct)
    {
        if (value.Length % 4 != 0) throw Invalid();
        if (value.IsEmpty) return 0;
        var padding = value[^1] == '=' ? (value[^2] == '=' ? 2 : 1) : 0;
        var dataLength = value.Length - padding;
        for (int i = 0; i < dataLength; i++)
        {
            if ((i & 4095) == 0) ct.ThrowIfCancellationRequested();
            if (Sextet(value[i]) < 0) throw Invalid();
        }
        if ((padding == 1 && (Sextet(value[dataLength - 1]) & 3) != 0) ||
            (padding == 2 && (Sextet(value[dataLength - 1]) & 15) != 0)) throw Invalid();
        return checked((value.Length / 4) * 3 - padding);
    }
    public static byte[] Write(EncryptedEnvelope envelope, EnvelopeDecryptionLimits limits,
        CancellationToken cancellationToken = default) => Write(envelope, limits, cancellationToken, null);

    internal static byte[] Write(EncryptedEnvelope envelope, EnvelopeDecryptionLimits limits,
        CancellationToken ct, Action<string, byte[]>? allocated)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ct.ThrowIfCancellationRequested();
        EncryptedEnvelopeFormat.Validate(envelope);
        if (envelope.FormatVersion != 2 || envelope.Ciphertext.Length > limits.MaximumPlaintextBytes)
            throw Invalid();
        // JsonEncodedText owns only bounded key metadata, never a payload-sized serialization buffer.
        var encodedKey = JsonEncodedText.Encode(envelope.KeyEncryptionKeyId);
        var key = encodedKey.EncodedUtf8Bytes;
        if (key.Length > 6144) throw Invalid();
        var binary = new[] { envelope.Ciphertext, envelope.Nonce, envelope.AuthenticationTag, envelope.WrappedDataEncryptionKey };
        long length = checked(149L + key.Length);
        foreach (var bytes in binary) length = checked(length + Base64Length(bytes.Length));
        if (length > MaximumProtectedBytes(limits.MaximumPlaintextBytes) || length > Array.MaxLength) throw Invalid();
        var output = new byte[checked((int)length)];
        try
        {
            allocated?.Invoke("SerializedEnvelope", output);
            int position = 0;
            Append(output, ref position, "{\"FormatVersion\":2"u8);
            for (int i = 0; i < binary.Length; i++)
            {
                ct.ThrowIfCancellationRequested();
                Append(output, ref position, StrictUtf8.GetBytes(",\"" + Names[i + 1] + "\":\""));
                if (Base64.EncodeToUtf8(binary[i], output.AsSpan(position), out var consumed, out var written) != OperationStatus.Done || consumed != binary[i].Length)
                    throw Invalid();
                position = checked(position + written);
                Append(output, ref position, "\""u8);
            }
            Append(output, ref position, ",\"KeyEncryptionKeyId\":\""u8);
            Append(output, ref position, key);
            Append(output, ref position, "\",\"Algorithm\":\"AES-256-GCM\"}"u8);
            if (position != output.Length) throw Invalid();
            ct.ThrowIfCancellationRequested();
            return output;
        }
        catch { CryptographicOperations.ZeroMemory(output); throw; }
    }
    private static void Append(byte[] output, ref int position, ReadOnlySpan<byte> value)
    { value.CopyTo(output.AsSpan(position)); position = checked(position + value.Length); }
    public static void Clear(EncryptedEnvelope envelope)
    {
        CryptographicOperations.ZeroMemory(envelope.Ciphertext);
        CryptographicOperations.ZeroMemory(envelope.Nonce);
        CryptographicOperations.ZeroMemory(envelope.AuthenticationTag);
        CryptographicOperations.ZeroMemory(envelope.WrappedDataEncryptionKey);
    }
}

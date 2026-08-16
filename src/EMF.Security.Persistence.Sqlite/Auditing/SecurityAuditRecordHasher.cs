using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace EMF.Security.Persistence.Sqlite.Auditing;

internal static class SecurityAuditRecordHasher
{
    public const int CurrentVersion = 1;

    public static string ComputeHash(
        int integrityVersion,
        string? previousRecordHash,
        string operation,
        string resourceType,
        string resourceId,
        string subjectId,
        string? policyDecision,
        string? destination,
        string outcome,
        string occurredUtc,
        string factsJson)
    {
        using var hash =
            IncrementalHash.CreateHash(
                HashAlgorithmName.SHA256);

        var fields =
            new string?[]
            {
                integrityVersion.ToString(
                    CultureInfo.InvariantCulture),
                previousRecordHash,
                operation,
                resourceType,
                resourceId,
                subjectId,
                policyDecision,
                destination,
                outcome,
                occurredUtc,
                factsJson
            };

        foreach (var field in fields)
        {
            AppendField(hash, field);
        }

        return Convert.ToHexString(
            hash.GetHashAndReset());
    }

    private static void AppendField(
        IncrementalHash hash,
        string? value)
    {
        Span<byte> lengthBytes =
            stackalloc byte[sizeof(int)];

        if (value is null)
        {
            BinaryPrimitives.WriteInt32BigEndian(
                lengthBytes,
                -1);

            hash.AppendData(lengthBytes);
            return;
        }

        var valueBytes =
            Encoding.UTF8.GetBytes(value);

        try
        {
            BinaryPrimitives.WriteInt32BigEndian(
                lengthBytes,
                valueBytes.Length);

            hash.AppendData(lengthBytes);
            hash.AppendData(valueBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(
                valueBytes);
        }
    }
}

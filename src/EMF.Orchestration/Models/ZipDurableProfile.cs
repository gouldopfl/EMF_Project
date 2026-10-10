using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Core.Contracts.Malware;
using EMF.Core.Contracts.Zip;
using EMF.Security.Encryption.Envelope;

namespace EMF.Orchestration.Models;

// A closed semantic profile. Fixed numeric limits come from the certified engine;
// caller-supplied identities are configuration bindings, never permission grants.
public sealed record ZipDurableProfile(string ParentNamespaceId, string ChildNamespaceId,
    MalwareScannerPolicy ScannerPolicy, string ProtectionContractId,
    TimeSpan ScannerTimeout, TimeSpan OwnershipLease, TimeSpan RenewalInterval)
{
    public const string EncodingVersion = "EMF.ZipDurableProfile.v1";
    public static readonly TimeSpan DefaultOwnershipLease = TimeSpan.FromMinutes(15);
    public static readonly TimeSpan DefaultRenewalInterval = TimeSpan.FromMinutes(5);

    public static ZipDurableProfile Create(string parentNamespace, string childNamespace,
        MalwareScannerPolicy scannerPolicy, string protectionContractId) =>
        new(parentNamespace, childNamespace, scannerPolicy, protectionContractId,
            ZipNumericLimits.AttemptTimeout, DefaultOwnershipLease, DefaultRenewalInterval);

    public byte[] CanonicalBytes()
    {
        ZipAdmissionValidation.Hash(ParentNamespaceId); ZipAdmissionValidation.Hash(ChildNamespaceId);
        if (ParentNamespaceId == ChildNamespaceId) throw new InvalidDataException("ZIP namespaces must be distinct.");
        ArgumentNullException.ThrowIfNull(ScannerPolicy);
        ZipAdmissionValidation.Text(ScannerPolicy.ProviderId); ZipAdmissionValidation.Text(ScannerPolicy.Version);
        ZipAdmissionValidation.Hash(ScannerPolicy.Hash); ZipAdmissionValidation.Text(ProtectionContractId);
        if (ScannerTimeout <= TimeSpan.Zero || ScannerTimeout > ZipNumericLimits.AttemptTimeout ||
            OwnershipLease <= ZipNumericLimits.AttemptTimeout || RenewalInterval <= TimeSpan.Zero ||
            RenewalInterval >= OwnershipLease - ZipNumericLimits.AttemptTimeout)
            throw new InvalidDataException("ZIP lease/timeout configuration cannot cover bounded work and renewal.");
        using var bytes = new MemoryStream(); using var w = new Utf8JsonWriter(bytes);
        w.WriteStartObject(); w.WriteString("encoding", EncodingVersion);
        w.WriteString("algorithm", "durable-zip-v1"); w.WriteNumber("schema", 5);
        w.WriteNumber("parentBytes", ZipNumericLimits.Parent); w.WriteNumber("childBytes", ZipNumericLimits.Child);
        w.WriteNumber("aggregateBytes", ZipNumericLimits.Aggregate); w.WriteNumber("entries", ZipNumericLimits.Entries);
        w.WriteNumber("attempts", ZipNumericLimits.Attempts); w.WriteNumber("expandedWork", ZipNumericLimits.ExpandedWork);
        w.WriteNumber("compressedWork", ZipNumericLimits.CompressedWork); w.WriteNumber("parentReads", ZipNumericLimits.ParentReads);
        w.WriteNumber("replayExpanded", ZipNumericLimits.Aggregate); w.WriteNumber("replayScanner", ZipNumericLimits.Aggregate);
        w.WriteNumber("scannerReserved", ZipNumericLimits.ExpandedWork); w.WriteNumber("crcWork", ZipNumericLimits.ExpandedWork);
        w.WriteNumber("phaseAttempts", ZipNumericLimits.Entries * ZipNumericLimits.Attempts);
        w.WriteNumber("probes", ZipNumericLimits.Entries * ZipNumericLimits.Attempts);
        w.WriteNumber("preflightWork", ZipNumericLimits.Attempts * (ZipNumericLimits.Parent + ZipCentralDirectoryLimits.EndSearchBytes));
        w.WriteString("preflight", ZipCentralDirectoryLimits.ProfileVersion);
        w.WriteNumber("fieldBytes", ZipCentralDirectoryLimits.MaximumFieldBytes);
        w.WriteNumber("directoryBytes", ZipCentralDirectoryLimits.MaximumDirectoryBytes);
        w.WriteNumber("endSearchBytes", ZipCentralDirectoryLimits.EndSearchBytes);
        w.WriteString("zipLayouts", "single-disk-bounded-zip64-v1");
        w.WriteString("compression", "System.IO.Compression.ZipArchive");
        w.WriteString("entryPolicy", "encrypted-review;no-recursive-admission;safe-display-path-v1;NFC;ordinal-occurrences");
        w.WriteNumber("extractionChunk", 65_536); w.WriteNumber("expansionProbe", 1);
        w.WriteString("integrity", "SHA256+CRC32;plan-v1;occurrence-v1;ack-v1;ingestion-v1");
        w.WriteString("envelope", "context-bound-v2;AES-256-GCM;strict-bounded-codec-v1");
        w.WriteNumber("envelopeOverhead", BoundedEncryptedEnvelopeCodec.FixedMaximumOverheadBytes);
        w.WriteNumber("nonceBytes", 12); w.WriteNumber("tagBytes", 16); w.WriteNumber("wrappedKeyBytes", 16_384);
        w.WriteNumber("contextBytes", 256); w.WriteNumber("keyIdBytes", 1_024); w.WriteNumber("rawKeyIdBytes", 6_144);
        w.WriteNumber("jsonDepth", 1); w.WriteNumber("jsonTokens", 16); w.WriteNumber("jsonFields", 7); w.WriteNumber("jsonNameBytes", 24);
        w.WriteNumber("parentProtectedBytes", BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Parent));
        w.WriteNumber("childProtectedBytes", BoundedEncryptedEnvelopeCodec.MaximumProtectedBytes(ZipNumericLimits.Child));
        w.WriteString("parentNamespace", ParentNamespaceId); w.WriteString("childNamespace", ChildNamespaceId);
        w.WriteString("protectionContract", ProtectionContractId); w.WriteString("retention", "frozen-candidate;exact-revision;receipt-v1");
        w.WriteString("scannerProvider", ScannerPolicy.ProviderId); w.WriteString("scannerVersion", ScannerPolicy.Version);
        w.WriteString("scannerPolicyHash", ScannerPolicy.Hash); w.WriteNumber("scannerChunk", ZipNumericLimits.ScannerChunk);
        w.WriteNumber("scannerStreamMinimum", ZipNumericLimits.ScannerStreamMinimum); w.WriteNumber("scannerWork", ZipNumericLimits.ScannerWork);
        w.WriteNumber("attemptTimeoutTicks", ZipNumericLimits.AttemptTimeout.Ticks);
        w.WriteNumber("scannerTimeoutTicks", ScannerTimeout.Ticks); w.WriteNumber("ownershipLeaseTicks", OwnershipLease.Ticks);
        w.WriteNumber("renewalIntervalTicks", RenewalInterval.Ticks);
        w.WriteString("authorityProtocol", "zip-admission-v1;separate-authority-and-ownership");
        w.WriteString("failurePolicy", "unknown-partial-threat-review;bounded-transient-retry;no-legacy-fallback");
        w.WriteEndObject(); w.Flush(); return bytes.ToArray();
    }
    public string CanonicalJson => Encoding.UTF8.GetString(CanonicalBytes());
    public string Fingerprint => Convert.ToHexString(SHA256.HashData(CanonicalBytes()));
    public void ValidateRecoveryBinding(string canonicalJson, string fingerprint)
    {
        if (canonicalJson != CanonicalJson || fingerprint != Fingerprint)
            throw new InvalidDataException("ZIP durable profile changed; review is required.");
    }
}

using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EMF.Orchestration.Models;

namespace EMF.Tests;

public sealed class ZipDurableProfileTests
{
    private static ZipDurableProfile Profile() => ZipDurableProfile.Create(new string('A', 64), new string('B', 64),
        new("clamd", "policy-v1", new string('C', 64)), "protected-storage-v1");

    [Fact]
    public void Canonical_encoding_is_reconstructible_culture_independent_and_matches_sha256()
    {
        var p = Profile(); var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");
            Assert.Equal(p.CanonicalJson, (p with { }).CanonicalJson);
            Assert.Equal(p.Fingerprint, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(p.CanonicalJson))));
            Assert.StartsWith("{\"encoding\":\"EMF.ZipDurableProfile.v1\",\"algorithm\":\"durable-zip-v1\",\"schema\":5,", p.CanonicalJson);
            using var json = JsonDocument.Parse(p.CanonicalJson);
            Assert.Equal(44_767_425, json.RootElement.GetProperty("parentProtectedBytes").GetInt64());
            Assert.Equal(69_933_249, json.RootElement.GetProperty("childProtectedBytes").GetInt64());
            Assert.Equal(900 * TimeSpan.TicksPerSecond, json.RootElement.GetProperty("ownershipLeaseTicks").GetInt64());
            Assert.Equal(300 * TimeSpan.TicksPerSecond, json.RootElement.GetProperty("renewalIntervalTicks").GetInt64());
            p.ValidateRecoveryBinding(p.CanonicalJson, p.Fingerprint);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }

    [Theory]
    [InlineData("parent")] [InlineData("child")] [InlineData("scanner")] [InlineData("protection")]
    [InlineData("timeout")] [InlineData("lease")] [InlineData("renewal")]
    public void Every_configurable_semantic_group_changes_recovery_fingerprint(string field)
    {
        var p = Profile();
        var changed = field switch
        {
            "parent" => p with { ParentNamespaceId = new string('D', 64) },
            "child" => p with { ChildNamespaceId = new string('D', 64) },
            "scanner" => p with { ScannerPolicy = p.ScannerPolicy with { Hash = new string('D', 64) } },
            "protection" => p with { ProtectionContractId = "protected-storage-v2" },
            "timeout" => p with { ScannerTimeout = TimeSpan.FromSeconds(90) },
            "lease" => p with { OwnershipLease = TimeSpan.FromMinutes(20) },
            _ => p with { RenewalInterval = TimeSpan.FromMinutes(4) }
        };
        Assert.NotEqual(p.Fingerprint, changed.Fingerprint);
        Assert.Throws<InvalidDataException>(() => changed.ValidateRecoveryBinding(p.CanonicalJson, p.Fingerprint));
    }

    [Theory]
    [InlineData("whitespace")] [InlineData("duplicate")] [InlineData("missing")] [InlineData("version")] [InlineData("unknown")]
    public void Persisted_noncanonical_unknown_or_incomplete_profile_is_rejected_even_with_matching_hash(string mutation)
    {
        var p = Profile();
        var json = mutation switch
        {
            "whitespace" => " " + p.CanonicalJson,
            "duplicate" => p.CanonicalJson.Replace("\"schema\":5", "\"schema\":5,\"schema\":5"),
            "missing" => p.CanonicalJson.Replace("\"schema\":5,", ""),
            "version" => p.CanonicalJson.Replace("EMF.ZipDurableProfile.v1", "EMF.ZipDurableProfile.v2"),
            _ => p.CanonicalJson[..^1] + ",\"extra\":1}"
        };
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        Assert.Throws<InvalidDataException>(() => p.ValidateRecoveryBinding(json, hash));
        Assert.Throws<InvalidDataException>(() => p.ValidateRecoveryBinding(p.CanonicalJson, new string('D', 64)));
    }
}

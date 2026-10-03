using System.Security.Cryptography;
using System.Text.Json;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models.Identities;
using EMF.Persistence.Storage;
using EMF.Security.Auditing;
using EMF.Security.Auditing.Models;
using EMF.Security.Authorization;
using EMF.Security.Encryption;
using EMF.Security.Encryption.Models;
using EMF.Security.Encryption.Envelope;
using EMF.Security.Encryption.Envelope.Models;
using EMF.Security.Encryption.Envelope.Services;
using EMF.Security.Models;
using EMF.Security.Models.Identities;
using EMF.Security.Storage;
using EMF.Security.Storage.Models;
using EMF.Security.Azure.Cryptography;
using EMF.Security.Azure.Encryption;
using EMF.Security.Azure.Keys;
using Xunit;

namespace EMF.Tests;

// Preserved ADR-017/022 repro assertions. Only required capability wiring changed.
public sealed class ProtectedContentAuditRegressionTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Rewrap_RejectsAuthenticatedContentCorruption(bool alreadyCurrent, bool corruptTag)
    {
        using var fixture = new Fixture();
        await fixture.Encrypted.WriteAsync(fixture.Id, new byte[] { 1, 2, 3 });
        var envelope = JsonSerializer.Deserialize<EncryptedEnvelope>((await fixture.Raw.ReadAsync(fixture.Id))!)!;
        if (corruptTag) envelope.AuthenticationTag[0] ^= 1;
        else envelope.Ciphertext[0] ^= 1;
        await fixture.Raw.WriteAsync(fixture.Id, JsonSerializer.SerializeToUtf8Bytes(envelope));
        await Assert.ThrowsAnyAsync<CryptographicException>(() => fixture.Encrypted.ReadAsync(fixture.Id));
        if (!alreadyCurrent) fixture.Keys.Current = "key/v2";
        var exception = await Record.ExceptionAsync(() => fixture.Service().RewrapAsync(Request(fixture.Id)));
        Assert.True(exception != null, "Rewrapping accepted content whose authentication already fails.");
    }

    [Fact]
    public async Task Rewrap_RejectsEnvelopeBoundToAnotherArtifact()
    {
        using var fixture = new Fixture();
        var other = new ArtifactId("synthetic-other-artifact");
        await fixture.Encrypted.WriteAsync(other, new byte[] { 1, 2, 3 });
        await fixture.Raw.WriteAsync(fixture.Id, (await fixture.Raw.ReadAsync(other))!);
        await Assert.ThrowsAnyAsync<CryptographicException>(() => fixture.Encrypted.ReadAsync(fixture.Id));
        fixture.Keys.Current = "key/v2";
        var exception = await Record.ExceptionAsync(() => fixture.Service().RewrapAsync(Request(fixture.Id)));
        Assert.True(exception != null, "Rewrapping accepted an envelope authenticated to a different Artifact.");
    }

    [Fact]
    public async Task Rewrap_DoesNotOverwriteConcurrentContentReplacement()
    {
        using var fixture = new Fixture();
        await fixture.Encrypted.WriteAsync(fixture.Id, new byte[] { 1, 2, 3 });
        fixture.Keys.Current = "key/v2";
        var gate = new GatedRewrapper(new DevelopmentEnvelopeKeyRewrappingService(fixture.Keys));
        var pending = fixture.Service(gate).RewrapAsync(Request(fixture.Id));
        await gate.Started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        byte[] replacement = { 4, 5, 6 };
        try { await fixture.Encrypted.WriteAsync(fixture.Id, replacement); }
        finally { gate.Release.TrySetResult(); }
        await pending;
        var durable = (await fixture.Encrypted.ReadAsync(fixture.Id))!;
        Assert.True(durable.SequenceEqual(replacement), "Rewrapping overwrote a concurrent durable content replacement.");
    }

    [Fact]
    public async Task Rewrap_FailureAuditRetainsKnownPreviousKeyId()
    {
        using var fixture = new Fixture();
        await fixture.Encrypted.WriteAsync(fixture.Id, new byte[] { 1, 2, 3 });
        fixture.Keys.Current = "key/v2";
        fixture.Keys.Keys.Remove("key/v1");
        await Assert.ThrowsAnyAsync<CryptographicException>(() => fixture.Service().RewrapAsync(Request(fixture.Id)));
        var audit = Assert.Single(fixture.Audit.Records);
        Assert.True(audit.Facts.ContainsKey("previousKeyEncryptionKeyId"), "Failed rewrapping audit omitted the known historical key identity.");
    }

    [Fact]
    public async Task Rewrap_MissingHistoricalKeyPreservesDurableEnvelope()
    {
        using var fixture = new Fixture();
        await fixture.Encrypted.WriteAsync(fixture.Id, new byte[] { 1, 2, 3 });
        var before = (await fixture.Raw.ReadAsync(fixture.Id))!;
        fixture.Keys.Current = "key/v2";
        fixture.Keys.Keys.Remove("key/v1");
        await Assert.ThrowsAnyAsync<CryptographicException>(() => fixture.Service().RewrapAsync(Request(fixture.Id)));
        var after = (await fixture.Raw.ReadAsync(fixture.Id))!;
        Assert.True(before.SequenceEqual(after), "Missing historical key changed durable envelope.");
    }

    [Fact]
    public async Task AzureEnvelope_SanitizesProviderExceptionDetails()
    {
        var provider = new AzureEnvelopeEncryptionService(new AzureKeys(), new ThrowingAzureFactory());
        var exception = await Record.ExceptionAsync(() => provider.EncryptAsync(new byte[] { 1, 2, 3 }));
        Assert.NotNull(exception);
        Assert.True(!exception!.ToString().Contains("synthetic-sensitive-marker"), "Provider exception details escaped the security boundary.");
    }

    static ArtifactEnvelopeRewrappingRequest Request(ArtifactId id) => new()
    {
        ArtifactId = id,
        SubjectId = "synthetic-steward",
        ProtectionClassificationId = new ProtectionClassificationId(ProtectionClassifications.Confidential)
    };
    sealed class Fixture : IDisposable
    {
        readonly string root = Path.Combine(Path.GetTempPath(), "emf-audit-content-" + Guid.NewGuid());
        public ArtifactId Id { get; } = new("synthetic-audit-artifact");
        public KeysProvider Keys { get; } = new();
        public FileSystemArtifactContentStore Raw { get; }
        public EncryptedArtifactContentStore Encrypted { get; }
        public RecordingSecurityAuditSink Audit { get; } = new();
        public Fixture()
        {
            Raw = new(root);
            Encrypted = new(Raw, new DevelopmentEnvelopeEncryptionService(Keys));
        }
        public ArtifactEnvelopeRewrappingService Service(IAuthenticatedEnvelopeKeyRewrappingService? provider = null) =>
            new(Raw, provider ?? new DevelopmentEnvelopeKeyRewrappingService(Keys), new AllowPolicy(), Audit,
                new RewrapTestAuthority(), new RewrapTestJournal(), new RewrapTestStaging());
        public void Dispose()
        {
            foreach (var key in Keys.Keys.Values) CryptographicOperations.ZeroMemory(key.KeyMaterial);
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }
    sealed class KeysProvider : IEncryptionKeyProvider
    {
        public string Current = "key/v1";
        public Dictionary<string, EncryptionKey> Keys = new()
        {
            ["key/v1"] = new() { KeyId = "key/v1", KeyMaterial = RandomNumberGenerator.GetBytes(32) },
            ["key/v2"] = new() { KeyId = "key/v2", KeyMaterial = RandomNumberGenerator.GetBytes(32) }
        };
        public Task<string?> GetCurrentKeyIdAsync(CancellationToken token = default) => Task.FromResult<string?>(Current);
        public Task<EncryptionKey?> GetKeyAsync(string id, CancellationToken token = default) => Task.FromResult(Keys.GetValueOrDefault(id));
    }
    sealed class AllowPolicy : IAuthorizationPolicy
    {
        public Task<AuthorizationDecision> EvaluateAsync(AuthorizationRequest request, CancellationToken token = default) => Task.FromResult(AuthorizationDecision.Allow);
    }
    sealed class GatedRewrapper(IAuthenticatedEnvelopeKeyRewrappingService inner) : IAuthenticatedEnvelopeKeyRewrappingService
    {
        public TaskCompletionSource Started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public async Task<EncryptedEnvelope> RewrapAuthenticatedAsync(EncryptedEnvelope envelope, ReadOnlyMemory<byte> context, CancellationToken token = default)
        {
            Started.TrySetResult();
            await Release.Task.WaitAsync(token);
            return await inner.RewrapAuthenticatedAsync(envelope, context, token);
        }
    }
    sealed class AzureKeys : IAzureKeyReferenceProvider
    {
        public Task<AzureKeyReference> GetCurrentKeyAsync(CancellationToken token = default) =>
            Task.FromResult(new AzureKeyReference { KeyName = "synthetic-key", KeyVersion = "0123456789abcdef0123456789abcdef" });
        public Task<AzureKeyReference?> GetKeyAsync(string id, CancellationToken token = default) => Task.FromResult<AzureKeyReference?>(null);
    }
    sealed class ThrowingAzureFactory : IAzureKeyCryptographyFactory
    {
        public IAzureKeyCryptography Create(AzureKeyReference key) => new ThrowingAzureCryptography();
    }
    sealed class ThrowingAzureCryptography : IAzureKeyCryptography
    {
        public Task<byte[]> WrapKeyAsync(byte[] key, CancellationToken token = default) => throw new InvalidOperationException("synthetic-sensitive-marker");
        public Task<byte[]> UnwrapKeyAsync(byte[] key, CancellationToken token = default) => throw new InvalidOperationException("synthetic-sensitive-marker");
    }
}

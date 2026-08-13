using System.Security.Cryptography;
using EMF.Core.Contracts;
using EMF.Core.Models.Integrity;

namespace EMF.Integrity;

public sealed class Sha256ContentFingerprintService 
    : IContentFingerprintService
{
    public async Task<ContentFingerprint> ComputeAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        await using var stream = File.OpenRead(sourcePath);

        var hash = await SHA256.HashDataAsync(
            stream,
            cancellationToken);

        return new ContentFingerprint
        {
            Algorithm = "SHA-256",
            Value = Convert.ToHexString(hash)
        };
    }

    public Task<ContentFingerprint> ComputeAsync(
        ReadOnlyMemory<byte> content,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var hash = SHA256.HashData(content.Span);

        return Task.FromResult(
            new ContentFingerprint
            {
                Algorithm = "SHA-256",
                Value = Convert.ToHexString(hash)
            });
    }
}

using EMF.Integrity;

namespace EMF.Tests;

public class IntegrityTests
{
    [Fact]
    public async Task Sha256ContentFingerprintService_ComputesFingerprint()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            $"emf-integrity-{Guid.NewGuid():N}.txt");

        try
        {
            await File.WriteAllTextAsync(
                path,
                "EMF integrity test");

            var service = new Sha256ContentFingerprintService();

            var fingerprint = await service.ComputeAsync(path);

            Assert.Equal(
                "SHA-256",
                fingerprint.Algorithm);

            Assert.False(
                string.IsNullOrWhiteSpace(fingerprint.Value));

            Assert.Equal(
                64,
                fingerprint.Value.Length);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
    [Fact]
    public async Task SameContentProducesSameFingerprint()
    {
        var a = Path.GetTempFileName();
        var b = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(a, "same EMF content");
            await File.WriteAllTextAsync(b, "same EMF content");
            var service = new Sha256ContentFingerprintService();
            var first = await service.ComputeAsync(a);
            var second = await service.ComputeAsync(b);
            Assert.Equal(first.Value, second.Value);
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
        }
    }

    [Fact]
    public async Task DifferentContentProducesDifferentFingerprints()
    {
        var a = Path.GetTempFileName();
        var b = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(a, "EMF content A");
            await File.WriteAllTextAsync(b, "EMF content B");
            var service = new Sha256ContentFingerprintService();
            var first = await service.ComputeAsync(a);
            var second = await service.ComputeAsync(b);
            Assert.NotEqual(first.Value, second.Value);
        }
        finally
        {
            File.Delete(a);
            File.Delete(b);
        }
    }

}

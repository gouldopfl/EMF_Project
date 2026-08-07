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
}

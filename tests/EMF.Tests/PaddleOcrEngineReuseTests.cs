using EMF.Core.Models;
using EMF.Orchestration.Services;
using OpenCvSharp;
using SkiaSharp;

namespace EMF.Tests;

// Blocking synchronization tests must not compete with parallel native renderer tests.
[Collection(ReviewerDeploymentEnvironmentCollection.Name)]
public sealed class PaddleOcrEngineReuseTests
{
    private sealed class Engine(Func<string?> run) : IPaddleOcrEngine
    {
        public string? Run(Mat image) => run();
    }

    private static byte[] Image()
    {
        using var bitmap = new SKBitmap(10, 10);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public void PaddleOcrUsesSupportedDeviceApi()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
        var directory = Path.Combine(root, "src", "EMF.Orchestration", "Services");
        foreach (var path in Directory.GetFiles(directory, "Paddle*.cs"))
            Assert.DoesNotContain("Mkldnn(", File.ReadAllText(path));
        Assert.Contains("PaddleDevice.OneDnn(",
            File.ReadAllText(Path.Combine(directory, "PaddleOcrEngineCache.cs")));
    }

    [Fact]
    public async Task EnglishAliasesAndServiceInstancesReuseOneEngine()
    {
        var created = 0;
        var runs = 0;
        var cache = new PaddleOcrEngineCache(_ =>
        {
            created++;
            return new Engine(() => { runs++; return " unchanged text "; });
        });
        foreach (var language in new[] { "english", "en", "unknown", null })
            Assert.Equal(" unchanged text ", await new PaddleImageOcrService(cache)
                .RecognizeTextAsync(new OcrRequest(Image(), language)));
        Assert.Equal(1, created);
        Assert.Equal(4, runs);
    }

    [Fact]
    public async Task AllSupportedModelsHaveDistinctBoundedSlots()
    {
        var created = new List<OcrLanguage>();
        var cache = new PaddleOcrEngineCache(language =>
        {
            created.Add(language);
            return new Engine(() => language.ToString());
        });
        using var image = new Mat();
        foreach (var language in Enum.GetValues<OcrLanguage>())
            for (var call = 0; call < 2; call++)
                Assert.Equal(language.ToString(), await cache.RunAsync(language, image, default));
        Assert.Equal(8, created.Distinct().Count());
        Assert.Equal(8, created.Count);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            cache.RunAsync((OcrLanguage)999, image, default));
        Assert.Equal(8, created.Count);
    }

    [Fact]
    public async Task SameModelWaitsWhileDifferentModelRunsIndependently()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var englishRuns = 0;
        var created = 0;
        var cache = new PaddleOcrEngineCache(language =>
        {
            Interlocked.Increment(ref created);
            return new Engine(() =>
            {
                if (language == OcrLanguage.English)
                {
                    if (Interlocked.Increment(ref englishRuns) == 1)
                    {
                        entered.Set();
                        Assert.True(release.Wait(TimeSpan.FromSeconds(10)));
                    }
                }
                return language.ToString();
            });
        });
        using var firstImage = new Mat();
        using var secondImage = new Mat();
        using var otherImage = new Mat();
        var first = Task.Run(() => cache.RunAsync(OcrLanguage.English, firstImage, default));
        try
        {
            Assert.True(entered.Wait(TimeSpan.FromSeconds(10)));
            var second = cache.RunAsync(OcrLanguage.English, secondImage, default);
            Assert.False(second.IsCompleted);
            Assert.Equal(1, englishRuns);
            Assert.Equal("Latin", await cache.RunAsync(OcrLanguage.Latin, otherImage, default));
            using var cancellation = new CancellationTokenSource();
            var cancelled = cache.RunAsync(OcrLanguage.English, otherImage, cancellation.Token);
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancelled);
            Assert.Equal(1, englishRuns);
            release.Set();
            await Task.WhenAll(first, second);
            Assert.Equal(2, englishRuns);
            Assert.Equal(2, created);
        }
        finally { release.Set(); await first; }
    }

    [Fact]
    public async Task ValidationAndPreCancelledRequestsNeverCreateEngine()
    {
        var cache = new PaddleOcrEngineCache(_ => throw new Xunit.Sdk.XunitException("Engine initialized"));
        var service = new PaddleImageOcrService(cache, maxInputBytes: 4);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RecognizeTextAsync(new OcrRequest(new byte[5])));
        service = new PaddleImageOcrService(cache, maxDimensionPixels: 5);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RecognizeTextAsync(new OcrRequest(Image())));
        service = new PaddleImageOcrService(cache, maxPixelCount: 50);
        await Assert.ThrowsAsync<InvalidDataException>(() => service.RecognizeTextAsync(new OcrRequest(Image())));
        service = new PaddleImageOcrService(cache);
        await Assert.ThrowsAsync<MetadataExtractor.ImageProcessingException>(() =>
            service.RecognizeTextAsync(new OcrRequest(new byte[3])));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RecognizeTextAsync(
            new OcrRequest(Image()), new CancellationToken(true)));
        using var image = new Mat();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cache.RunAsync(
            OcrLanguage.English, image, new CancellationToken(true)));
    }

    [Fact]
    public async Task CancellationDuringInitializationPreventsInference()
    {
        using var cancellation = new CancellationTokenSource();
        var runs = 0;
        var cache = new PaddleOcrEngineCache(_ =>
        {
            cancellation.Cancel();
            return new Engine(() => { runs++; return "text"; });
        });
        using var image = new Mat();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cache.RunAsync(OcrLanguage.English, image, cancellation.Token));
        Assert.Equal(0, runs);
        Assert.Equal("text", await cache.RunAsync(OcrLanguage.English, image, default));
        Assert.Equal(1, runs);
    }

    [Fact]
    public async Task InitializationAndInferenceFailuresReleaseTheGate()
    {
        var attempts = 0;
        var runs = 0;
        var cache = new PaddleOcrEngineCache(_ =>
        {
            if (++attempts == 1) throw new InvalidOperationException();
            return new Engine(() =>
            {
                if (++runs == 1) throw new InvalidOperationException();
                return "text";
            });
        });
        using var image = new Mat();
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                cache.RunAsync(OcrLanguage.English, image, default));
        Assert.Equal("text", await cache.RunAsync(OcrLanguage.English, image, default));
        Assert.Equal(2, attempts);
        Assert.Equal(2, runs);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("  ", null)]
    [InlineData(" text ", " text ")]
    public async Task OutputSemanticsArePreserved(string? output, string? expected)
    {
        var cache = new PaddleOcrEngineCache(_ => new Engine(() => output));
        Assert.Equal(expected, await new PaddleImageOcrService(cache).RecognizeTextAsync(new OcrRequest(Image())));
    }

    [Fact]
    public async Task TextLimitStillAppliesToReusedEngine()
    {
        var cache = new PaddleOcrEngineCache(_ => new Engine(() => "long"));
        var service = new PaddleImageOcrService(cache, maxExtractedTextChars: 1);
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<InvalidDataException>(() => service.RecognizeTextAsync(new OcrRequest(Image())));
    }
}

using EMF.Common;
using EMF.Core.Contracts;
using EMF.Core.Models;
using EMF.Core.Services;
using OpenCvSharp;
using System.Runtime.InteropServices;
using MetadataExtractor;
using MetadataExtractor.Formats.Bmp;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Gif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.WebP;

namespace EMF.Orchestration.Services;

public sealed class PaddleImageOcrService :
    IImageOcrService
{
    public const long DefaultMaxInputBytes = 100L * 1024 * 1024;
    public const int DefaultMaxDimensionPixels = 20_000;
    public const long DefaultMaxPixelCount = 40_000_000L;
    public const int DefaultMaxExtractedTextChars = 10 * 1024 * 1024;

    private readonly PaddleOcrEngineCache _engines;
    private readonly long _maxInputBytes;
    private readonly int _maxDimensionPixels;
    private readonly long _maxPixelCount;
    private readonly int _maxExtractedTextChars;

    public PaddleImageOcrService(
        long maxInputBytes = DefaultMaxInputBytes,
        int maxDimensionPixels = DefaultMaxDimensionPixels,
        long maxPixelCount = DefaultMaxPixelCount,
        int maxExtractedTextChars = DefaultMaxExtractedTextChars)
        : this(PaddleOcrEngineCache.Shared, maxInputBytes, maxDimensionPixels,
            maxPixelCount, maxExtractedTextChars)
    {
    }

    internal PaddleImageOcrService(
        PaddleOcrEngineCache engines,
        long maxInputBytes = DefaultMaxInputBytes,
        int maxDimensionPixels = DefaultMaxDimensionPixels,
        long maxPixelCount = DefaultMaxPixelCount,
        int maxExtractedTextChars = DefaultMaxExtractedTextChars)
    {
        _engines = engines;
        ValidatePositive(maxInputBytes, nameof(maxInputBytes));
        ValidatePositive(
            maxDimensionPixels,
            nameof(maxDimensionPixels));
        ValidatePositive(maxPixelCount, nameof(maxPixelCount));
        ValidatePositive(
            maxExtractedTextChars,
            nameof(maxExtractedTextChars));

        _maxInputBytes = maxInputBytes;
        _maxDimensionPixels = maxDimensionPixels;
        _maxPixelCount = maxPixelCount;
        _maxExtractedTextChars = maxExtractedTextChars;
    }

    public async Task<string?> RecognizeTextAsync(
        OcrRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        if (request.Image.Length > _maxInputBytes)
            throw new InvalidDataException(
                "OCR image exceeds the maximum allowed size.");

        ValidateImageDimensions(request.Image);

        cancellationToken.ThrowIfCancellationRequested();

        using var decodeTiming = EmfPerformanceTiming.Measure(EmfPerformancePhase.PaddleDecode);
        using var source =
            Cv2.ImDecode(
                request.Image.ToArray(),
                ImreadModes.Color);

        decodeTiming.Dispose();
        if (source.Empty())
            throw new InvalidDataException(
                "The image could not be decoded.");

        var resultText = await _engines.RunAsync(
            OcrLanguageResolver.Resolve(request.Language),
            source,
            cancellationToken);

        var text =
            string.IsNullOrWhiteSpace(resultText)
                ? null
                : resultText;

        if (text is not null &&
            text.Length > _maxExtractedTextChars)
        {
            throw new InvalidDataException(
                "OCR text exceeds the maximum allowed size.");
        }

        return text;
    }

    private void ValidateImageDimensions(ReadOnlyMemory<byte> image)
    {
        if (image.Length > _maxInputBytes)
        {
            throw new InvalidDataException(
                "OCR image exceeds the maximum allowed size.");
        }

        if (!MemoryMarshal.TryGetArray(image, out var segment) ||
            segment.Array is null)
        {
            segment = new ArraySegment<byte>(image.ToArray());
        }

        var buffer =
            segment.Array
            ?? throw new InvalidOperationException(
                "OCR image buffer is unavailable.");

        using var stream =
            new MemoryStream(
                buffer,
                segment.Offset,
                segment.Count,
                writable: false);

        var directories =
            ImageMetadataReader.ReadMetadata(stream);

        foreach (var directory in directories)
        {
            var tags = directory switch
            {
                JpegDirectory => (
                    JpegDirectory.TagImageWidth,
                    JpegDirectory.TagImageHeight),
                PngDirectory => (
                    PngDirectory.TagImageWidth,
                    PngDirectory.TagImageHeight),
                GifHeaderDirectory => (
                    GifHeaderDirectory.TagImageWidth,
                    GifHeaderDirectory.TagImageHeight),
                BmpHeaderDirectory => (
                    BmpHeaderDirectory.TagImageWidth,
                    BmpHeaderDirectory.TagImageHeight),
                WebPDirectory => (
                    WebPDirectory.TagImageWidth,
                    WebPDirectory.TagImageHeight),
                ExifIfd0Directory => (
                    ExifDirectoryBase.TagImageWidth,
                    ExifDirectoryBase.TagImageHeight),
                _ => (-1, -1)
            };

            if (tags.Item1 < 0)
                continue;

            var widthValue = directory.GetObject(tags.Item1);
            var heightValue = directory.GetObject(tags.Item2);

            if (widthValue is null || heightValue is null)
                continue;

            var width = Convert.ToInt64(widthValue);
            var height = Convert.ToInt64(heightValue);

            if (width <= 0 ||
                height <= 0 ||
                width > _maxDimensionPixels ||
                height > _maxDimensionPixels ||
                checked(width * height) > _maxPixelCount)
            {
                throw new InvalidDataException(
                    "OCR image dimensions exceed the maximum allowed size.");
            }

            return;
        }

        throw new InvalidDataException(
            "OCR image dimensions could not be determined.");
    }


    private static void ValidatePositive(
        long value,
        string parameterName)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(
                parameterName);
        }
    }
}

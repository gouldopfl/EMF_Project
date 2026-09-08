using System.Runtime.InteropServices;
using EMF.Core.Contracts;
using EMF.Core.Contracts.Storage;
using EMF.Core.Models;
using EMF.Core.Models.Identities;
using MetadataExtractor;
using MetadataExtractor.Formats.Bmp;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Jpeg;
using MetadataExtractor.Formats.Png;
using MetadataExtractor.Formats.WebP;
using OpenCvSharp;

namespace EMF.Orchestration.Services;

public sealed class ImageArtifactPrintRenderingProvider :
    IArtifactPrintRenderingProvider
{
    private readonly IArtifactContentStore _contentStore;

    public ImageArtifactPrintRenderingProvider(
        IArtifactContentStore contentStore)
    {
        ArgumentNullException.ThrowIfNull(contentStore);
        _contentStore = contentStore;
    }

    public bool CanRender(string contentType) =>
        contentType.Equals("image/jpeg", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/png", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/bmp", StringComparison.OrdinalIgnoreCase) ||
        contentType.Equals("image/webp", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<PrintableArtifactPage>> RenderAsync(
        ArtifactId artifactId,
        CancellationToken cancellationToken = default)
    {
        var content =
            await _contentStore.ReadAsync(
                artifactId,
                cancellationToken);

        if (content is null)
            return [];

        cancellationToken.ThrowIfCancellationRequested();

        ValidateImage(content);

        using var image =
            Cv2.ImDecode(
                content,
                ImreadModes.Unchanged);

        if (image.Empty())
            throw new InvalidDataException(
                "Printable image could not be decoded.");

        Cv2.ImEncode(".png", image, out var png);

        return
        [
            new PrintableArtifactPage
            {
                PageNumber = 1,
                ContentType = "image/png",
                Content = png
            }
        ];
    }

    private static void ValidateImage(byte[] content)
    {
        if (content.LongLength >
            PaddleImageOcrService.DefaultMaxInputBytes)
            throw new InvalidDataException(
                "Printable image exceeds the maximum allowed size.");

        using var stream =
            new MemoryStream(content, writable: false);

        foreach (var directory in
            ImageMetadataReader.ReadMetadata(stream))
        {
            var tags = directory switch
            {
                JpegDirectory => (
                    JpegDirectory.TagImageWidth,
                    JpegDirectory.TagImageHeight),
                PngDirectory => (
                    PngDirectory.TagImageWidth,
                    PngDirectory.TagImageHeight),
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

            var width = Convert.ToInt64(directory.GetObject(tags.Item1));
            var height = Convert.ToInt64(directory.GetObject(tags.Item2));

            if (width <= 0 || height <= 0 ||
                width > PaddleImageOcrService.DefaultMaxDimensionPixels ||
                height > PaddleImageOcrService.DefaultMaxDimensionPixels ||
                checked(width * height) >
                    PaddleImageOcrService.DefaultMaxPixelCount)
                throw new InvalidDataException(
                    "Printable image dimensions exceed the maximum allowed size.");

            return;
        }

        throw new InvalidDataException(
            "Printable image dimensions could not be determined.");
    }
}

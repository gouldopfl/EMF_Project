using EMF.Discovery.Contracts;
using EMF.Discovery.Models;

namespace EMF.Discovery.Services;

public sealed class ArtifactInspectionService :
    IArtifactInspectionService
{
    private const int InspectionSampleSize = 64 * 1024;

    private readonly IReadOnlyList<IArtifactSignatureProvider> _providers;
    private readonly IReadOnlyList<IArtifactContentInspector> _inspectors;
    private readonly ArtifactContentTypeResolver _contentTypeResolver;

    public ArtifactInspectionService(
        IEnumerable<IArtifactSignatureProvider> providers,
        IEnumerable<IArtifactContentInspector>? inspectors = null)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToArray();
        _inspectors = inspectors?.ToArray()
            ?? Array.Empty<IArtifactContentInspector>();

        _contentTypeResolver = new ArtifactContentTypeResolver();
    }

    public async Task<ArtifactInspectionResult> InspectAsync(
        string sourcePath,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException(
                "A source path is required.",
                nameof(sourcePath));

        if (!File.Exists(sourcePath))
            throw new FileNotFoundException(
                "The source file was not found.",
                sourcePath);

        cancellationToken.ThrowIfCancellationRequested();

        var info = new FileInfo(sourcePath);

        var sampleLength =
            (int)Math.Min(
                info.Length,
                InspectionSampleSize);

        var content = new byte[sampleLength];

        await using (var stream =
            new FileStream(
                sourcePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read))
        {
            var offset = 0;

            while (offset < content.Length)
            {
                var read = await stream.ReadAsync(
                    content.AsMemory(offset),
                    cancellationToken);

                if (read == 0)
                    break;

                offset += read;
            }
        }

        string? contentType = null;
        string? format = null;
        var findings = new List<string>();

        foreach (var provider in _providers)
        {
            if (provider.TryDetect(
                content,
                out var detectedType,
                out var detectedFormat))
            {
                contentType = detectedType;
                format = detectedFormat;
                break;
            }
        }

        if (format is null)
        {
            findings.Add(
                "No registered content signature matched the artifact.");

            contentType =
                _contentTypeResolver.Resolve(info.Extension);
        }

        var metadata = new Dictionary<string, object>
        {
            ["extension"] = info.Extension,
            ["inspectionSampleBytes"] = content.Length,
            ["inspectionSampleTruncated"] =
                info.Length > content.Length
        };

        if (contentType is not null)
        {
            foreach (var inspector in _inspectors)
            {
                if (!inspector.CanInspect(contentType))
                    continue;

                inspector.Inspect(
                    content,
                    metadata,
                    findings);

                break;
            }
        }

        return new ArtifactInspectionResult
        {
            SourcePath = info.FullName,
            DetectedContentType = contentType,
            DetectedFormat = format,
            SizeBytes = info.Length,
            Metadata = metadata,
            Findings = findings
        };
    }
}

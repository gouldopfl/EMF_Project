using System.Text;
using System.Text.Json;

namespace EMF.Common;

public static class EmfBuildManifestFile
{
    private static readonly JsonSerializerOptions Options =
        new() { WriteIndented = true };

    public static void Save(string path, EmfBuildManifest manifest)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        EmfBuildManifestIdentity.Validate(manifest);

        File.WriteAllText(
            path,
            JsonSerializer.Serialize(manifest, Options),
            new UTF8Encoding(false));
    }

    public static EmfBuildManifest Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        EmfBuildManifest manifest;
        try
        {
            manifest =
                JsonSerializer.Deserialize<EmfBuildManifest>(
                    File.ReadAllText(path),
                    Options)
                ?? throw new InvalidDataException(
                    "Build manifest is invalid.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException(
                "Build manifest is invalid.",
                ex);
        }

        EmfBuildManifestIdentity.Validate(manifest);
        return manifest;
    }

}

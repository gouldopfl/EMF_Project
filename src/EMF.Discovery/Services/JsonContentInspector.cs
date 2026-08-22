using System.Text;
using System.Text.Json;
using EMF.Discovery.Contracts;

namespace EMF.Discovery.Services;

public sealed class JsonContentInspector :
    IArtifactContentInspector
{
    public bool CanInspect(string contentType) =>
        string.Equals(
            contentType,
            "application/json",
            StringComparison.OrdinalIgnoreCase);

    public void Inspect(
        ReadOnlySpan<byte> content,
        IDictionary<string, object> metadata,
        ICollection<string> findings)
    {
        try
        {
            using var document =
                JsonDocument.Parse(
                    Encoding.UTF8.GetString(content));

            var root = document.RootElement;

            metadata["jsonRootKind"] =
                root.ValueKind.ToString();

            if (root.ValueKind == JsonValueKind.Object)
            {
                metadata["jsonPropertyCount"] =
                    root.EnumerateObject().Count();
            }
            else if (root.ValueKind == JsonValueKind.Array)
            {
                metadata["jsonElementCount"] =
                    root.GetArrayLength();
            }

            findings.Add(
                "Valid JSON content was parsed successfully.");
        }
        catch (JsonException)
        {
            findings.Add(
                "Content is not valid JSON.");
        }
    }
}

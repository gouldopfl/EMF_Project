using System.Text;
using EMF.Discovery.Contracts;

namespace EMF.Discovery.Services;

public sealed class CsvContentInspector :
    IArtifactContentInspector
{
    public bool CanInspect(string contentType) =>
        string.Equals(
            contentType,
            "text/csv",
            StringComparison.OrdinalIgnoreCase);

    public void Inspect(
        ReadOnlySpan<byte> content,
        IDictionary<string, object> metadata,
        ICollection<string> findings)
    {
        var text = Encoding.UTF8.GetString(content);

        var lines =
            text.Split(
                ['\r', '\n'],
                StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length == 0)
        {
            findings.Add("CSV content is empty.");
            return;
        }

        var rows =
            lines.Take(100)
                .Select(ParseRow)
                .ToArray();

        var columnCount = rows[0].Count;

        metadata["csvSampleRowCount"] = rows.Length;
        metadata["csvColumnCount"] = columnCount;

        var consistent =
            rows.All(row => row.Count == columnCount);

        metadata["csvConsistentColumnCount"] = consistent;

        if (columnCount > 1 && consistent)
        {
            findings.Add(
                "CSV structure detected with consistent column counts.");
        }
        else
        {
            findings.Add(
                "CSV structure is inconsistent or could not be established.");
        }
    }

    private static IReadOnlyList<string> ParseRow(
        string line)
    {
        var values = new List<string>();
        var current = new StringBuilder();
        var quoted = false;

        for (var i = 0; i < line.Length; i++)
        {
            var character = line[i];

            if (character == '"')
            {
                if (quoted &&
                    i + 1 < line.Length &&
                    line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                    continue;
                }

                quoted = !quoted;
                continue;
            }

            if (character == ',' && !quoted)
            {
                values.Add(current.ToString());
                current.Clear();
                continue;
            }

            current.Append(character);
        }

        values.Add(current.ToString());

        return values;
    }
}

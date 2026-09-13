using EMF.Core.Models;

namespace EMF.Extensions.VeteransClaims.Orchestration;

public static class VeteransReviewerPageSelector
{
    public static IReadOnlyList<PrintableArtifactPage> Select(
        IReadOnlyList<PrintableArtifactPage> pages,
        string specification)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(specification);

        if (string.IsNullOrWhiteSpace(specification))
            throw new InvalidDataException(
                "Reviewer page selection cannot be empty.");

        var byPage = new Dictionary<int, PrintableArtifactPage>();

        foreach (var page in pages)
        {
            if (page.PageNumber <= 0 ||
                !byPage.TryAdd(page.PageNumber, page))
            {
                throw new InvalidDataException(
                    "Printable source pages must have unique positive page numbers.");
            }
        }

        var requested = new List<int>();
        var seen = new HashSet<int>();

        foreach (var rawPart in specification.Split(','))
        {
            var part = rawPart.Trim();

            if (part.Length == 0)
                throw Invalid(specification);

            var dash = part.IndexOf('-');

            if (dash < 0)
            {
                Add(ParsePositive(part, specification));
                continue;
            }

            if (dash == 0 ||
                dash == part.Length - 1 ||
                part.IndexOf('-', dash + 1) >= 0)
            {
                throw Invalid(specification);
            }

            var start =
                ParsePositive(part[..dash].Trim(), specification);
            var end =
                ParsePositive(part[(dash + 1)..].Trim(), specification);

            if (end < start)
                throw Invalid(specification);

            if ((long)end - start + 1 > pages.Count)
                throw new InvalidDataException(
                    "Reviewer page range exceeds the available source page count.");

            for (var page = start; page <= end; page++)
                Add(page);
        }

        var selected = new List<PrintableArtifactPage>();

        foreach (var pageNumber in requested)
        {
            if (!byPage.TryGetValue(pageNumber, out var page))
                throw new InvalidDataException(
                    $"Reviewer page selection requests unavailable source page {pageNumber}.");

            selected.Add(page);
        }

        return selected;

        void Add(int page)
        {
            if (!seen.Add(page))
                throw new InvalidDataException(
                    $"Reviewer page selection contains duplicate page {page}.");

            requested.Add(page);
        }
    }

    private static int ParsePositive(
        string value,
        string specification)
    {
        if (!int.TryParse(value, out var page) || page <= 0)
            throw Invalid(specification);

        return page;
    }

    private static InvalidDataException Invalid(string specification) =>
        new(
            $"Invalid reviewer page selection '{specification}'. " +
            "Use positive page numbers and ascending ranges such as '11,13-17'.");
}

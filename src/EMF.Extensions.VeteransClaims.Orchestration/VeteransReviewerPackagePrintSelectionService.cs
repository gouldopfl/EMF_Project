namespace EMF.Extensions.VeteransClaims.Orchestration;

public sealed class VeteransReviewerPackagePrintSelectionService
{
    public VeteransReviewerPackagePrintSelection Normalize(
        VeteransReviewerPackagePrintSelection selection)
    {
        ArgumentNullException.ThrowIfNull(selection);

        return selection.Mode switch
        {
            VeteransReviewerPackagePrintSelectionMode.All =>
                NormalizeAll(selection),
            VeteransReviewerPackagePrintSelectionMode.CurrentPage =>
                NormalizeCurrentPage(selection),
            VeteransReviewerPackagePrintSelectionMode.PageRange =>
                NormalizePageRanges(selection),
            VeteransReviewerPackagePrintSelectionMode.Sections =>
                NormalizeSections(selection),
            _ => throw new InvalidOperationException(
                "Unsupported reviewer package print-selection mode.")
        };
    }

    private static VeteransReviewerPackagePrintSelection NormalizeAll(
        VeteransReviewerPackagePrintSelection selection)
    {
        RequireNoCurrentPage(selection);
        RequireNoPageRanges(selection);
        RequireNoSections(selection);

        return new VeteransReviewerPackagePrintSelection
        {
            Mode = VeteransReviewerPackagePrintSelectionMode.All
        };
    }

    private static VeteransReviewerPackagePrintSelection NormalizeCurrentPage(
        VeteransReviewerPackagePrintSelection selection)
    {
        if (selection.CurrentPage is null || selection.CurrentPage.Value <= 0)
        {
            throw new InvalidOperationException(
                "Current-page reprint selection requires a positive page number.");
        }

        RequireNoPageRanges(selection);
        RequireNoSections(selection);

        return new VeteransReviewerPackagePrintSelection
        {
            Mode = VeteransReviewerPackagePrintSelectionMode.CurrentPage,
            CurrentPage = selection.CurrentPage.Value
        };
    }

    private static VeteransReviewerPackagePrintSelection NormalizePageRanges(
        VeteransReviewerPackagePrintSelection selection)
    {
        RequireNoCurrentPage(selection);
        RequireNoSections(selection);

        if (selection.PageRanges.Count == 0)
        {
            throw new InvalidOperationException(
                "Page-range reprint selection requires at least one range.");
        }

        var ordered =
            selection.PageRanges
                .Select(
                    range =>
                    {
                        ArgumentNullException.ThrowIfNull(range);

                        if (range.StartPage <= 0 ||
                            range.EndPage < range.StartPage)
                        {
                            throw new InvalidOperationException(
                                "Reviewer package page range is invalid.");
                        }

                        return range;
                    })
                .OrderBy(range => range.StartPage)
                .ThenBy(range => range.EndPage)
                .ToArray();

        var merged = new List<VeteransReviewerPackagePageRange>();

        foreach (var range in ordered)
        {
            if (merged.Count == 0)
            {
                merged.Add(range);
                continue;
            }

            var last = merged[^1];

            if (range.StartPage <= last.EndPage ||
                (last.EndPage < int.MaxValue &&
                 range.StartPage == last.EndPage + 1))
            {
                merged[^1] =
                    new VeteransReviewerPackagePageRange
                    {
                        StartPage = last.StartPage,
                        EndPage = Math.Max(last.EndPage, range.EndPage)
                    };
                continue;
            }

            merged.Add(range);
        }

        return new VeteransReviewerPackagePrintSelection
        {
            Mode = VeteransReviewerPackagePrintSelectionMode.PageRange,
            PageRanges = merged
        };
    }

    private static VeteransReviewerPackagePrintSelection NormalizeSections(
        VeteransReviewerPackagePrintSelection selection)
    {
        RequireNoCurrentPage(selection);
        RequireNoPageRanges(selection);

        if (selection.SectionIds.Count == 0)
        {
            throw new InvalidOperationException(
                "Section reprint selection requires at least one section.");
        }

        var requested =
            selection.SectionIds
                .Select(
                    id =>
                    {
                        if (string.IsNullOrWhiteSpace(id))
                        {
                            throw new InvalidOperationException(
                                "Reviewer package section identifier is empty.");
                        }

                        return id.Trim();
                    })
                .ToHashSet(StringComparer.Ordinal);

        var known =
            VeteransReviewerPackageSectionCatalog.All
                .Select(section => section.Id)
                .ToHashSet(StringComparer.Ordinal);

        var unknown =
            requested
                .Where(id => !known.Contains(id))
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

        if (unknown.Length > 0)
        {
            throw new InvalidOperationException(
                "Reviewer package section selection contains unknown section " +
                $"identifier(s): {string.Join(", ", unknown)}.");
        }

        var normalized =
            VeteransReviewerPackageSectionCatalog.All
                .Where(section => requested.Contains(section.Id))
                .Select(section => section.Id)
                .ToArray();

        return new VeteransReviewerPackagePrintSelection
        {
            Mode = VeteransReviewerPackagePrintSelectionMode.Sections,
            SectionIds = normalized
        };
    }

    private static void RequireNoCurrentPage(
        VeteransReviewerPackagePrintSelection selection)
    {
        if (selection.CurrentPage is not null)
        {
            throw new InvalidOperationException(
                "Reviewer package print selection has conflicting current-page criteria.");
        }
    }

    private static void RequireNoPageRanges(
        VeteransReviewerPackagePrintSelection selection)
    {
        if (selection.PageRanges.Count > 0)
        {
            throw new InvalidOperationException(
                "Reviewer package print selection has conflicting page-range criteria.");
        }
    }

    private static void RequireNoSections(
        VeteransReviewerPackagePrintSelection selection)
    {
        if (selection.SectionIds.Count > 0)
        {
            throw new InvalidOperationException(
                "Reviewer package print selection has conflicting section criteria.");
        }
    }
}

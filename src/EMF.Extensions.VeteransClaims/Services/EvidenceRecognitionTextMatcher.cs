namespace EMF.Extensions.VeteransClaims.Services;

public static class EvidenceRecognitionTextMatcher
{
    public static bool ContainsTerm(
        string text,
        string term)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(term);

        var searchStart = 0;

        while (searchStart <= text.Length - term.Length)
        {
            var index =
                text.IndexOf(
                    term,
                    searchStart,
                    StringComparison.OrdinalIgnoreCase);

            if (index < 0)
                return false;

            var end = index + term.Length;

            if (HasLeftBoundary(text, term, index) &&
                HasRightBoundary(text, term, end))
            {
                return true;
            }

            searchStart = index + 1;
        }

        return false;
    }

    private static bool HasLeftBoundary(
        string text,
        string term,
        int index) =>
        !char.IsLetterOrDigit(term[0]) ||
        index == 0 ||
        !char.IsLetterOrDigit(text[index - 1]);

    private static bool HasRightBoundary(
        string text,
        string term,
        int end) =>
        !char.IsLetterOrDigit(term[^1]) ||
        end == text.Length ||
        !char.IsLetterOrDigit(text[end]);
}

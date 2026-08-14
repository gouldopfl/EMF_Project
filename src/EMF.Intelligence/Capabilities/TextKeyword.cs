namespace EMF.Intelligence.Capabilities;

public sealed class TextKeyword
{
    public TextKeyword(
        string term,
        IReadOnlyCollection<int> offsets)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            term);
        ArgumentNullException.ThrowIfNull(offsets);

        var positions = offsets.ToArray();

        if (positions.Length == 0)
        {
            throw new ArgumentException(
                "A keyword must have at least one offset.",
                nameof(offsets));
        }

        if (positions.Any(offset => offset < 0))
        {
            throw new ArgumentOutOfRangeException(
                nameof(offsets));
        }

        if (positions.Distinct().Count() !=
            positions.Length)
        {
            throw new ArgumentException(
                "Keyword offsets must be unique.",
                nameof(offsets));
        }

        Term = term;
        Offsets =
            positions.OrderBy(offset => offset)
                .ToArray();
    }

    public string Term { get; }

    public IReadOnlyList<int> Offsets { get; }

    public int Occurrences => Offsets.Count;

    public int FirstOffset => Offsets[0];
}

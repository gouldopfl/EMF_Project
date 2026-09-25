namespace EMF.Core.Models;

/// <summary>Native PDF coordinates in points, with a top-left origin.</summary>
public sealed record PrintableArtifactTextGeometry(
    double Width, double Height, bool ContainsGraphics,
    IReadOnlyList<PrintableArtifactGlyph> Glyphs);

public sealed record PrintableArtifactGlyph(
    string Text, double X, double EndX, double Top, double Bottom,
    double Left, double Right, double Baseline, string Font, double FontSize);

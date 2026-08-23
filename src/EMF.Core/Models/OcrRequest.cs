namespace EMF.Core.Models;

public sealed record OcrRequest(
    ReadOnlyMemory<byte> Image,
    string? Language = null);

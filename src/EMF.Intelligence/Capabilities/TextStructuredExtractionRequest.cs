namespace EMF.Intelligence.Capabilities;

public sealed class TextStructuredExtractionRequest
{
    public TextStructuredExtractionRequest(
        string text,
        string instruction,
        string jsonSchema,
        int? maximumOutputTokenCount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            instruction);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            jsonSchema);

        if (maximumOutputTokenCount is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maximumOutputTokenCount));
        }

        Text = text;
        Instruction = instruction;
        JsonSchema = jsonSchema;
        MaximumOutputTokenCount = maximumOutputTokenCount;
    }

    public string Text { get; }

    public string Instruction { get; }

    public string JsonSchema { get; }

    public int? MaximumOutputTokenCount { get; }
}

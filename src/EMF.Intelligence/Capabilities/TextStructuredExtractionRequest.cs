namespace EMF.Intelligence.Capabilities;

public sealed class TextStructuredExtractionRequest
{
    public TextStructuredExtractionRequest(
        string text,
        string instruction,
        string jsonSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            instruction);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            jsonSchema);

        Text = text;
        Instruction = instruction;
        JsonSchema = jsonSchema;
    }

    public string Text { get; }

    public string Instruction { get; }

    public string JsonSchema { get; }
}

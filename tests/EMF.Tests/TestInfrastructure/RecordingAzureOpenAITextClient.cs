using EMF.Intelligence.AzureOpenAI.Clients;
using EMF.Intelligence.AzureOpenAI.Models;

namespace EMF.Tests.TestInfrastructure;

internal sealed class
    RecordingAzureOpenAITextClient :
    IAzureOpenAITextClient
{
    public AzureOpenAITextCompletion Completion
    { get; set; } =
        new(
            "summary",
            "gpt-test",
            "operation-001",
            "Stop");

    public string? SystemInstruction
    { get; private set; }

    public string? Input { get; private set; }

    public Task<AzureOpenAITextCompletion>
        CompleteAsync(
            string systemInstruction,
            string input,
            CancellationToken cancellationToken =
                default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        SystemInstruction = systemInstruction;
        Input = input;

        return Task.FromResult(Completion);
    }
}

using EMF.Intelligence.AzureOpenAI.Exceptions;

namespace EMF.ConsoleApplication;

internal static class
    TextSummarizationConsoleRunner
{
    public static async Task<int> RunAsync(
        string text)
    {
        try
        {
            return await
                TextSummarizationConsoleExecution
                    .ExecuteAsync(
                        text);
        }
        catch (AzureOpenAIProviderException exception)
        {
            global::System.Console.Error.WriteLine(
                "Azure OpenAI request failed: " +
                exception.FailureKind + ".");
            return 1;
        }
        catch (ArgumentException exception)
        {
            global::System.Console.Error.WriteLine(
                exception.Message);
            return 2;
        }
        catch (InvalidOperationException exception)
        {
            global::System.Console.Error.WriteLine(
                exception.Message);
            return 2;
        }
    }
}

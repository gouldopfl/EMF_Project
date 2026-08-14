namespace EMF.ConsoleApplication;

internal static class
    TextSummarizationConsoleCommand
{
    public static async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length != 1)
        {
            global::System.Console.WriteLine(
                "Usage: emf intelligence summarize " +
                "<text-file>");
            return 2;
        }

        var sourcePath = Path.GetFullPath(args[0]);

        if (!File.Exists(sourcePath))
        {
            global::System.Console.Error.WriteLine(
                $"Text file not found: {sourcePath}");
            return 2;
        }

        var text =
            await File.ReadAllTextAsync(sourcePath);

        if (string.IsNullOrWhiteSpace(text))
        {
            global::System.Console.Error.WriteLine(
                "The text file is empty.");
            return 2;
        }

        return await TextSummarizationConsoleRunner
            .RunAsync(text);
    }
}

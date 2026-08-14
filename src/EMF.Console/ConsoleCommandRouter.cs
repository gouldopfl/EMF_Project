namespace EMF.ConsoleApplication;

public static class ConsoleCommandRouter
{
    public static Task<int> RunAsync(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
            return InventoryConsoleCommand.RunAsync(args);

        return args[0] switch
        {
            "inventory" =>
                InventoryConsoleCommand.RunAsync(args[1..]),

            "help" or "--help" or "-h" =>
                ShowHelpAsync(),

            _ => InventoryConsoleCommand.RunAsync(args)
        };
    }

    private static Task<int> ShowHelpAsync()
    {
        global::System.Console.WriteLine(
            "Evidence Management Framework");
        global::System.Console.WriteLine();
        global::System.Console.WriteLine(
            "Usage:");
        global::System.Console.WriteLine(
            "  emf inventory [source-path] [workflow-id]");
        global::System.Console.WriteLine();
        global::System.Console.WriteLine(
            "Legacy positional inventory arguments remain supported.");

        return Task.FromResult(0);
    }
}

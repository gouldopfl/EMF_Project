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

            "intelligence" =>
                IntelligenceConsoleCommand.RunAsync(args[1..]),

            "security" =>
                SecurityConsoleCommand.RunAsync(args[1..]),

            "help" or "--help" or "-h" =>
                ShowHelpAsync(),

            _ when IsLegacyInventoryInvocation(args) =>
                RunLegacyInventoryAsync(args),

            _ => ShowUnknownCommandAsync(args[0])
        };
    }

    private static bool IsLegacyInventoryInvocation(
        string[] args)
    {
        if (args.Length is < 1 or > 2)
            return false;

        var sourcePath = args[0];

        return Path.IsPathRooted(sourcePath) ||
            sourcePath.StartsWith(
                ".",
                StringComparison.Ordinal) ||
            sourcePath.Contains(
                Path.DirectorySeparatorChar) ||
            sourcePath.Contains(
                Path.AltDirectorySeparatorChar) ||
            Directory.Exists(sourcePath);
    }

    private static Task<int> RunLegacyInventoryAsync(
        string[] args)
    {
        global::System.Console.Error.WriteLine(
            "Legacy inventory syntax is deprecated; use 'emf inventory [source-path] [workflow-id]'.");

        return InventoryConsoleCommand.RunAsync(args);
    }

    private static Task<int> ShowUnknownCommandAsync(
        string command)
    {
        global::System.Console.Error.WriteLine(
            $"Unknown command '{command}'.");

        return Task.FromResult(2);
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
        global::System.Console.WriteLine(
            "  emf security audit verify <database-path>");
        global::System.Console.WriteLine(
            "  emf intelligence analyze [--promote] <text-file>");
        global::System.Console.WriteLine(
            "  emf intelligence summarize <text-file>");
        global::System.Console.WriteLine();
        global::System.Console.WriteLine(
            "Legacy positional inventory arguments are deprecated.");

        return Task.FromResult(0);
    }
}

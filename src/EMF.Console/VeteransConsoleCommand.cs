namespace EMF.ConsoleApplication;

public static class VeteransConsoleCommand
{
    public static Task<int> RunAsync(
        string[] args)
    {
        if (args.Length == 0)
        {
            global::System.Console.WriteLine(
                "Usage: emf veterans evidence develop <database-path> <plan-id> <evidence-gap-id>");
            return Task.FromResult(2);
        }

        global::System.Console.WriteLine(
            "Usage: emf veterans evidence develop <database-path> <plan-id> <evidence-gap-id>");

        return Task.FromResult(2);
    }
}

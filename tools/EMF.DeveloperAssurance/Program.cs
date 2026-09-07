namespace EMF.DeveloperAssurance;

internal static class Program
{
    private static int Main()
    {
        while (true)
        {
            ShowMenu();

            global::System.Console.Write("Selection: ");
            var selection =
                global::System.Console.ReadLine()?.Trim();

            global::System.Console.WriteLine();

            switch (selection)
            {
                case "1":
                    ShowPending("Perform Current Task");
                    break;

                case "2":
                    ShowPending("Validate Current File");
                    break;

                case "3":
                    ShowPending("Validate Current Project");
                    break;

                case "4":
                    ShowPending("Run Focused Tests");
                    break;

                case "5":
                    ShowPending("Run Project Tests");
                    break;

                case "6":
                    ShowPending("Run Full Regression");
                    break;

                case "7":
                    ShowPending("Run Architecture / Code Auditor");
                    break;

                case "8":
                    ShowPending("Run Complete Development Gate");
                    break;

                case "9":
                    ShowPending("Show Project Assurance Report");
                    break;

                case "10":
                    ShowPending("Generate Project Assurance Package");
                    break;

                case "11":
                    ShowPending("Show Findings / Next Work");
                    break;

                case "12":
                    new GitCheckpointStatusOperation().Run();
                    break;

                case "0":
                    global::System.Console.WriteLine(
                        "Developer Assurance closed.");
                    return 0;

                default:
                    global::System.Console.WriteLine(
                        "Unknown selection.");
                    break;
            }

            global::System.Console.WriteLine();
        }
    }

    private static void ShowMenu()
    {
        global::System.Console.WriteLine(
            "===== EMF DEVELOPER ASSURANCE =====");
        global::System.Console.WriteLine();

        global::System.Console.WriteLine("CURRENT WORK");
        global::System.Console.WriteLine(
            "1. Perform Current Task");
        global::System.Console.WriteLine(
            "2. Validate Current File");
        global::System.Console.WriteLine(
            "3. Validate Current Project");
        global::System.Console.WriteLine();

        global::System.Console.WriteLine("TESTING");
        global::System.Console.WriteLine(
            "4. Run Focused Tests");
        global::System.Console.WriteLine(
            "5. Run Project Tests");
        global::System.Console.WriteLine(
            "6. Run Full Regression");
        global::System.Console.WriteLine();

        global::System.Console.WriteLine("ASSURANCE");
        global::System.Console.WriteLine(
            "7. Run Architecture / Code Auditor");
        global::System.Console.WriteLine(
            "8. Run Complete Development Gate");
        global::System.Console.WriteLine();

        global::System.Console.WriteLine("REPORTING");
        global::System.Console.WriteLine(
            "9. Show Project Assurance Report");
        global::System.Console.WriteLine(
            "10. Generate Project Assurance Package");
        global::System.Console.WriteLine();

        global::System.Console.WriteLine("STATUS");
        global::System.Console.WriteLine(
            "11. Show Findings / Next Work");
        global::System.Console.WriteLine(
            "12. Show Git / Checkpoint Status");
        global::System.Console.WriteLine();

        global::System.Console.WriteLine("0. Exit");
        global::System.Console.WriteLine();
    }

    private static void ShowPending(string operation)
    {
        global::System.Console.WriteLine(
            $"{operation}: not wired yet.");
    }
}

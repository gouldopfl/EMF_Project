using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;

namespace EMF.ConsoleApplication;

public static class SecurityConsoleCommand
{
    public static async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length != 3 ||
            args[0] != "audit" ||
            args[1] != "verify")
        {
            ShowUsage();
            return 2;
        }

        var databasePath =
            Path.GetFullPath(args[2]);

        if (!File.Exists(databasePath))
        {
            global::System.Console.Error.WriteLine(
                $"Security audit database not found: {databasePath}");
            return 2;
        }

        SecurityAuditIntegrityVerificationResult result;

        try
        {
            result =
                await new
                    SqliteSecurityAuditIntegrityVerifier(
                        databasePath)
                    .VerifyAsync();
        }
        catch (SqliteException exception)
        {
            global::System.Console.Error.WriteLine(
                "Security audit verification could not run: " +
                exception.Message);
            return 2;
        }

        if (!result.IsValid)
        {
            global::System.Console.Error.WriteLine(
                "Security audit integrity verification failed.");

            global::System.Console.Error.WriteLine(
                $"Invalid record : {result.InvalidRecordId}");

            global::System.Console.Error.WriteLine(
                $"Reason         : {result.FailureReason}");

            return 1;
        }

        global::System.Console.WriteLine(
            "Security audit integrity verified.");

        global::System.Console.WriteLine(
            $"Protected records : {result.ProtectedRecordCount}");

        global::System.Console.WriteLine(
            $"Legacy records    : {result.LegacyRecordCount}");

        return 0;
    }

    private static void ShowUsage()
    {
        global::System.Console.WriteLine(
            "Usage: emf security audit verify <database-path>");
    }
}

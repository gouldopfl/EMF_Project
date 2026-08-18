using EMF.Security.Persistence.Sqlite.Auditing;
using Microsoft.Data.Sqlite;

namespace EMF.ConsoleApplication;

public static class SecurityConsoleCommand
{
    public static async Task<int> RunAsync(
        string[] args)
    {
        if (args.Length == 4 &&
            args[0] == "audit" &&
            args[1] == "report")
        {
            return await RunReportAsync(args[2], args[3]);
        }

        if (args.Length == 7 &&
            args[0] == "workflow" &&
            args[1] == "recover")
        {
            return await
                SecurityWorkflowRecoveryConsoleCommand
                    .RunAsync(args[2..]);
        }

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

        if (result.LastProtectedRecordId.HasValue)
        {
            global::System.Console.WriteLine(
                $"Chain head record  : {result.LastProtectedRecordId}");

            global::System.Console.WriteLine(
                $"Chain head SHA-256 : {result.ChainHeadHash}");
        }

        return 0;
    }


    private static async Task<int> RunReportAsync(
        string databaseArgument,
        string operation)
    {
        var databasePath =
            Path.GetFullPath(databaseArgument);

        if (!File.Exists(databasePath))
        {
            global::System.Console.Error.WriteLine(
                $"Security audit database not found: {databasePath}");
            return 2;
        }

        SecurityAuditOperationReport report;

        try
        {
            report =
                await new SqliteSecurityAuditOperationReporter(
                    databasePath)
                    .CreateAsync(operation);
        }
        catch (Exception exception)
            when (exception is SqliteException
                or InvalidOperationException)
        {
            global::System.Console.Error.WriteLine(
                "Security audit report could not run: " +
                exception.Message);
            return 1;
        }

        global::System.Console.WriteLine(
            $"Operation   : {report.Operation}");
        global::System.Console.WriteLine(
            $"Total       : {report.TotalCount}");

        foreach (var outcome in report.OutcomeCounts)
        {
            global::System.Console.WriteLine(
                $"{outcome.Key,-11} : {outcome.Value}");
        }

        global::System.Console.WriteLine(
            $"First UTC   : {report.FirstOccurredUtc:O}");
        global::System.Console.WriteLine(
            $"Last UTC    : {report.LastOccurredUtc:O}");
        global::System.Console.WriteLine(
            $"Chain head  : {report.ChainHeadHash}");

        return 0;
    }
    private static void ShowUsage()
    {
        global::System.Console.WriteLine(
            "Usage: emf security audit verify <database-path>");
        global::System.Console.WriteLine(
            "Usage: emf security audit report <database-path> <operation>");
        global::System.Console.WriteLine(
            "Usage: emf security workflow recover <database-path> <workflow-id> <activity-id> <new-claim-id> <abandoned-before-utc>");
    }
}

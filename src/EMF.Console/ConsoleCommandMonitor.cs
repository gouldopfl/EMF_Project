using EMF.Core.Contracts;
using EMF.Core.Diagnostics;

namespace EMF.ConsoleApplication;

internal sealed class ConsoleCommandMonitor :
    ICommandMonitor
{
    public void Report(CommandMonitorEvent status)
    {
        ArgumentNullException.ThrowIfNull(status);

        var progress =
            status.Current is not null &&
            status.Total is not null
                ? $"[{status.Current}/{status.Total}] "
                : string.Empty;

        global::System.Console.WriteLine(
            $"{status.Timestamp:HH:mm:ss} UTC  " +
            $"{status.Area,-8}  " +
            progress +
            ConsoleTextSanitizer.Sanitize(
                status.Message));

        global::System.Console.Out.Flush();
    }
}

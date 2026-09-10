using EMF.Core.Diagnostics;

namespace EMF.Core.Contracts;

public interface ICommandMonitor
{
    void Report(CommandMonitorEvent status);
}

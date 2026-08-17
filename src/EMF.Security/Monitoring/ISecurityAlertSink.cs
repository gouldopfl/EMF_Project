namespace EMF.Security.Monitoring;

public interface ISecurityAlertSink
{
    Task WriteAsync(
        SecurityAlert alert,
        CancellationToken cancellationToken = default);
}

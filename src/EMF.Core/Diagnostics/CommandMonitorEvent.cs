namespace EMF.Core.Diagnostics;

public sealed record CommandMonitorEvent(
    DateTimeOffset Timestamp,
    string Area,
    string Message,
    int? Current = null,
    int? Total = null);

namespace EMF.Tests.TestInfrastructure;

public sealed class AzureMonitorIntegrationFactAttribute :
    FactAttribute
{
    public AzureMonitorIntegrationFactAttribute()
    {
        if (!string.Equals(
                Environment.GetEnvironmentVariable(
                    "EMF_AZURE_MONITOR_LIVE_TESTS"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            Skip = "Azure Monitor live tests are disabled.";
        }
    }
}

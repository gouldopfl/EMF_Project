namespace EMF.Discovery.Models;

public sealed class DiscoveryStatistics
{
    public int DirectoriesDiscovered { get; set; }

    public int FilesDiscovered { get; set; }

    public long TotalBytes { get; set; }

    public TimeSpan Elapsed { get; set; }
}
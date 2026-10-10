using EMF.Core.Contracts.Zip;

namespace EMF.Orchestration.Models;

// Representation/work ceilings. These do not bound total heap or process memory.
public static class ZipCentralDirectoryLimits
{
    public const int MaximumParentBytes = (int)ZipNumericLimits.Parent;
    public const int MaximumEntries = ZipNumericLimits.Entries;
    public const int MaximumFieldBytes = ushort.MaxValue;
    public const int MaximumDirectoryBytes = MaximumParentBytes - 22;
    public const int EndSearchBytes = ushort.MaxValue + 4;
    public const string ProfileVersion = "zip-central-directory-v1";
}

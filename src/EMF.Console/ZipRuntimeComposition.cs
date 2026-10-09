using EMF.Orchestration.Models;
using EMF.Orchestration.Services;
namespace EMF.ConsoleApplication;

public sealed class ZipRuntimeComposition
{
    public static ZipRuntimeComposition Default { get; } = new();
    public ZipParentAllocationProfile Profile { get; }
    public ZipParentReadAdmission Admission => ZipParentReadAdmission.ProcessWide;
    public ZipRuntimeComposition(ZipParentAllocationProfile? profile = null) => Profile = profile ?? new();
}

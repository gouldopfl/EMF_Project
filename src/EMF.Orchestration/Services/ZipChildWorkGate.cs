namespace EMF.Orchestration.Services;
// Shared by extraction and scanning: one owned child plaintext lifetime across instances/stages.
internal static class ZipChildWorkGate
{
    internal static readonly SemaphoreSlim Gate=new(1,1);
}

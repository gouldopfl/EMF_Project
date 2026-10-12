namespace EMF.Tests;

// These tests share production's process-wide parent and child gates.
// A single collection keeps gate-sensitive test workflows from competing
// with each other while unrelated collections remain parallel.
[CollectionDefinition(Name)]
public sealed class ZipProcessWideGateCollection
{
    public const string Name = "ZIP process-wide gates";
}

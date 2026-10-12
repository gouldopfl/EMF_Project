namespace EMF.Tests;

// Local protocol and independent database probes use real wall-clock deadlines.
// Run these fixtures without competing test collections for reliable timing.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class TimingSensitiveFixtureCollection
{
    public const string Name = "Timing-sensitive local fixtures";
}

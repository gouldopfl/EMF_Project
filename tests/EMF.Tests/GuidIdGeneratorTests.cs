using EMF.Common;

namespace EMF.Tests;

public sealed class GuidIdGeneratorTests
{
    [Fact]
    public void Generate_ReturnsNonEmptyGuidWithoutSeparators()
    {
        var generator = new GuidIdGenerator();

        var value = generator.Generate();

        Assert.NotNull(value);
        Assert.NotEmpty(value);
        Assert.Equal(32, value.Length);
        Assert.DoesNotContain("-", value);
        Assert.True(Guid.TryParseExact(value, "N", out _));
    }

    [Fact]
    public void Generate_ReturnsUniqueValues()
    {
        var generator = new GuidIdGenerator();

        var first = generator.Generate();
        var second = generator.Generate();

        Assert.NotEqual(first, second);
    }
}

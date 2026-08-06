using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

public class RngTests
{
    [Fact]
    public void SameSeed_ProducesIdenticalSequence()
    {
        var a = new RngState(12345);
        var b = new RngState(12345);

        for (int i = 0; i < 1000; i++)
        {
            Assert.Equal(Rng.NextUInt(ref a), Rng.NextUInt(ref b));
        }
    }

    [Fact]
    public void DifferentSeeds_DivergeImmediately()
    {
        var a = new RngState(1);
        var b = new RngState(2);

        Assert.NotEqual(Rng.NextUInt(ref a), Rng.NextUInt(ref b));
    }

    [Fact]
    public void NextDouble_IsWithinUnitRange()
    {
        var rng = new RngState(999);

        for (int i = 0; i < 10_000; i++)
        {
            double d = Rng.NextDouble(ref rng);
            Assert.InRange(d, 0.0, 1.0);
        }
    }

    [Fact]
    public void Range_RespectsBounds()
    {
        var rng = new RngState(42);

        for (int i = 0; i < 10_000; i++)
        {
            double v = Rng.Range(ref rng, -3.0, 3.0);
            Assert.InRange(v, -3.0, 3.0);
        }
    }
}

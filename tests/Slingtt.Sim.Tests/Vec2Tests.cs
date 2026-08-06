using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

public class Vec2Tests
{
    [Fact]
    public void Length_UsesExplicitSqrt_NotHypot()
    {
        var v = new Vec2(3, 4);
        Assert.Equal(5.0, v.Length());
    }

    [Fact]
    public void Normalized_HasUnitLength()
    {
        var v = new Vec2(7, -2).Normalized();
        Assert.Equal(1.0, v.Length(), precision: 12);
    }

    [Fact]
    public void Normalized_OfZero_ReturnsZero()
    {
        Assert.Equal(Vec2.Zero, Vec2.Zero.Normalized());
    }

    [Fact]
    public void Addition_IsComponentwise()
    {
        var result = new Vec2(1, 2) + new Vec2(3, 4);
        Assert.Equal(new Vec2(4, 6), result);
    }
}

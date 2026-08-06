using System.Reflection;
using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Enforces 20-architecture.md §7.1: sim/ is its own assembly with no reference to
// the Godot assembly. An engine import must be a build error, and this test is the
// belt-and-suspenders check that the reference graph never grows one back in.
public class PurityTests
{
    [Fact]
    public void SimAssembly_HasNoGodotReference()
    {
        Assembly simAssembly = typeof(Vec2).Assembly;
        IEnumerable<string?> referenced = simAssembly.GetReferencedAssemblies().Select(a => a.Name);

        Assert.DoesNotContain(referenced, name =>
            name != null && name.Contains("Godot", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void SimAssembly_LoadsWithNoEngineAssemblyPresent()
    {
        // This test running at all, in a plain xunit host with no Godot runtime
        // loaded, is the purity proof from 21-determinism-spec.md §8.
        World world = TestWorlds.OneOnOne();
        Assert.Equal(0, world.Tick);
    }
}

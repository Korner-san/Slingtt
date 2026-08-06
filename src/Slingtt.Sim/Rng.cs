namespace Slingtt.Sim;

// mulberry32 — exactly-specified 32-bit integer operations only, no floats, no
// platform variance. See 21-determinism-spec.md §6. RNG state lives in the world,
// never in a static/module-level variable.
public struct RngState
{
    public uint S;

    public RngState(uint seed) => S = seed;
}

public static class Rng
{
    public static uint NextUInt(ref RngState rng)
    {
        unchecked
        {
            rng.S += 0x6D2B79F5u;
            uint t = rng.S;
            t = (t ^ (t >> 15)) * (t | 1u);
            t ^= t + (t ^ (t >> 7)) * (t | 61u);
            return t ^ (t >> 14);
        }
    }

    public static double NextDouble(ref RngState rng)
        => NextUInt(ref rng) / 4294967296.0; // 2^32, exact in double

    public static double Range(ref RngState rng, double min, double max)
        => min + (max - min) * NextDouble(ref rng);
}

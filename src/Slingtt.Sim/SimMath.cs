namespace Slingtt.Sim;

// The web original is JavaScript, and its damage/stat numbers all pass through
// Math.round, which rounds half AWAY from zero for positives (0.5 -> 1).
// System.Math.Round uses banker's rounding (0.5 -> 0), so porting it directly
// would silently shift every damage roll. Everything numeric that crossed over
// from TypeScript uses these helpers instead.
public static class SimMath
{
    /// <summary>JavaScript Math.round semantics: floor(x + 0.5).</summary>
    public static double RoundJs(double x) => Math.Floor(x + 0.5);

    /// <summary>JavaScript Math.round, narrowed to int.</summary>
    public static int RoundJsInt(double x) => (int)Math.Floor(x + 0.5);

    public static double Clamp(double v, double min, double max)
        => v < min ? min : (v > max ? max : v);
}

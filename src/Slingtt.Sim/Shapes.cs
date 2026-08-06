namespace Slingtt.Sim;

public enum ShapeType
{
    RadialArms, // arms evenly spaced around a world-fixed base direction (+X, angle 0)
    Lines,      // explicit line offsets relative to a caller-supplied direction
    Rings,      // a circular area around the origin
}

/// <summary>Data-driven ultimate/ability geometry — Prompt 1 moves shape data
/// that used to be a hardcoded per-Kind switch in Ultimates.cs into this struct,
/// sourced from content JSON. RadialArms is anchored to world space (its
/// zero-angle is the world +X axis), which is what makes a Crossing Slash always
/// a "+" no matter which way the hero was travelling. Lines is anchored to a
/// caller-supplied direction (the actor's travel direction, for Piercing Ray) —
/// its zero-angle rotates with that direction. Rings ignores direction.
///
/// RotationOffset and ExcludeAlreadyHit are plumbed for later prompts and always
/// their default (0 / false) today — nothing currently sets them to anything
/// else, and ShapeResolver only reads ExcludeAlreadyHit's presence, never acts
/// on it yet.</summary>
public readonly struct ShapeDef
{
    public ShapeType Type { get; init; }

    /// <summary>RadialArms: how many bidirectional lines, evenly spaced
    /// 180°/ArmCount apart (each line already covers both its angle and
    /// angle+180°, so full 360° coverage only needs half that many entries).</summary>
    public int ArmCount { get; init; }

    /// <summary>Lines: bidirectional line angles in radians, relative to the
    /// caller-supplied base direction. Null or empty means "just the base
    /// direction itself" (a single line, offset 0).</summary>
    public IReadOnlyList<double>? LineAngles { get; init; }

    /// <summary>RadialArms, Lines: hit-test half-width band around each line.</summary>
    public double Width { get; init; }

    /// <summary>Additional rotation (radians) composed with every arm/line angle.
    /// Plumbed, unused — always 0 today.</summary>
    public double RotationOffset { get; init; }

    /// <summary>Rings: radius of the circular area.</summary>
    public double Radius { get; init; }

    /// <summary>Plumbed, unused — always false today. Intended for a future
    /// resolution step that must not re-hit a target a prior beat in the same
    /// resolution already caught.</summary>
    public bool ExcludeAlreadyHit { get; init; }
}

/// <summary>The one shared shape-overlap query for ultimates (and, later, other
/// shape-driven systems) — continuous-space, no grid/tile approximation. This
/// replaces the per-Kind arm-building switch that used to live in Ultimates.cs;
/// the geometry itself now comes from a ShapeDef instead of being rebuilt in
/// code for each ultimate kind.</summary>
public static class ShapeResolver
{
    // A handful of the angles ArmDirections/exact rotation actually need today
    // (multiples of 45°) get exact values instead of Math.Cos/Sin's floating-
    // point residue from Math.PI's imprecision (Math.Cos(Math.PI/2) is not
    // exactly 0) — this is what makes the refactor reproduce the original
    // hand-written arm vectors ((1,0),(0,1),(-y,x)...) bit-for-bit rather than
    // merely approximating them, which the golden-master tests require.
    private static readonly double S = Math.Sqrt(0.5);

    private static (double X, double Y) RotateExact(double x, double y, double angleRadians)
    {
        double eighths = angleRadians / (Math.PI / 4);
        int k = (int)Math.Round(eighths);
        if (Math.Abs(eighths - k) < 1e-9)
        {
            int idx = ((k % 8) + 8) % 8;
            (double c, double s) = idx switch
            {
                0 => (1.0, 0.0),
                1 => (S, S),
                2 => (0.0, 1.0),
                3 => (-S, S),
                4 => (-1.0, 0.0),
                5 => (-S, -S),
                6 => (0.0, -1.0),
                _ => (S, -S), // 7
            };
            return (x * c - y * s, x * s + y * c);
        }
        double cc = Math.Cos(angleRadians);
        double ss = Math.Sin(angleRadians);
        return (x * cc - y * ss, x * ss + y * cc);
    }

    /// <summary>Perpendicular distance from point p to the infinite line through
    /// origin with unit direction (ux, uy).</summary>
    private static double LineDistance(double px, double py, double ox, double oy, double ux, double uy)
        => Math.Abs((px - ox) * uy - (py - oy) * ux);

    /// <summary>The unit direction vectors the shape's arms/lines run along, each
    /// a bidirectional infinite line through the origin. baseDir is only
    /// meaningful for Lines — RadialArms is always world-anchored to +X.</summary>
    public static IEnumerable<(double X, double Y)> Directions(ShapeDef shape, Vec2 baseDir)
    {
        switch (shape.Type)
        {
            case ShapeType.RadialArms:
            {
                int count = Math.Max(shape.ArmCount, 1);
                double step = Math.PI / count;
                for (int i = 0; i < count; i++)
                {
                    yield return RotateExact(1.0, 0.0, i * step + shape.RotationOffset);
                }
                break;
            }
            case ShapeType.Lines:
            {
                IReadOnlyList<double> offsets = shape.LineAngles is { Count: > 0 } l ? l : new[] { 0.0 };
                foreach (double offset in offsets)
                {
                    yield return RotateExact(baseDir.X, baseDir.Y, offset + shape.RotationOffset);
                }
                break;
            }
        }
    }

    /// <summary>True if target's collision circle overlaps a shape query fired
    /// from origin. Rings ignores baseDir entirely.</summary>
    public static bool Overlaps(ShapeDef shape, Vec2 origin, Vec2 baseDir, Actor target)
    {
        if (shape.Type == ShapeType.Rings)
        {
            return origin.DistanceTo(target.Pos) <= shape.Radius;
        }

        foreach ((double ux, double uy) in Directions(shape, baseDir))
        {
            if (LineDistance(target.Pos.X, target.Pos.Y, origin.X, origin.Y, ux, uy) <= shape.Width / 2 + target.Radius)
            {
                return true;
            }
        }
        return false;
    }
}

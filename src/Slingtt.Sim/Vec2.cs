namespace Slingtt.Sim;

// Deliberately double-precision. Godot's Vector2/Vector3 are 32-bit and MUST NOT
// appear anywhere in this assembly — see 21-determinism-spec.md §3.
public struct Vec2
{
    public double X;
    public double Y;

    public Vec2(double x, double y)
    {
        X = x;
        Y = y;
    }

    public static readonly Vec2 Zero = new(0, 0);

    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Y + b.Y);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Y - b.Y);
    public static Vec2 operator -(Vec2 a) => new(-a.X, -a.Y);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Y * s);
    public static Vec2 operator *(double s, Vec2 a) => new(a.X * s, a.Y * s);

    public double Dot(Vec2 other) => X * other.X + Y * other.Y;

    // sqrt is the one transcendental IEEE-754 specifies exactly — safe to use.
    // hypot is banned: 21-determinism-spec.md §4.
    public double Length() => Math.Sqrt(X * X + Y * Y);

    public double LengthSquared() => X * X + Y * Y;

    public double DistanceTo(Vec2 other)
    {
        double dx = X - other.X;
        double dy = Y - other.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    public Vec2 Normalized()
    {
        double len = Length();
        if (len == 0)
        {
            return Zero;
        }
        return new Vec2(X / len, Y / len);
    }

    /// <summary>Reflect about unit normal n, scaled by restitution: v' = r * (v - 2(v·n)n).</summary>
    public Vec2 Reflected(Vec2 n, double restitution)
    {
        double d = Dot(n);
        return new Vec2((X - 2 * d * n.X) * restitution, (Y - 2 * d * n.Y) * restitution);
    }

    public override string ToString() => $"({X}, {Y})";
}

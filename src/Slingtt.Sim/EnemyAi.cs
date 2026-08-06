namespace Slingtt.Sim;

// Enemy launch decisions are part of the simulation and draw from the world RNG,
// so a replay needs only the *player's* inputs to reproduce a battle exactly.
// Phase 1 AI: aim at the nearest living hero with a little angular jitter and a
// strong draw.
public static class EnemyAi
{
    private const double JitterRad = 0.06;

    public static LaunchInput DecideLaunch(World world, Actor self)
    {
        Actor? target = null;
        double best = double.PositiveInfinity;
        foreach (Actor h in world.Actors)
        {
            if (h.Team != Team.Hero || !h.IsAlive)
            {
                continue;
            }
            double dx = h.Pos.X - self.Pos.X;
            double dy = h.Pos.Y - self.Pos.Y;
            double d = dx * dx + dy * dy;
            if (d < best)
            {
                best = d;
                target = h;
            }
        }

        if (target is null)
        {
            // No heroes left; settling will end the battle on the next step.
            return new LaunchInput { DirX = 0, DirY = -1, DrawRatio = 1 };
        }

        double baseAngle = Math.Atan2(target.Pos.Y - self.Pos.Y, target.Pos.X - self.Pos.X);
        double angle = baseAngle + Rng.Range(ref world.Rng, -JitterRad, JitterRad);
        return new LaunchInput
        {
            DirX = Math.Cos(angle),
            DirY = Math.Sin(angle),
            DrawRatio = Rng.Range(ref world.Rng, 0.75, 1.0),
        };
    }
}

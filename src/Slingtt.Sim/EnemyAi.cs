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
        // Prompt 7 — Chain enemies aim for the midpoint between the two active
        // heroes instead of the nearest one: it's not a solved bounce (the
        // elastic reflection off whichever hero it actually contacts first
        // isn't inverted here), just a heuristic that happens to fire straight
        // through both when they're clustered close together — which is
        // exactly the scenario this enemy exists to punish, since clustering
        // is also what Prompt 5's contact combo rewards. Falls through to the
        // normal nearest-hero aim when there isn't a second active hero to aim
        // between.
        if (self.Weapon.Type == WeaponType.Chain && TryAimBetweenActiveHeroes(world, self, out LaunchInput chainAim))
        {
            return chainAim;
        }

        Actor? target = null;
        double best = double.PositiveInfinity;
        foreach (Actor h in world.Actors)
        {
            if (h.Team != Team.Hero || !h.IsAlive || h.IsBenched)
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

        return AimAt(world, self, target.Pos);
    }

    private static bool TryAimBetweenActiveHeroes(World world, Actor self, out LaunchInput input)
    {
        Actor? first = null;
        Actor? second = null;
        foreach (Actor h in world.Actors)
        {
            if (h.Team != Team.Hero || !h.IsAlive || h.IsBenched)
            {
                continue;
            }
            if (first is null)
            {
                first = h;
            }
            else if (second is null)
            {
                second = h;
                break;
            }
        }
        if (first is null || second is null)
        {
            input = default;
            return false;
        }

        var midpoint = new Vec2((first.Pos.X + second.Pos.X) / 2, (first.Pos.Y + second.Pos.Y) / 2);
        input = AimAt(world, self, midpoint);
        return true;
    }

    private static LaunchInput AimAt(World world, Actor self, Vec2 point)
    {
        double baseAngle = Math.Atan2(point.Y - self.Pos.Y, point.X - self.Pos.X);
        double angle = baseAngle + Rng.Range(ref world.Rng, -JitterRad, JitterRad);
        return new LaunchInput
        {
            DirX = Math.Cos(angle),
            DirY = Math.Sin(angle),
            DrawRatio = Rng.Range(ref world.Rng, 0.75, 1.0),
        };
    }
}

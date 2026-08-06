namespace Slingtt.Sim;

// Weapon ultimates fire in the Ultimate phase, after travel fully resolves, from
// the actor's final position. They are instantaneous shape queries, not moving
// projectiles: a beam is a line query, a cross is two, an aftershock is a circle
// query. Targets are visited in stable actor order so variance draws stay
// deterministic.
public static class Ultimates
{
    /// <summary>Perpendicular distance from point p to the infinite line through
    /// origin with unit direction (ux, uy).</summary>
    private static double LineDistance(double px, double py, double ox, double oy, double ux, double uy)
        => Math.Abs((px - ox) * uy - (py - oy) * ux);

    public static void FireWeaponUltimate(World world, SimConfig cfg, Actor self)
    {
        if (self.Weapon.Ultimate is not { } spec)
        {
            return;
        }

        double x = self.Pos.X;
        double y = self.Pos.Y;
        double dirX = self.LastTravelDir.X;
        double dirY = self.LastTravelDir.Y;
        double dirLen = Math.Sqrt(dirX * dirX + dirY * dirY);
        if (dirLen == 0)
        {
            dirLen = 1;
        }
        dirX /= dirLen;
        dirY /= dirLen;

        world.Events.Add(new SimEvent
        {
            Kind = SimEventKind.WeaponUltimate,
            ActorId = self.Id,
            WeaponUlt = spec,
            Pos = self.Pos,
            Dir = new Vec2(dirX, dirY),
        });

        var arms = new List<(double X, double Y)>();
        double circleRadius = 0;
        double width = 0;
        double s = Math.Sqrt(0.5); // Math.SQRT1_2

        switch (spec.Kind)
        {
            case WeaponUltKind.Cross:
                width = spec.Width;
                arms.Add((1, 0));
                arms.Add((0, 1));
                if (spec.DoubleCross)
                {
                    arms.Add((s, s));
                    arms.Add((s, -s));
                }
                break;
            case WeaponUltKind.Beam:
                width = spec.Width;
                arms.Add((dirX, dirY));
                if (spec.SecondaryBeam)
                {
                    arms.Add((-dirY, dirX));
                }
                break;
            case WeaponUltKind.Aftershock:
                circleRadius = spec.Radius;
                break;
        }

        foreach (Actor target in world.Actors)
        {
            if (target.Team == self.Team || !target.IsAlive)
            {
                continue;
            }

            bool inShape = false;
            if (spec.Kind == WeaponUltKind.Aftershock)
            {
                inShape = self.Pos.DistanceTo(target.Pos) <= circleRadius;
            }
            else
            {
                foreach ((double ux, double uy) in arms)
                {
                    if (LineDistance(target.Pos.X, target.Pos.Y, x, y, ux, uy) <= width / 2 + target.Radius)
                    {
                        inShape = true;
                        break; // an enemy caught by two arms is hit once
                    }
                }
            }
            if (!inShape)
            {
                continue;
            }

            Damage.Apply(world, cfg, self, target, spec.DmgMult, HitKind.Ultimate, target.Pos);
            if (spec.Kind == WeaponUltKind.Aftershock && spec.StunTurns > 0 && target.IsAlive)
            {
                target.StunnedTurns = Math.Max(target.StunnedTurns, spec.StunTurns);
            }
        }
    }
}

namespace Slingtt.Sim;

// Swept (continuous) collision. At MaxSpeed 26 u/s and dt 1/120 an actor moves
// ~0.22u/tick against radius 0.5. Discrete overlap tests mostly work, which is
// exactly what makes them dangerous. Everything here solves for time of impact
// within the tick and resolves impacts in temporal order.
public static class Collision
{
    private const double Eps = 1e-4;
    private const int MaxSubSteps = 8; // corners + multi-enemy chains within one tick

    private enum ToiKind
    {
        Wall,
        Actor,
    }

    /// <summary>Earliest wall TOI for a circle moving at vel over time tr, or
    /// PositiveInfinity. Writes the surface normal into nx/ny on a hit.</summary>
    private static double SweepWalls(Actor self, double boundsW, double boundsH, double tr, out double nx, out double ny)
    {
        double r = self.Radius;
        double best = double.PositiveInfinity;
        nx = 0;
        ny = 0;

        if (self.Vel.X < 0)
        {
            double t = (r - self.Pos.X) / self.Vel.X;
            if (t >= 0 && t <= tr && t < best)
            {
                best = t;
                nx = 1;
                ny = 0;
            }
        }
        else if (self.Vel.X > 0)
        {
            double t = (boundsW - r - self.Pos.X) / self.Vel.X;
            if (t >= 0 && t <= tr && t < best)
            {
                best = t;
                nx = -1;
                ny = 0;
            }
        }

        if (self.Vel.Y < 0)
        {
            double t = (r - self.Pos.Y) / self.Vel.Y;
            if (t >= 0 && t <= tr && t < best)
            {
                best = t;
                nx = 0;
                ny = 1;
            }
        }
        else if (self.Vel.Y > 0)
        {
            double t = (boundsH - r - self.Pos.Y) / self.Vel.Y;
            if (t >= 0 && t <= tr && t < best)
            {
                best = t;
                nx = 0;
                ny = -1;
            }
        }

        return best;
    }

    /// <summary>Earliest circle-circle TOI against a static target, or
    /// PositiveInfinity. Skips pairs already overlapping (lance pass-through
    /// handles those via PiercedIds) and pairs moving apart.</summary>
    private static double SweepActor(Actor self, Actor target, double tr)
    {
        double dx = self.Pos.X - target.Pos.X;
        double dy = self.Pos.Y - target.Pos.Y;
        double radiusSum = self.Radius + target.Radius;
        double c = dx * dx + dy * dy - radiusSum * radiusSum;
        if (c <= 0)
        {
            return double.PositiveInfinity; // already overlapping at tick start
        }
        double vx = self.Vel.X;
        double vy = self.Vel.Y;
        double b = 2 * (dx * vx + dy * vy);
        if (b >= 0)
        {
            return double.PositiveInfinity; // moving apart
        }
        double a = vx * vx + vy * vy;
        if (a == 0)
        {
            return double.PositiveInfinity;
        }
        double disc = b * b - 4 * a * c;
        if (disc < 0)
        {
            return double.PositiveInfinity;
        }
        double t = (-b - Math.Sqrt(disc)) / (2 * a);
        return t >= 0 && t <= tr ? t : double.PositiveInfinity;
    }

    /// <summary>Integrate the active actor over one tick of duration dt, resolving
    /// wall and enemy impacts in temporal order. Returns true if a weapon behavior
    /// stopped the travel (hammer). Mutates world state; emits events.</summary>
    public static bool IntegrateTravel(World world, SimConfig cfg, Actor self, double dt)
    {
        double tr = dt;

        for (int sub = 0; sub < MaxSubSteps && tr > Eps; sub++)
        {
            double bestT = double.PositiveInfinity;
            ToiKind bestKind = ToiKind.Wall;
            Actor? bestTarget = null;
            double bestNx = 0;
            double bestNy = 0;

            double wallT = SweepWalls(self, world.BoundsW, world.BoundsH, tr, out double nx, out double ny);
            if (wallT < bestT)
            {
                bestT = wallT;
                bestKind = ToiKind.Wall;
                bestNx = nx;
                bestNy = ny;
            }

            foreach (Actor other in world.Actors)
            {
                if (other.Team == self.Team || !other.IsAlive || other.IsBenched)
                {
                    continue;
                }
                double t = SweepActor(self, other, tr);
                if (t < bestT)
                {
                    bestT = t;
                    bestKind = ToiKind.Actor;
                    bestTarget = other;
                }
            }

            if (double.IsPositiveInfinity(bestT))
            {
                self.Pos.X += self.Vel.X * tr;
                self.Pos.Y += self.Vel.Y * tr;
                break;
            }

            // Advance to the impact.
            self.Pos.X += self.Vel.X * bestT;
            self.Pos.Y += self.Vel.Y * bestT;
            tr -= bestT;

            if (bestKind == ToiKind.Wall)
            {
                var normal = new Vec2(bestNx, bestNy);
                self.Vel = self.Vel.Reflected(normal, cfg.WallRestitution);
                // Push out of the wall plane so we don't re-collide at t=0 forever.
                self.Pos.X = SimMath.Clamp(self.Pos.X, self.Radius + Eps, world.BoundsW - self.Radius - Eps);
                self.Pos.Y = SimMath.Clamp(self.Pos.Y, self.Radius + Eps, world.BoundsH - self.Radius - Eps);
                world.Events.Add(new SimEvent
                {
                    Kind = SimEventKind.WallBounce,
                    ActorId = self.Id,
                    Pos = self.Pos,
                    Dir = normal,
                    Amount = self.Vel.Length(),
                });
            }
            else if (bestTarget is not null)
            {
                Actor target = bestTarget;
                // Contact normal: from target center toward self; contact point on the segment.
                double dx = self.Pos.X - target.Pos.X;
                double dy = self.Pos.Y - target.Pos.Y;
                double len = Math.Sqrt(dx * dx + dy * dy);
                if (len == 0)
                {
                    len = 1;
                }
                var normal = new Vec2(dx / len, dy / len);
                var contact = new Vec2(
                    target.Pos.X + normal.X * target.Radius,
                    target.Pos.Y + normal.Y * target.Radius);

                ContactResult result = Weapons.Behavior(self.Weapon.Type)
                    .OnEnemyContact(world, cfg, self, target, new ContactInfo { Pos = contact, Normal = normal });

                if (result.Stop)
                {
                    self.Vel = Vec2.Zero;
                    self.TravelTicksRemaining = 0;
                    return true;
                }
                if (result.Deflect)
                {
                    self.Vel = self.Vel.Reflected(normal, 1.0);
                    // Push out along the normal so the pair separates.
                    self.Pos.X += normal.X * Eps;
                    self.Pos.Y += normal.Y * Eps;
                }
                else
                {
                    // Pass-through (lance): nudge past the contact so the sweep's
                    // overlap exclusion takes over and we don't re-detect the same surface.
                    self.Pos.X += self.Vel.X * Eps;
                    self.Pos.Y += self.Vel.Y * Eps;
                    tr = Math.Max(0, tr - Eps);
                }
            }
        }

        // Safety clamp: whatever happened above, never leave the arena.
        self.Pos.X = SimMath.Clamp(self.Pos.X, self.Radius, world.BoundsW - self.Radius);
        self.Pos.Y = SimMath.Clamp(self.Pos.Y, self.Radius, world.BoundsH - self.Radius);
        return false;
    }

    /// <summary>Drop pierce marks for targets the lance has fully separated from,
    /// so a later pass over the same enemy can damage it again.</summary>
    public static void MaintainPierceMarks(World world, Actor self)
    {
        if (self.PiercedIds.Count == 0)
        {
            return;
        }
        for (int i = self.PiercedIds.Count - 1; i >= 0; i--)
        {
            string id = self.PiercedIds[i];
            Actor? target = null;
            foreach (Actor a in world.Actors)
            {
                if (a.Id == id)
                {
                    target = a;
                    break;
                }
            }
            if (target is null)
            {
                self.PiercedIds.RemoveAt(i);
                continue;
            }
            double dx = self.Pos.X - target.Pos.X;
            double dy = self.Pos.Y - target.Pos.Y;
            double r = self.Radius + target.Radius + Eps * 10;
            if (dx * dx + dy * dy > r * r)
            {
                self.PiercedIds.RemoveAt(i);
            }
        }
    }
}

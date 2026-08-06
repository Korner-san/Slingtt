namespace Slingtt.Sim;

public enum ProjectileKind
{
    Bullet,
    Grenade,
    Boomerang,
}

/// <summary>Live-iteration rework — a real, tick-simulated weapon-ultimate
/// projectile in flight. Lives on World.Projectiles from the tick it's
/// spawned (Projectiles.Spawn) until it hits or expires (Projectiles.Advance,
/// which then removes it). Deliberately a mutable class, not a struct: it's
/// mutated in place every tick it's active, the same posture Actor already
/// takes for the same reason.</summary>
public sealed class UltimateProjectile
{
    public string Id = "";
    public string OwnerId = "";
    public Team Team;
    public ProjectileKind Kind;
    public Vec2 Pos;
    public Vec2 Vel;
    public double DmgMult;
    public int StunTurns;

    /// <summary>Counts down to 0 before the projectile starts moving or can
    /// hit anything — real (not cosmetic) staggering: a small stagger between
    /// bullets in one Striker volley, and Legendary DualActivation's second
    /// wave firing visibly after the first.</summary>
    public double DelaySeconds;

    /// <summary>Accumulated distance since becoming active (DelaySeconds hit
    /// 0). Bullet's expiry range and Boomerang's arrival-at-FanRange both key
    /// off this instead of re-deriving distance from position each tick.</summary>
    public double DistanceTravelled;

    public bool Resolved; // marks for removal at the end of this tick's Advance pass

    // Bullet
    public double HitRadius = 0.3;

    // Grenade
    public Vec2 TargetPos;
    public double AoeRadius;

    // Boomerang — resolves its one fan hit from the ORIGINAL cast origin/
    // direction, not from wherever the projectile visually ends up, so the
    // fan always reads as "the area the boomerang swept," not "a hit exactly
    // at its current tip."
    public Vec2 Origin;
    public Vec2 Dir;
    public double FanRange;
    public double CosFanHalfAngle;
}

/// <summary>Replaces the old instant Ultimates.FireShapeQuery: weapon
/// ultimates now spawn real projectiles (Spawn) that travel and resolve over
/// real sim ticks (Advance), called every tick of Phase.UltimateTravel from
/// BattleSim.Step. Hits arrive at whatever tick each projectile actually
/// connects — no artificial staggering system needed on top, unlike the old
/// instant-resolution-plus-cosmetic-replay design.</summary>
public static class Projectiles
{
    private const double DualActivationDelaySeconds = 0.4;
    private const double BulletBurstStaggerSeconds = 0.08;

    /// <summary>scaleMultiplier composes with the spec's already-resolved
    /// rarity scale (BattleSetup) exactly like the old FireWeaponUltimate did
    /// — 1.0 for a normal firing, 1.25 for Prompt 5's contact combo. Only
    /// Grenade's AoeRadius and Boomerang's FanRange scale further by it;
    /// Striker's bullet/direction counts are fixed by rarity/tier already.</summary>
    public static void Spawn(World world, SimConfig cfg, Actor self, double scaleMultiplier = 1.0)
    {
        if (self.Weapon.Ultimate is not { } spec)
        {
            return;
        }

        // A zero LastTravelDir (should never happen — Actor defaults it to
        // (0,1) and launch always normalizes it) divides by 1 rather than 0.
        double dirLen = self.LastTravelDir.Length();
        Vec2 landingDir = self.LastTravelDir * (1.0 / (dirLen == 0 ? 1 : dirLen));

        // Live-iteration request: ultimate projectiles always aim at an
        // enemy, never just "whichever way the shot happened to be
        // travelling" — landingDir now only ever matters as the fallback for
        // the edge case where somehow no living enemy exists at cast time.
        // Which enemy varies by kind, matching its own identity: Striker
        // (precise, single-target, can't multi-hit) goes for whoever's
        // closest; Boomerang (a wide fan) goes for the living enemies'
        // centroid, maximizing how many its arc can catch; Grenade (see
        // SpawnGrenade) already goes for the furthest living enemy.
        Vec2 baseDir = spec.Kind switch
        {
            WeaponUltKind.Striker => DirectionToNearestOpponent(world, self, landingDir),
            WeaponUltKind.Boomerang => DirectionToOpponentCentroid(world, self, landingDir),
            _ => landingDir,
        };

        // Carry (Light archetype): the wielder's own last-recorded travel
        // distance adds further scale, same as before.
        double carryBonus = self.Armor?.Archetype == ArmorArchetype.Light
            ? Math.Min(cfg.CarryMaxBonus, self.DistanceTravelledThisTravel * cfg.CarryScalePerDistanceUnit)
            : 0;
        double totalScale = scaleMultiplier * (1 + carryBonus);
        double aoeRadius = spec.AoeRadius * totalScale;
        double fanRange = spec.FanRange * totalScale;

        // The emitted event carries the actually-scaled spec (matching what
        // really fired, including Carry/contact-combo's scaleMultiplier), not
        // the pre-scale original — the same posture the old shape-query
        // system took when it embedded its own already-scaled Shape.
        world.Events.Add(new SimEvent
        {
            Kind = SimEventKind.WeaponUltimate,
            ActorId = self.Id,
            WeaponUlt = spec with { AoeRadius = aoeRadius, FanRange = fanRange },
            Pos = self.Pos,
            Dir = baseDir,
        });

        SpawnWave(world, cfg, self, spec, baseDir, aoeRadius, fanRange, waveIndex: 0, delaySeconds: 0);
        if (spec.DualActivation)
        {
            SpawnWave(world, cfg, self, spec, baseDir, aoeRadius, fanRange, waveIndex: 1, delaySeconds: DualActivationDelaySeconds);
        }
    }

    private static void SpawnWave(
        World world, SimConfig cfg, Actor self, WeaponUltimateSpec spec, Vec2 baseDir,
        double aoeRadius, double fanRange, int waveIndex, double delaySeconds)
    {
        switch (spec.Kind)
        {
            case WeaponUltKind.Striker:
                SpawnBullets(world, cfg, self, spec, baseDir, waveIndex, delaySeconds);
                break;
            case WeaponUltKind.Grenade:
                SpawnGrenade(world, cfg, self, spec, aoeRadius, waveIndex, delaySeconds);
                break;
            case WeaponUltKind.Boomerang:
                SpawnBoomerang(world, cfg, self, spec, baseDir, fanRange, waveIndex, delaySeconds);
                break;
        }
    }

    /// <summary>Rotate v by angleRadians. Angle 0 returns v unchanged (not a
    /// lossy Atan2-then-reconstruct roundtrip) — what makes direction 0 land
    /// exactly on the landing direction, matching "the direction the
    /// character landed in" precisely.</summary>
    private static Vec2 Rotate(Vec2 v, double angleRadians)
    {
        double c = Math.Cos(angleRadians);
        double s = Math.Sin(angleRadians);
        return new Vec2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    private static void SpawnBullets(
        World world, SimConfig cfg, Actor self, WeaponUltimateSpec spec, Vec2 baseDir, int waveIndex, double waveDelaySeconds)
    {
        int directions = Math.Max(spec.DirectionCount, 1);
        int perDirection = Math.Max(spec.BulletCount, 1);
        double step = 2 * Math.PI / directions;

        for (int d = 0; d < directions; d++)
        {
            Vec2 dir = Rotate(baseDir, d * step);
            for (int b = 0; b < perDirection; b++)
            {
                world.Projectiles.Add(new UltimateProjectile
                {
                    Id = $"{self.Id}:ult:{world.Tick}:{waveIndex}:{d}:{b}",
                    OwnerId = self.Id,
                    Team = self.Team,
                    Kind = ProjectileKind.Bullet,
                    Pos = self.Pos,
                    Vel = dir * cfg.UltimateProjectileSpeed,
                    DmgMult = spec.DmgMult,
                    DelaySeconds = waveDelaySeconds + b * BulletBurstStaggerSeconds,
                });
            }
        }
    }

    private static void SpawnGrenade(
        World world, SimConfig cfg, Actor self, WeaponUltimateSpec spec, double aoeRadius, int waveIndex, double delaySeconds)
    {
        Actor? target = FindFurthestOpponent(world, self);
        if (target is null)
        {
            return;
        }
        Vec2 toTarget = target.Pos - self.Pos;
        double dist = toTarget.Length();
        Vec2 dir = dist > 0 ? toTarget * (1.0 / dist) : new Vec2(0, 1);

        world.Projectiles.Add(new UltimateProjectile
        {
            Id = $"{self.Id}:ult:{world.Tick}:{waveIndex}",
            OwnerId = self.Id,
            Team = self.Team,
            Kind = ProjectileKind.Grenade,
            Pos = self.Pos,
            Vel = dir * cfg.UltimateProjectileSpeed,
            DmgMult = spec.DmgMult,
            StunTurns = spec.StunTurns,
            TargetPos = target.Pos,
            AoeRadius = aoeRadius,
            DelaySeconds = delaySeconds,
        });
    }

    private static void SpawnBoomerang(
        World world, SimConfig cfg, Actor self, WeaponUltimateSpec spec, Vec2 baseDir, double fanRange, int waveIndex, double delaySeconds)
    {
        world.Projectiles.Add(new UltimateProjectile
        {
            Id = $"{self.Id}:ult:{world.Tick}:{waveIndex}",
            OwnerId = self.Id,
            Team = self.Team,
            Kind = ProjectileKind.Boomerang,
            Pos = self.Pos,
            Vel = baseDir * cfg.UltimateProjectileSpeed,
            DmgMult = spec.DmgMult,
            StunTurns = spec.StunTurns,
            Origin = self.Pos,
            Dir = baseDir,
            FanRange = fanRange,
            CosFanHalfAngle = Math.Cos(spec.FanHalfAngleRadians),
            DelaySeconds = delaySeconds,
        });
    }

    private static Actor? FindFurthestOpponent(World world, Actor self)
    {
        Team opposing = self.Team == Team.Hero ? Team.Enemy : Team.Hero;
        Actor? furthest = null;
        double best = -1;
        foreach (Actor a in world.Actors)
        {
            if (a.Team != opposing || !a.IsAlive || a.IsBenched)
            {
                continue;
            }
            double d = self.Pos.DistanceTo(a.Pos);
            if (d > best)
            {
                best = d;
                furthest = a;
            }
        }
        return furthest;
    }

    private static Vec2 DirectionToNearestOpponent(World world, Actor self, Vec2 fallback)
    {
        Team opposing = self.Team == Team.Hero ? Team.Enemy : Team.Hero;
        Actor? nearest = null;
        double best = double.PositiveInfinity;
        foreach (Actor a in world.Actors)
        {
            if (a.Team != opposing || !a.IsAlive || a.IsBenched)
            {
                continue;
            }
            double d = self.Pos.DistanceTo(a.Pos);
            if (d < best)
            {
                best = d;
                nearest = a;
            }
        }
        return nearest is null ? fallback : DirectionTo(self.Pos, nearest.Pos, fallback);
    }

    /// <summary>The average position of every living opponent — not a real
    /// target, just the direction that gives a fixed-width fan the best
    /// chance of sweeping through more than one of them.</summary>
    private static Vec2 DirectionToOpponentCentroid(World world, Actor self, Vec2 fallback)
    {
        Team opposing = self.Team == Team.Hero ? Team.Enemy : Team.Hero;
        Vec2 sum = Vec2.Zero;
        int count = 0;
        foreach (Actor a in world.Actors)
        {
            if (a.Team != opposing || !a.IsAlive || a.IsBenched)
            {
                continue;
            }
            sum += a.Pos;
            count++;
        }
        return count == 0 ? fallback : DirectionTo(self.Pos, sum * (1.0 / count), fallback);
    }

    private static Vec2 DirectionTo(Vec2 from, Vec2 to, Vec2 fallback)
    {
        Vec2 delta = to - from;
        double len = delta.Length();
        return len > 0 ? delta * (1.0 / len) : fallback;
    }

    /// <summary>Called every tick of Phase.UltimateTravel. Snapshotted before
    /// iterating — resolving a hit can kill a split-capable boss, and
    /// Damage.Apply spawns its children straight into world.Actors, same
    /// mid-iteration hazard Ultimates.FireShapeQuery used to snapshot
    /// against.</summary>
    public static void Advance(World world, SimConfig cfg)
    {
        if (world.Projectiles.Count == 0)
        {
            return;
        }
        double dt = cfg.Dt;

        foreach (UltimateProjectile p in world.Projectiles.ToArray())
        {
            if (p.DelaySeconds > 0)
            {
                p.DelaySeconds -= dt;
                continue;
            }

            switch (p.Kind)
            {
                case ProjectileKind.Bullet:
                    AdvanceBullet(world, cfg, p, dt);
                    break;
                case ProjectileKind.Grenade:
                    AdvanceGrenade(world, cfg, p);
                    break;
                case ProjectileKind.Boomerang:
                    AdvanceBoomerang(world, cfg, p, dt);
                    break;
            }
        }

        world.Projectiles.RemoveAll(p => p.Resolved);
    }

    private static void AdvanceBullet(World world, SimConfig cfg, UltimateProjectile p, double dt)
    {
        p.Pos += p.Vel * dt;
        p.DistanceTravelled += cfg.UltimateProjectileSpeed * dt;

        Team opposing = p.Team == Team.Hero ? Team.Enemy : Team.Hero;
        foreach (Actor target in world.Actors)
        {
            if (target.Team != opposing || !target.IsAlive || target.IsBenched)
            {
                continue;
            }
            double dx = p.Pos.X - target.Pos.X;
            double dy = p.Pos.Y - target.Pos.Y;
            double r = p.HitRadius + target.Radius;
            if (dx * dx + dy * dy > r * r)
            {
                continue;
            }

            Damage.Apply(world, cfg, world.GetActor(p.OwnerId), target, p.DmgMult, HitKind.Ultimate, target.Pos);
            p.Resolved = true;
            return;
        }

        bool outOfBounds = p.Pos.X < 0 || p.Pos.X > world.BoundsW || p.Pos.Y < 0 || p.Pos.Y > world.BoundsH;
        if (outOfBounds || p.DistanceTravelled >= cfg.UltimateProjectileMaxRange)
        {
            p.Resolved = true; // never found a target — vanishes, no damage
        }
    }

    private static void AdvanceGrenade(World world, SimConfig cfg, UltimateProjectile p)
    {
        Vec2 prev = p.Pos;
        p.Pos += p.Vel * cfg.Dt;

        // "Arrived" once this tick's movement reaches or passes TargetPos —
        // distance-to-target starts increasing again the instant it's passed,
        // which a dt-sized step will hit before ever landing exactly on it.
        double distBefore = prev.DistanceTo(p.TargetPos);
        double distAfter = p.Pos.DistanceTo(p.TargetPos);
        if (distAfter > distBefore || distAfter < 0.05)
        {
            ResolveGrenade(world, cfg, p);
            p.Resolved = true;
        }
    }

    private static void ResolveGrenade(World world, SimConfig cfg, UltimateProjectile p)
    {
        Team opposing = p.Team == Team.Hero ? Team.Enemy : Team.Hero;
        Actor owner = world.GetActor(p.OwnerId);
        // Always explodes at the original target point even if that enemy
        // died in the meantime (e.g. an earlier-resolving bullet in the same
        // cast already killed it) — someone else may still be standing there.
        foreach (Actor target in world.Actors.ToArray())
        {
            if (target.Team != opposing || !target.IsAlive || target.IsBenched)
            {
                continue;
            }
            if (p.TargetPos.DistanceTo(target.Pos) > p.AoeRadius + target.Radius)
            {
                continue;
            }

            double? dealt = Damage.Apply(world, cfg, owner, target, p.DmgMult, HitKind.Ultimate, target.Pos);
            if (dealt is null)
            {
                continue;
            }
            if (p.StunTurns > 0 && target.IsAlive)
            {
                target.StunnedTurns = Math.Max(target.StunnedTurns, p.StunTurns);
            }
        }
    }

    private static void AdvanceBoomerang(World world, SimConfig cfg, UltimateProjectile p, double dt)
    {
        p.Pos += p.Vel * dt;
        p.DistanceTravelled += cfg.UltimateProjectileSpeed * dt;
        if (p.DistanceTravelled < p.FanRange)
        {
            return;
        }

        Team opposing = p.Team == Team.Hero ? Team.Enemy : Team.Hero;
        Actor owner = world.GetActor(p.OwnerId);
        foreach (Actor target in world.Actors.ToArray())
        {
            if (target.Team != opposing || !target.IsAlive || target.IsBenched)
            {
                continue;
            }
            Vec2 toTarget = target.Pos - p.Origin;
            double dist = toTarget.Length();
            if (dist > p.FanRange + target.Radius)
            {
                continue;
            }
            Vec2 toTargetDir = dist > 0 ? toTarget * (1.0 / dist) : p.Dir;
            if (p.Dir.Dot(toTargetDir) < p.CosFanHalfAngle)
            {
                continue;
            }

            double? dealt = Damage.Apply(world, cfg, owner, target, p.DmgMult, HitKind.Ultimate, target.Pos);
            if (dealt is null)
            {
                continue;
            }
            if (p.StunTurns > 0 && target.IsAlive)
            {
                target.StunnedTurns = Math.Max(target.StunnedTurns, p.StunTurns);
            }
        }
        p.Resolved = true;
    }
}

namespace Slingtt.Sim;

/// <summary>Prompt 7 — a toxic hazard point left along a Trail enemy's path.
/// Persists for exactly one round past the round it was dropped in, then
/// stops mattering; TurnOrder's per-turn housekeeping prunes expired ones out
/// of World.Hazards entirely. Each hazard can only ever damage a given victim
/// once (AlreadyHitIds) — a "stepped in something nasty" model rather than a
/// continuously-ticking damage zone, so lingering near one point can't be
/// farmed or abused into unbounded damage.</summary>
public sealed class Hazard
{
    public string Id { get; init; } = "";
    public Vec2 Pos { get; init; }
    public double Radius { get; init; } = 0.6;
    public int ExpiresRound { get; init; }
    public Team HostileTo { get; init; }

    /// <summary>Pre-resolved from the layer's own Atk at drop time — the sim
    /// never re-reads a since-possibly-dead source actor.</summary>
    public double Atk { get; init; }

    public HashSet<string> AlreadyHitIds { get; } = new();
}

public static class Hazards
{
    /// <summary>Called once per travel tick for a Trail-type actor. Drops a new
    /// hazard point at the current position every HazardDropIntervalTicks,
    /// starting immediately on launch (TrailDropCooldownTicks resets to 0 in
    /// BattleSim.TryLaunch). No-ops during aim-prediction (DamageEnabled=false)
    /// — World.Clone shares the Hazards list by reference for cheap cloning,
    /// so a prediction run must never add to it.</summary>
    public static void MaybeDropTrail(World world, SimConfig cfg, Actor self)
    {
        if (!world.DamageEnabled || self.Weapon.Type != WeaponType.Trail)
        {
            return;
        }

        self.TrailDropCooldownTicks -= 1;
        if (self.TrailDropCooldownTicks > 0)
        {
            return;
        }
        self.TrailDropCooldownTicks = cfg.HazardDropIntervalTicks;

        world.Hazards.Add(new Hazard
        {
            Id = $"{self.Id}:{world.Tick}",
            Pos = self.Pos,
            Radius = cfg.HazardRadius,
            ExpiresRound = world.Round + 1,
            HostileTo = self.Team == Team.Hero ? Team.Enemy : Team.Hero,
            Atk = self.Weapon.Atk,
        });
    }

    /// <summary>Called once per travel tick for whoever is currently moving,
    /// regardless of team. Same DamageEnabled guard as MaybeDropTrail, and for
    /// the same reason — AlreadyHitIds lives on the shared Hazard objects
    /// themselves, so a prediction run marking a victim "already hit" would
    /// leak into the real battle.</summary>
    public static void CheckContact(World world, SimConfig cfg, Actor self)
    {
        if (!world.DamageEnabled || self.IsBenched)
        {
            return;
        }

        foreach (Hazard h in world.Hazards)
        {
            if (h.HostileTo != self.Team || world.Round > h.ExpiresRound || h.AlreadyHitIds.Contains(self.Id))
            {
                continue;
            }
            double dx = self.Pos.X - h.Pos.X;
            double dy = self.Pos.Y - h.Pos.Y;
            double r = self.Radius + h.Radius;
            if (dx * dx + dy * dy > r * r)
            {
                continue;
            }

            h.AlreadyHitIds.Add(self.Id);

            double raw = h.Atk * cfg.HazardDamageMult;
            double mitigated = raw * (1 - self.Def / (self.Def + cfg.DefK));
            double varied = mitigated * Rng.Range(ref world.Rng, cfg.VarianceMin, cfg.VarianceMax);
            double final = Math.Max(1, SimMath.RoundJs(varied));
            self.Hp = Math.Max(0, self.Hp - final);

            world.Events.Add(new SimEvent
            {
                Kind = SimEventKind.Hit,
                ActorId = h.Id,
                TargetId = self.Id,
                Amount = final,
                Pos = self.Pos,
                HitKind = HitKind.Hazard,
            });

            if (self.Hp <= 0)
            {
                world.Events.Add(new SimEvent { Kind = SimEventKind.Death, ActorId = self.Id, Pos = self.Pos });
                Damage.SpawnSplitChildren(world, self);
            }
        }
    }

    /// <summary>TurnOrder's per-turn housekeeping: drop hazards whose round has
    /// fully passed so World.Hazards doesn't grow for the length of a battle.</summary>
    public static void PruneExpired(World world)
    {
        world.Hazards.RemoveAll(h => world.Round > h.ExpiresRound);
    }
}

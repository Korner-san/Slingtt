namespace Slingtt.Sim;

public static class Damage
{
    /// <summary>Kinds that bypass the contact cooldown: the cooldown exists to stop
    /// an actor wedged against an enemy from draining it every tick, not to
    /// suppress AOE/ultimate bursts.</summary>
    private static bool CooldownExempt(HitKind kind)
        => kind == HitKind.Aoe || kind == HitKind.Ultimate;

    /// <summary>Full damage application: cooldown gate, formula, shield absorb,
    /// death, armor ultimate trigger. Returns the damage dealt, or null if skipped
    /// by the contact cooldown — callers use null to avoid counting a non-hit
    /// toward bounce decay or the pierce budget.</summary>
    public static double? Apply(
        World world,
        SimConfig cfg,
        Actor source,
        Actor target,
        double hitMult,
        HitKind kind,
        Vec2 pos)
    {
        if (!world.DamageEnabled)
        {
            return 0; // aim-prediction clone: no damage, no rng draws
        }
        if (!target.IsAlive)
        {
            return null;
        }

        string key = source.Id + ":" + target.Id;
        if (!CooldownExempt(kind))
        {
            if (world.ContactLog.TryGetValue(key, out int last)
                && (world.Tick - last) / (double)cfg.TickRate < cfg.ContactCooldownSeconds)
            {
                return null;
            }
            world.ContactLog[key] = world.Tick;
        }

        WeaponStats w = source.Weapon;
        // Prompt 5 — the contact-combo counter buffs whatever an active hero
        // deals (benched heroes deal no damage anyway; this check is what keeps
        // a fallback-activated hero, mid-transition, from double-dipping before
        // IsBenched actually flips). Never applies to enemy damage.
        double comboMult = source.Team == Team.Hero && !source.IsBenched ? Combo.DamageMultiplier(world) : 1.0;
        double raw = w.Atk * hitMult * (1 + w.Tier * cfg.EvolutionDamagePerTier) * comboMult;
        double mitigated = raw * (1 - target.Def / (target.Def + cfg.DefK));
        double varied = mitigated * Rng.Range(ref world.Rng, cfg.VarianceMin, cfg.VarianceMax);
        double final = Math.Max(1, SimMath.RoundJs(varied));

        double absorbed = 0;
        if (target.ShieldHp > 0 && world.Round <= target.ShieldExpiresRound)
        {
            absorbed = Math.Min(target.ShieldHp, final);
            target.ShieldHp -= absorbed;
        }
        double dealt = final - absorbed;
        target.Hp = Math.Max(0, target.Hp - dealt);

        world.Events.Add(new SimEvent
        {
            Kind = SimEventKind.Hit,
            ActorId = source.Id,
            TargetId = target.Id,
            Amount = dealt,
            Pos = pos,
            HitKind = kind,
            Absorbed = absorbed,
        });

        // Ordering: damage -> death check -> armor ultimate only if survived.
        // An actor killed outright does not get its armor ultimate.
        if (target.Hp <= 0)
        {
            world.Events.Add(new SimEvent
            {
                Kind = SimEventKind.Death,
                ActorId = target.Id,
                Pos = target.Pos,
            });
            SpawnSplitChildren(world, target);
        }
        else if (!target.ArmorUltFired
                 && target.Hp / target.MaxHp < cfg.ArmorUltThreshold
                 && target.Armor?.Ultimate is not null)
        {
            FireArmorUltimate(world, target);
        }

        // Prompt 6 — Siphon (Heavy archetype): lifesteal from ultimate damage
        // only, off the net amount actually taken from the target's HP (a
        // shield-absorbed hit heals nothing), capped per turn-window so a
        // multi-wave ultimate (sweep, dual activation) can't stack unbounded
        // healing.
        if (kind == HitKind.Ultimate && source.Armor?.Archetype == ArmorArchetype.Heavy)
        {
            double capRemaining = Math.Max(0, source.MaxHp * cfg.SiphonCapPctMaxHp - source.SiphonHealedThisTurn);
            double heal = Math.Min(dealt * cfg.SiphonRatio, capRemaining);
            if (heal > 0)
            {
                source.SiphonHealedThisTurn += heal;
                source.Hp = Math.Min(source.MaxHp, source.Hp + heal);
                world.Events.Add(new SimEvent { Kind = SimEventKind.Heal, ActorId = source.Id, Amount = heal, Pos = source.Pos });
            }
        }

        return dealt;
    }

    /// <summary>Prompt 7 — "splitting on death": a split-capable boss's
    /// pre-resolved children (BattleSetup) spawn around its death position,
    /// spread out so they don't stack exactly on top of each other. Public so
    /// Hazards.CheckContact's own death path can call it too, even though
    /// nothing in the current content can actually put a split-capable actor
    /// on the receiving end of a hazard today.</summary>
    public static void SpawnSplitChildren(World world, Actor target)
    {
        if (target.SplitOnDeath is not { Count: > 0 } children)
        {
            return;
        }

        double step = 2 * Math.PI / children.Count;
        const double offset = 0.8;
        for (int i = 0; i < children.Count; i++)
        {
            double angle = i * step;
            ActorInit blueprint = children[i];
            var init = new ActorInit
            {
                Id = blueprint.Id,
                Team = blueprint.Team,
                Pos = new Vec2(
                    target.Pos.X + Math.Cos(angle) * offset,
                    target.Pos.Y + Math.Sin(angle) * offset),
                Radius = blueprint.Radius,
                Hp = blueprint.Hp,
                Def = blueprint.Def,
                Weapon = blueprint.Weapon,
                Armor = blueprint.Armor,
                MoveDurationTicks = blueprint.MoveDurationTicks,
                IsBenched = blueprint.IsBenched,
                SplitOnDeath = blueprint.SplitOnDeath,
            };
            Actor spawned = world.SpawnActor(init);
            world.Events.Add(new SimEvent
            {
                Kind = SimEventKind.EnemySplit,
                ActorId = target.Id,
                TargetId = spawned.Id,
                Pos = spawned.Pos,
            });
        }
    }

    private static void FireArmorUltimate(World world, Actor actor)
    {
        if (actor.Armor?.Ultimate is not { } spec)
        {
            return;
        }
        actor.ArmorUltFired = true;
        world.Events.Add(new SimEvent
        {
            Kind = SimEventKind.ArmorUltimate,
            ActorId = actor.Id,
            ArmorUlt = spec,
            Pos = actor.Pos,
        });

        switch (spec.Kind)
        {
            case ArmorUltKind.Bulwark:
            {
                double amount = SimMath.RoundJs(actor.MaxHp * spec.ShieldRatio);
                actor.ShieldHp += amount;
                actor.ShieldExpiresRound = world.Round + spec.Rounds;
                world.Events.Add(new SimEvent
                {
                    Kind = SimEventKind.Shield,
                    ActorId = actor.Id,
                    Amount = amount,
                    Pos = actor.Pos,
                });
                break;
            }
            case ArmorUltKind.Swift:
                actor.MoveMultNextTurn = spec.MoveMult;
                break;
            case ArmorUltKind.Vital:
            {
                double amount = SimMath.RoundJs(actor.MaxHp * spec.HealRatio);
                actor.Hp = Math.Min(actor.MaxHp, actor.Hp + amount);
                world.Events.Add(new SimEvent
                {
                    Kind = SimEventKind.Heal,
                    ActorId = actor.Id,
                    Amount = amount,
                    Pos = actor.Pos,
                });
                break;
            }
        }
    }
}

using Slingtt.Sim;

namespace Slingtt.Game;

/// <summary>One equipped hero. CurrentHp is carried from the previous floor within
/// a run; null means full.</summary>
public sealed class LoadoutSlot
{
    public string HeroId { get; set; } = "";
    public string WeaponId { get; set; } = "";
    public string ArmorId { get; set; } = "";
    public int WeaponLevel { get; set; } = 1;
    public int ArmorLevel { get; set; } = 1;
    public double? CurrentHp { get; set; }

    public LoadoutSlot Clone() => (LoadoutSlot)MemberwiseClone();
}

// Maps validated content into the sim's plain runtime shapes. This is the only
// place content defs and sim types meet — Slingtt.Sim receives data, never
// imports content.
public static class BattleSetup
{
    /// <summary>Showcase loadout: one of each weapon type, item level 10 so the
    /// tier-1 ultimates are unlocked and visible.</summary>
    public static List<LoadoutSlot> DefaultTeam() => new()
    {
        new LoadoutSlot { HeroId = "hero_bram", WeaponId = "wpn_dawnbreaker", ArmorId = "arm_squire_mail", WeaponLevel = 10, ArmorLevel = 10 },
        new LoadoutSlot { HeroId = "hero_lyra", WeaponId = "wpn_stormpike", ArmorId = "arm_traveler_garb", WeaponLevel = 10, ArmorLevel = 10 },
        new LoadoutSlot { HeroId = "hero_tove", WeaponId = "wpn_sunforge_hammer", ArmorId = "arm_ironhide_plate", WeaponLevel = 10, ArmorLevel = 10 },
    };

    public static WeaponType ParseWeaponType(string s) => s switch
    {
        "lance" => WeaponType.Lance,
        "hammer" => WeaponType.Hammer,
        "trail" => WeaponType.Trail, // Prompt 7 — enemy-only
        "chain" => WeaponType.Chain, // Prompt 7 — enemy-only
        _ => WeaponType.Sword,
    };

    public static ArmorArchetype ParseArchetype(string s) => s switch
    {
        "Heavy" => ArmorArchetype.Heavy,
        "Light" => ArmorArchetype.Light,
        _ => ArmorArchetype.Balanced,
    };

    private static WeaponUltimateSpec? WeaponUltSpec(Content content, UltimateDef def, WeaponDef weapon, int level)
    {
        int tier = Formulas.EvolutionTier(level); // gates unlock (>=10) and DmgMult/StunTurns
        if (tier < 1 || def.Tiers.Count == 0)
        {
            return null;
        }
        int i = Math.Min(Math.Min(tier, 3) - 1, def.Tiers.Count - 1);
        UltimateTierDef t = def.Tiers[i];

        WeaponUltKind? kind = def.Kind switch
        {
            "striker" => WeaponUltKind.Striker,
            "boomerang" => WeaponUltKind.Boomerang,
            "grenade" => WeaponUltKind.Grenade,
            _ => null, // armor kinds are not weapon ultimates
        };
        if (kind is null)
        {
            return null;
        }

        ItemRarityBalance rarityBalance = content.Balance.ItemRarity;
        UltimateEscalationBalance esc = content.Balance.UltimateEscalation;
        int rarityIdx = Math.Max(0, rarityBalance.Order.IndexOf(weapon.Rarity));

        // Boomerang's fan half-angle ramps linearly across the item's raw
        // level (1..30), capped at FanMaxHalfAngleDegrees — same ramp-then-
        // cap shape the old sweep angle used, just aimed at a different field.
        double levelFraction = Math.Clamp((level - 1) / 29.0, 0, 1);
        double fanHalfAngleDegrees = levelFraction * esc.FanMaxHalfAngleDegrees;

        return new WeaponUltimateSpec
        {
            Kind = kind.Value,
            DmgMult = t.DmgMult ?? 1,
            StunTurns = t.StunTurns ?? 0,
            DualActivation = weapon.Rarity == "Legendary",
            BulletCount = esc.BulletCountFor(rarityIdx),
            DirectionCount = esc.DirectionCountFor(tier),
            AoeRadius = (def.BaseRadius ?? 2) * esc.ScaleFor(rarityIdx),
            FanRange = (def.BaseRange ?? 6) * esc.ScaleFor(rarityIdx),
            FanHalfAngleRadians = fanHalfAngleDegrees * Math.PI / 180.0,
        };
    }

    private static ArmorUltimateSpec? ArmorUltSpec(UltimateDef def, int tier)
    {
        if (tier < 1 || def.Tiers.Count == 0)
        {
            return null;
        }
        int i = Math.Min(Math.Min(tier, 3) - 1, def.Tiers.Count - 1);
        UltimateTierDef t = def.Tiers[i];
        return def.Kind switch
        {
            "bulwark" => new ArmorUltimateSpec
            {
                Kind = ArmorUltKind.Bulwark,
                ShieldRatio = t.ShieldRatio ?? 0.2,
                Rounds = t.Rounds ?? 2,
            },
            "swift" => new ArmorUltimateSpec { Kind = ArmorUltKind.Swift, MoveMult = t.MoveMult ?? 1.3 },
            "vital" => new ArmorUltimateSpec { Kind = ArmorUltKind.Vital, HealRatio = t.HealRatio ?? 0.2 },
            _ => null,
        };
    }

    private static ActorInit HeroActor(Content content, LoadoutSlot slot, int index, bool benched)
    {
        if (!content.Heroes.TryGetValue(slot.HeroId, out HeroDef? hero)
            || !content.Weapons.TryGetValue(slot.WeaponId, out WeaponDef? weapon)
            || !content.Armor.TryGetValue(slot.ArmorId, out ArmorDef? armor))
        {
            throw new InvalidOperationException(
                $"battleSetup: unknown id in loadout slot {index} ({slot.HeroId}/{slot.WeaponId}/{slot.ArmorId})");
        }

        double levelCoeff = content.Balance.Progression.LevelCoeff;
        int wTier = Formulas.EvolutionTier(slot.WeaponLevel);
        int aTier = Formulas.EvolutionTier(slot.ArmorLevel);
        PassiveDef passive = hero.Passive;

        // Prompt 2 — rarity multiplies the base stat; level scaling (Formulas.
        // StatAtLevel) then applies on top of that, not the other way around,
        // so a Legendary item at level 1 already reads as strong before any
        // leveling investment.
        ItemRarityBalance rarity = content.Balance.ItemRarity;
        double weaponBase = weapon.Atk * rarity.MultiplierFor(weapon.Rarity);
        double armorHpBase = armor.Hp * rarity.MultiplierFor(armor.Rarity);
        double armorDefBase = armor.Def * rarity.MultiplierFor(armor.Rarity);

        double atk = Formulas.StatAtLevel(weaponBase, slot.WeaponLevel, levelCoeff);
        double maxHp = hero.BaseHP + Formulas.StatAtLevel(armorHpBase, slot.ArmorLevel, levelCoeff);
        double def = hero.BaseDEF + Formulas.StatAtLevel(armorDefBase, slot.ArmorLevel, levelCoeff);
        if (passive.Kind == "atkPct")
        {
            atk = SimMath.RoundJs(atk * (1 + passive.Value));
        }
        if (passive.Kind == "hpPct")
        {
            maxHp = SimMath.RoundJs(maxHp * (1 + passive.Value));
        }
        if (passive.Kind == "defPct")
        {
            def = SimMath.RoundJs(def * (1 + passive.Value));
        }

        content.Ultimates.TryGetValue(weapon.UltimateId, out UltimateDef? weaponUltDef);
        content.Ultimates.TryGetValue(armor.UltimateId, out UltimateDef? armorUltDef);
        int tickRate = content.Balance.Sim.TickRate;

        // Prompt 6 — Heavy/Balanced/Light as a rarity-driven stat budget split
        // between HP and moveDuration. HP's rarity scaling is the existing line
        // above (armorHpBase); this is moveDuration's own, archetype-directional
        // version of the same idea — Heavy gets shorter at higher rarity (more
        // extreme tank), Light gets longer (more extreme mobility, more room
        // for Carry to scale off), Balanced is untouched by rarity either way.
        ArmorArchetype archetype = ParseArchetype(armor.Archetype);
        int archetypeSkew = archetype switch { ArmorArchetype.Heavy => -1, ArmorArchetype.Light => 1, _ => 0 };
        int armorRarityIdx = Math.Max(0, rarity.Order.IndexOf(armor.Rarity));
        double moveDurationRarityAdjust = 1 + archetypeSkew * armorRarityIdx * content.Balance.Archetype.MoveDurationRarityStepPct;
        int moveTicks = SimMath.RoundJsInt(armor.MoveDuration * moveDurationRarityAdjust * tickRate);

        var weaponStats = new WeaponStats
        {
            Type = ParseWeaponType(weapon.Type),
            Atk = atk,
            Tier = wTier,
            BounceDecay = weapon.BounceDecay ?? 0.1,
            PierceCount = weapon.PierceCount ?? 1,
            AoeRadius = weapon.AoeRadius ?? 1,
            Ultimate = weaponUltDef is null ? null : WeaponUltSpec(content, weaponUltDef, weapon, slot.WeaponLevel),
        };

        ArenaBalance arena = content.Balance.Arena;
        double[] xs = { 0.25, 0.5, 0.75 };
        double fx = index < xs.Length ? xs[index] : 0.5;

        // Prompt 4 — a benched hero is untargetable regardless of position
        // (Collision/Ultimates/EnemyAi all skip IsBenched actors), so this is
        // cosmetic only: the unused third lane, pulled back against the wall so
        // it reads as "off the front line" instead of overlapping either active
        // hero's tile.
        Vec2 pos = benched
            ? new Vec2(arena.W * 0.75, arena.H - 0.5)
            : new Vec2(arena.W * fx, arena.H - (index == 1 ? 1.6 : 2.2));

        return new ActorInit
        {
            Id = hero.Id,
            Team = Team.Hero,
            Pos = pos,
            Radius = 0.5,
            Hp = Math.Min(slot.CurrentHp ?? maxHp, maxHp),
            Def = def,
            Weapon = weaponStats,
            Armor = new ArmorStats
            {
                MoveDurationTicks = moveTicks,
                Tier = aTier,
                Ultimate = armorUltDef is null ? null : ArmorUltSpec(armorUltDef, aTier),
                Archetype = archetype,
            },
            MoveDurationTicks = moveTicks,
            IsBenched = benched,
        };
    }

    /// <summary>Enemy stats scale with floor: base * (1 + coeff * floor).</summary>
    public static WorldSetup Build(Content content, int floorNumber, List<LoadoutSlot> team, uint seed)
    {
        FloorDef floorDef = content.Floor(floorNumber)
                            ?? throw new InvalidOperationException($"battleSetup: no floor {floorNumber} in content");

        ArenaBalance arena = content.Balance.Arena;
        double scale = 1 + content.Balance.Progression.EnemyScalingCoeff * floorNumber;
        int tickRate = content.Balance.Sim.TickRate;

        // Prompt 4 — party of 2 active / 1 benched: the third roster slot starts
        // benched (a team of 2 or fewer has nobody to bench). Which slot starts
        // benched isn't yet player-choosable; that's a follow-up, not this prompt.
        var actors = new List<ActorInit>();
        for (int i = 0; i < team.Count; i++)
        {
            actors.Add(HeroActor(content, team[i], i, benched: team.Count >= 3 && i == 2));
        }

        int n = floorDef.Enemies.Count;
        for (int i = 0; i < n; i++)
        {
            string enemyId = floorDef.Enemies[i].EnemyId;
            if (!content.Enemies.TryGetValue(enemyId, out EnemyDef? def))
            {
                throw new InvalidOperationException($"battleSetup: unknown enemy {enemyId}");
            }
            var pos = new Vec2(arena.W * (i + 1) / (n + 1), i % 2 == 0 ? 2.0 : 3.0);
            actors.Add(new ActorInit
            {
                Id = $"{def.Id}#{i}",
                ContentId = def.Id,
                Team = Team.Enemy,
                Pos = pos,
                Radius = def.Radius,
                Hp = SimMath.RoundJs(def.Hp * scale),
                Def = SimMath.RoundJs(def.Def * scale),
                Weapon = new WeaponStats
                {
                    Type = ParseWeaponType(def.WeaponType),
                    Atk = SimMath.RoundJs(def.Atk * scale),
                    Tier = 0,
                    BounceDecay = def.BounceDecay ?? 0.1,
                    PierceCount = def.PierceCount ?? 1,
                    AoeRadius = def.AoeRadius ?? 1,
                    HasMarkerSpit = def.HasMarkerSpit,
                },
                MoveDurationTicks = SimMath.RoundJsInt(def.MoveDuration * tickRate),
                SplitOnDeath = BuildSplitChildren(content, def, scale, tickRate, $"{def.Id}#{i}", pos),
            });
        }

        return new WorldSetup
        {
            Seed = seed,
            BoundsW = arena.W,
            BoundsH = arena.H,
            Actors = actors,
        };
    }

    /// <summary>Prompt 7 — pre-resolves a split-capable boss's death-spawn
    /// blueprints, at the same floor-scaling multiplier as everything else on
    /// this floor (a standard enemy is already weaker than its boss variant
    /// by its own base stats, so no extra split-specific penalty is needed).
    /// Children never split further — the sim's own SpawnActor doesn't
    /// recurse into these blueprints' own SplitOnDeath, so this only needs to
    /// resolve one level deep regardless.</summary>
    private static List<ActorInit>? BuildSplitChildren(
        Content content, EnemyDef parent, double scale, int tickRate, string parentId, Vec2 pos)
    {
        if (parent.SplitOnDeath is not { Count: > 0 } childIds)
        {
            return null;
        }

        var children = new List<ActorInit>(childIds.Count);
        for (int i = 0; i < childIds.Count; i++)
        {
            if (!content.Enemies.TryGetValue(childIds[i], out EnemyDef? childDef))
            {
                throw new InvalidOperationException($"battleSetup: unknown split-on-death enemy {childIds[i]} for {parent.Id}");
            }
            children.Add(new ActorInit
            {
                Id = $"{parentId}_split{i}",
                ContentId = childDef.Id,
                Team = Team.Enemy,
                Pos = pos, // Damage.SpawnSplitChildren repositions around the actual death point
                Radius = childDef.Radius,
                Hp = SimMath.RoundJs(childDef.Hp * scale),
                Def = SimMath.RoundJs(childDef.Def * scale),
                Weapon = new WeaponStats
                {
                    Type = ParseWeaponType(childDef.WeaponType),
                    Atk = SimMath.RoundJs(childDef.Atk * scale),
                    Tier = 0,
                    BounceDecay = childDef.BounceDecay ?? 0.1,
                    PierceCount = childDef.PierceCount ?? 1,
                    AoeRadius = childDef.AoeRadius ?? 1,
                    HasMarkerSpit = childDef.HasMarkerSpit,
                },
                MoveDurationTicks = SimMath.RoundJsInt(childDef.MoveDuration * tickRate),
            });
        }
        return children;
    }
}

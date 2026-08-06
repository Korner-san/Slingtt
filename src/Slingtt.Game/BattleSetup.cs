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
        _ => WeaponType.Sword,
    };

    private static WeaponUltimateSpec? WeaponUltSpec(UltimateDef def, int tier)
    {
        if (tier < 1 || def.Tiers.Count == 0)
        {
            return null;
        }
        int i = Math.Min(Math.Min(tier, 3) - 1, def.Tiers.Count - 1);
        UltimateTierDef t = def.Tiers[i];
        return def.Kind switch
        {
            "cross" => new WeaponUltimateSpec
            {
                Kind = WeaponUltKind.Cross,
                Width = t.Width ?? 1,
                DmgMult = t.DmgMult ?? 1,
                DoubleCross = t.DoubleCross ?? false,
            },
            "beam" => new WeaponUltimateSpec
            {
                Kind = WeaponUltKind.Beam,
                Width = t.Width ?? 1,
                DmgMult = t.DmgMult ?? 1,
                SecondaryBeam = t.SecondaryBeam ?? false,
            },
            "aftershock" => new WeaponUltimateSpec
            {
                Kind = WeaponUltKind.Aftershock,
                Radius = t.Radius ?? 2,
                DmgMult = t.DmgMult ?? 1,
                StunTurns = t.StunTurns ?? 0,
            },
            _ => null, // armor kinds are not weapon ultimates
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

    private static ActorInit HeroActor(Content content, LoadoutSlot slot, int index)
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

        double atk = Formulas.StatAtLevel(weapon.Atk, slot.WeaponLevel, levelCoeff);
        double maxHp = hero.BaseHP + Formulas.StatAtLevel(armor.Hp, slot.ArmorLevel, levelCoeff);
        double def = hero.BaseDEF + Formulas.StatAtLevel(armor.Def, slot.ArmorLevel, levelCoeff);
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
        int moveTicks = SimMath.RoundJsInt(armor.MoveDuration * tickRate);

        var weaponStats = new WeaponStats
        {
            Type = ParseWeaponType(weapon.Type),
            Atk = atk,
            Tier = wTier,
            BounceDecay = weapon.BounceDecay ?? 0.1,
            PierceCount = weapon.PierceCount ?? 1,
            AoeRadius = weapon.AoeRadius ?? 1,
            Ultimate = weaponUltDef is null ? null : WeaponUltSpec(weaponUltDef, wTier),
        };

        ArenaBalance arena = content.Balance.Arena;
        double[] xs = { 0.25, 0.5, 0.75 };
        double fx = index < xs.Length ? xs[index] : 0.5;

        return new ActorInit
        {
            Id = hero.Id,
            Team = Team.Hero,
            Pos = new Vec2(arena.W * fx, arena.H - (index == 1 ? 1.6 : 2.2)),
            Radius = 0.5,
            Hp = Math.Min(slot.CurrentHp ?? maxHp, maxHp),
            Def = def,
            Weapon = weaponStats,
            Armor = new ArmorStats
            {
                MoveDurationTicks = moveTicks,
                Tier = aTier,
                Ultimate = armorUltDef is null ? null : ArmorUltSpec(armorUltDef, aTier),
            },
            MoveDurationTicks = moveTicks,
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

        var actors = new List<ActorInit>();
        for (int i = 0; i < team.Count; i++)
        {
            actors.Add(HeroActor(content, team[i], i));
        }

        int n = floorDef.Enemies.Count;
        for (int i = 0; i < n; i++)
        {
            string enemyId = floorDef.Enemies[i].EnemyId;
            if (!content.Enemies.TryGetValue(enemyId, out EnemyDef? def))
            {
                throw new InvalidOperationException($"battleSetup: unknown enemy {enemyId}");
            }
            actors.Add(new ActorInit
            {
                Id = $"{def.Id}#{i}",
                Team = Team.Enemy,
                Pos = new Vec2(arena.W * (i + 1) / (n + 1), i % 2 == 0 ? 2.0 : 3.0),
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
                },
                MoveDurationTicks = SimMath.RoundJsInt(def.MoveDuration * tickRate),
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
}

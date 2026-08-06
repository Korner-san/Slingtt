using System.Text.Json.Serialization;

namespace Slingtt.Game;

// Plain deserialization targets for the embedded content JSON. Field names and
// shapes match the web original's src/data/content/*.json byte for byte, so the
// same files drop straight in.

public sealed class PassiveDef
{
    public string Kind { get; set; } = "";   // atkPct | hpPct | defPct
    public double Value { get; set; }
}

public sealed class HeroDef
{
    public string Id { get; set; } = "";
    public string NameKey { get; set; } = "";
    public double BaseHP { get; set; }
    public double BaseDEF { get; set; }
    public PassiveDef Passive { get; set; } = new();
    public string ModelId { get; set; } = "";
}

public sealed class WeaponDef
{
    public string Id { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string Type { get; set; } = "sword";
    // Common | Uncommon | Rare | Epic | Legendary — see Balance.ItemRarity.
    public string Rarity { get; set; } = "Common";
    public double Atk { get; set; }
    public string UltimateId { get; set; } = "";
    public string ModelId { get; set; } = "";
    public double? BounceDecay { get; set; }
    public int? PierceCount { get; set; }
    public double? AoeRadius { get; set; }
}

public sealed class ArmorDef
{
    public string Id { get; set; } = "";
    public string NameKey { get; set; } = "";
    // Common | Uncommon | Rare | Epic | Legendary — see Balance.ItemRarity.
    public string Rarity { get; set; } = "Common";
    public double Hp { get; set; }
    public double Def { get; set; }
    public double MoveDuration { get; set; }
    public string UltimateId { get; set; } = "";
    public string ModelId { get; set; } = "";
}

public sealed class EnemyDef
{
    public string Id { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string Kind { get; set; } = "standard"; // standard | boss
    public double Hp { get; set; }
    public double Def { get; set; }
    public double Atk { get; set; }
    public double Radius { get; set; }
    public string WeaponType { get; set; } = "sword";
    public double? BounceDecay { get; set; }
    public int? PierceCount { get; set; }
    public double? AoeRadius { get; set; }
    public double MoveDuration { get; set; }
    public string ModelId { get; set; } = "";
}

public sealed class FloorEnemyEntry
{
    public string EnemyId { get; set; } = "";
}

public sealed class FloorDef
{
    public int Floor { get; set; }
    public List<FloorEnemyEntry> Enemies { get; set; } = new();
}

/// <summary>One tier row. The JSON carries only the keys relevant to the parent
/// ultimate's kind, so every field is optional here. Geometry (Shape, as of
/// Prompt 1) moved out as of Prompt 3 — shape structure and scale are now
/// derived from the wielding item's RARITY (UltimateEscalationBalance), not
/// authored per evolution tier. Tiers now carry only non-geometric effect
/// strength (DmgMult, StunTurns) and the armor-ultimate fields.</summary>
public sealed class UltimateTierDef
{
    public double? DmgMult { get; set; }
    public int? StunTurns { get; set; }
    public double? ShieldRatio { get; set; }
    public int? Rounds { get; set; }
    public double? MoveMult { get; set; }
    public double? HealRatio { get; set; }
}

public sealed class UltimateDef
{
    public string Id { get; set; } = "";
    public string NameKey { get; set; } = "";
    public string Kind { get; set; } = ""; // cross | beam | aftershock | bulwark | swift | vital
    // Base shape size before the rarity scale multiplier (Prompt 3). Only one
    // of these is meaningful per Kind: Width for cross/beam, Radius for
    // aftershock.
    public double? BaseWidth { get; set; }
    public double? BaseRadius { get; set; }
    public List<UltimateTierDef> Tiers { get; set; } = new();
}

// --- balance.json ----------------------------------------------------------

public sealed class ArenaBalance
{
    public double W { get; set; } = 9;
    public double H { get; set; } = 16;
}

public sealed class SwordBalance
{
    public double FloorMult { get; set; } = 0.4;
}

public sealed class LanceBalance
{
    public double PierceMult { get; set; } = 0.9;
}

public sealed class HammerBalance
{
    public double DirectMult { get; set; } = 0.6;
    public double AoeCenterMult { get; set; } = 1.4;
    public double AoeRimMult { get; set; } = 0.6;
}

public sealed class SimBalance
{
    public int TickRate { get; set; } = 120;
    public double MaxSpeed { get; set; } = 26;
    public double Friction { get; set; } = 0.32;
    public double MinSpeed { get; set; } = 0.6;
    public double MinDrawRatio { get; set; } = 0.25;
    public double MaxDrag { get; set; } = 3.0;
    public double HardCapSeconds { get; set; } = 5;
    public double ContactCooldownSeconds { get; set; } = 0.08;
    public double DefK { get; set; } = 300;
    public double VarianceMin { get; set; } = 0.95;
    public double VarianceMax { get; set; } = 1.05;
    public double WallRestitution { get; set; } = 1.0;
    public int TurnLimit { get; set; } = 30;
    public double EvolutionDamagePerTier { get; set; } = 0.1;
    public double ArmorUltThreshold { get; set; } = 0.5;
    public SwordBalance Sword { get; set; } = new();
    public LanceBalance Lance { get; set; } = new();
    public HammerBalance Hammer { get; set; } = new();
}

// arena_prompt-style Prompt 2 — five-rarity vocabulary replacing R/SR/SSR.
// StatMultiplier is what actually differentiates rarities now: items of the
// same weapon type/armor weight class share a similar base stat, and rarity
// does the scaling (BattleSetup applies this before Formulas.StatAtLevel's
// level scaling — the two compose multiplicatively, not vice versa).
public sealed class ItemRarityBalance
{
    public List<string> Order { get; set; } = new() { "Common", "Uncommon", "Rare", "Epic", "Legendary" };
    public Dictionary<string, double> StatMultiplier { get; set; } = new();

    public double MultiplierFor(string rarity)
        => StatMultiplier.TryGetValue(rarity, out double m) ? m : 1.0;
}

// Prompt 3 — "the main mechanical payload." Structure (element count) and
// scale (width/radius) of a weapon ultimate's shape now come from the
// wielding item's RARITY index (0=Common..4=Legendary) via the arrays below,
// not from evolution tier. Sweep (the per-level angular escalation) and dual
// activation (Legendary-exclusive) constants live here too since they're the
// same kind of "how much does this ability actually do" balance data.
public sealed class UltimateEscalationBalance
{
    public List<int> ArmCountByRarityIndex { get; set; } = new() { 2, 2, 3, 3, 4 };
    public List<int> LineCountByRarityIndex { get; set; } = new() { 1, 1, 2, 2, 3 };
    public List<double> ScalePerRarityIndex { get; set; } = new() { 1.0, 1.15, 1.3, 1.45, 1.6 };
    public double SweepMaxDegrees { get; set; } = 20;
    public double SweepDamageMult { get; set; } = 0.6;
    public double SweepMinBeatOffsetSeconds { get; set; } = 0.15;
    public double AftershockBonusRingScale { get; set; } = 1.3;

    private static int AtIndex(List<int> list, int i) => list[Math.Clamp(i, 0, list.Count - 1)];

    public int ArmCountFor(int rarityIndex) => AtIndex(ArmCountByRarityIndex, rarityIndex);
    public int LineCountFor(int rarityIndex) => AtIndex(LineCountByRarityIndex, rarityIndex);

    public double ScaleFor(int rarityIndex)
        => ScalePerRarityIndex[Math.Clamp(rarityIndex, 0, ScalePerRarityIndex.Count - 1)];
}

// Prompt 4 — party of 2 active / 1 benched. The only balance number the bench
// mechanic itself needs; everything else (turn order, swap, targeting
// exclusion) is pure mechanics with no tunable.
public sealed class BenchBalance
{
    public double RegenPctPerTurn { get; set; } = 0.05;
}

public sealed class ProgressionBalance
{
    public double LevelCoeff { get; set; } = 0.06;
    public int MaxLevel { get; set; } = 30;
    public double EnemyScalingCoeff { get; set; } = 0.11;
    public int BossEvery { get; set; } = 5;
    public int ShopEvery { get; set; } = 20;
    public int CheckpointEvery { get; set; } = 10;
}

public sealed class GachaRates
{
    public double SSR { get; set; }
    public double SR { get; set; }
    public double R { get; set; }
}

public sealed class GachaBalance
{
    public GachaRates Rates { get; set; } = new();
    public string TenPullGuarantee { get; set; } = "SR";
    public int SoftPityStart { get; set; } = 60;
    public int HardPity { get; set; } = 80;
    public int SinglePullCost { get; set; } = 100;
    public int TenPullCost { get; set; } = 900;
}

public sealed class FloorClearRewards
{
    public double SlingCoresBase { get; set; } = 18;
    public double SlingCoresPerFloor { get; set; } = 3;
    public double EvoOreBase { get; set; } = 12;
    public double EvoOrePerFloor { get; set; } = 3;
    public double SparksBase { get; set; } = 25;
    public double SparksPerFloor { get; set; } = 4;
    public double HealPctMaxHp { get; set; } = 0.15;
}

public sealed class BossRewards
{
    public int EvolutionCores { get; set; } = 1;
    public int FirstClearSlingCores { get; set; } = 120;
}

public sealed class MilestoneRewards
{
    public int EvoOreUpgradeThreshold { get; set; } = 60;
}

public sealed class RewardsBalance
{
    public FloorClearRewards FloorClear { get; set; } = new();
    public BossRewards Boss { get; set; } = new();
    public MilestoneRewards Milestones { get; set; } = new();
}

public sealed class Balance
{
    public ArenaBalance Arena { get; set; } = new();
    public SimBalance Sim { get; set; } = new();
    public ItemRarityBalance ItemRarity { get; set; } = new();
    public UltimateEscalationBalance UltimateEscalation { get; set; } = new();
    public BenchBalance Bench { get; set; } = new();
    public ProgressionBalance Progression { get; set; } = new();
    public GachaBalance Gacha { get; set; } = new();
    public RewardsBalance Rewards { get; set; } = new();
}

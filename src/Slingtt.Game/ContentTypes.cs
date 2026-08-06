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
    public string Rarity { get; set; } = "R";
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
    public string Rarity { get; set; } = "R";
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

/// <summary>Deserialization target for a ShapeDef, one level up from the sim
/// type — Prompt 1 moves geometry out of hardcoded C# into this JSON shape.
/// Angles are authored in degrees for readability and converted to radians in
/// BattleSetup. RotationOffsetDegrees and ExcludeAlreadyHit are plumbed for
/// later prompts; no current content sets them.</summary>
public sealed class ShapeDefDto
{
    public string Type { get; set; } = ""; // radial_arms | lines | rings
    public int? ArmCount { get; set; }
    public List<double>? LineAngleDegrees { get; set; }
    public double? Width { get; set; }
    public double? RotationOffsetDegrees { get; set; }
    public double? Radius { get; set; }
    public bool? ExcludeAlreadyHit { get; set; }
}

/// <summary>One tier row. The JSON carries only the keys relevant to the parent
/// ultimate's kind, so every field is optional here.</summary>
public sealed class UltimateTierDef
{
    public double? DmgMult { get; set; }
    public ShapeDefDto? Shape { get; set; } // weapon-ultimate kinds only
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
    public ProgressionBalance Progression { get; set; } = new();
    public GachaBalance Gacha { get; set; } = new();
    public RewardsBalance Rewards { get; set; } = new();
}

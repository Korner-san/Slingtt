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
    // Heavy | Balanced | Light — see Balance.Archetype. Also the trait
    // selector: Heavy=Siphon, Light=Carry (Prompt 6); Balanced has no active
    // trait of its own (its former Focus aim-preview bonus was removed).
    public string Archetype { get; set; } = "Balanced";
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

    // Prompt 7 — enemy specials. HasMarkerSpit maps straight onto
    // WeaponStats.HasMarkerSpit. SplitOnDeath is a list of enemy ids (from
    // this same file) that spawn around this one's death position when it
    // dies — empty/null for everything except split-capable bosses.
    public bool HasMarkerSpit { get; set; }
    public List<string>? SplitOnDeath { get; set; }
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
    public string Kind { get; set; } = ""; // striker | boomerang | grenade | bulwark | swift | vital
    // Base reach before the rarity scale multiplier. Only one of these is
    // meaningful per Kind: BaseRange for boomerang, BaseRadius for grenade —
    // striker needs neither (its reach is bullet/direction count, not size).
    public double? BaseRange { get; set; }
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

    /// <summary>Prompt 10 — rounds before TurnLimit that the HUD starts
    /// warning. Pure display timing: the sim itself never reads this, so it
    /// lives here and not on SimConfig. Default 30-5=25, matching "from
    /// round 25" against the default 30-round TurnLimit.</summary>
    public int RoundLimitWarningBuffer { get; set; } = 5;

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

// Prompt 3 (reworked in a later live-iteration pass) — "the main mechanical
// payload." Striker's bullet count and Grenade/Boomerang's reach scale with
// the wielding item's RARITY index (0=Common..4=Legendary); Striker's
// direction count and Boomerang's fan angle scale with evolution TIER
// (1..3, gated by item level 10/20/30) instead — two independent axes, not
// one.
public sealed class UltimateEscalationBalance
{
    /// <summary>Striker only — bullets fired per direction.</summary>
    public List<int> BulletCountByRarityIndex { get; set; } = new() { 2, 2, 3, 3, 4 };

    /// <summary>Grenade's AoeRadius and Boomerang's FanRange both scale off
    /// this the same way width/radius always have.</summary>
    public List<double> ScalePerRarityIndex { get; set; } = new() { 1.0, 1.15, 1.3, 1.45, 1.6 };

    /// <summary>Boomerang's fan half-angle cap — ramps linearly across the
    /// item's raw level (1..30), capped here, same formula the old sweep
    /// angle used.</summary>
    public double FanMaxHalfAngleDegrees { get; set; } = 20;

    /// <summary>Striker only — firing directions per evolution tier (1..3).
    /// Direction 0 is always the exact landing direction; tier 2's second
    /// direction lands exactly opposite it ("front and back").</summary>
    public List<int> DirectionCountByTier { get; set; } = new() { 1, 2, 4 };

    private static int AtIndex(List<int> list, int i) => list[Math.Clamp(i, 0, list.Count - 1)];

    public int BulletCountFor(int rarityIndex) => AtIndex(BulletCountByRarityIndex, rarityIndex);
    public int DirectionCountFor(int evolutionTier) => AtIndex(DirectionCountByTier, evolutionTier - 1);

    public double ScaleFor(int rarityIndex)
        => ScalePerRarityIndex[Math.Clamp(rarityIndex, 0, ScalePerRarityIndex.Count - 1)];
}

/// <summary>Shared travel speed/range for weapon-ultimate projectiles
/// (Striker's bullets, Grenade, Boomerang) — see SimConfig's own fields for
/// why these are shared across all three rather than per-kind.</summary>
public sealed class UltimateProjectilesBalance
{
    public double Speed { get; set; } = 3.5;
    public double MaxRange { get; set; } = 14.0;
}

// Prompt 4 — party of 2 active / 1 benched. The only balance number the bench
// mechanic itself needs; everything else (turn order, swap, targeting
// exclusion) is pure mechanics with no tunable.
public sealed class BenchBalance
{
    public double RegenPctPerTurn { get; set; } = 0.05;
}

// Prompt 6 — Heavy/Balanced/Light armor archetypes. MoveDurationRarityStepPct
// is the "rarity-driven stat budget split" itself: HP already scales with
// rarity via ItemRarity.StatMultiplier (unchanged since Prompt 2), and this
// makes moveDuration rarity-driven too, in whichever direction the archetype
// already leans — Heavy gets shorter at higher rarity (more extreme tank),
// Light gets longer (more extreme mobility, and more room for Carry to
// scale off), Balanced is untouched by rarity either way. The rest of the
// fields are Heavy/Light's own trait numbers (Siphon/Carry, read by the
// sim), plus PreviewFractionBase — the aim preview's flat length fraction,
// read by the Game-layer BattleController, same for every archetype.
public sealed class ArchetypeBalance
{
    public double MoveDurationRarityStepPct { get; set; } = 0.05;
    public double SiphonRatio { get; set; } = 0.25;
    public double SiphonCapPctMaxHp { get; set; } = 0.15;
    public double CarryScalePerDistanceUnit { get; set; } = 0.05;
    public double CarryMaxBonus { get; set; } = 0.5;
    public double PreviewFractionBase { get; set; } = 0.6;
}

// Prompt 7 — enemy specials (floors 6-15): trail hazards, and how long a
// Marker's mark blocks the Prompt 5 contact combo.
public sealed class EnemySpecialsBalance
{
    public double HazardDamageMult { get; set; } = 0.5;
    public double HazardRadius { get; set; } = 0.6;
    public int HazardDropIntervalTicks { get; set; } = 18;
    public int MarkDurationTurns { get; set; } = 2;
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

// Prompt 8 — one rarity weight table per gacha tier. Weights are read as
// literal probabilities (should sum to 1.0 per table, not enforced/normalized
// — a content bug here should show up as a visibly wrong drop rate, not get
// silently smoothed over).
public sealed class RarityRateTable
{
    public double Common { get; set; }
    public double Uncommon { get; set; }
    public double Rare { get; set; }
    public double Epic { get; set; }
    public double Legendary { get; set; }

    public double For(string rarity) => rarity switch
    {
        "Common" => Common,
        "Uncommon" => Uncommon,
        "Rare" => Rare,
        "Epic" => Epic,
        "Legendary" => Legendary,
        _ => 0,
    };
}

// Prompt 8 — the restructured gacha: two independent tabs (weapon, armor),
// three tiers per tab gated by tokens crafted from pieces every pull grants
// toward the tier above. See GachaEconomy.cs for the mechanics themselves;
// this is purely the tuning data.
public sealed class GachaBalance
{
    /// <summary>Index 0/1/2 = Tier 1/2/3. Same table for both tabs — tier
    /// (not tab) is what changes the odds; tab only changes which item pool
    /// a hit rarity draws from.</summary>
    public List<RarityRateTable> TierRates { get; set; } = new() { new(), new(), new() };

    /// <summary>Pulls since this tab's last Legendary (any tier) before the
    /// next pull is guaranteed one. Hard pity only — no soft ramp.</summary>
    public int PityPullsForLegendary { get; set; } = 80;

    public int Tier1PullCost { get; set; } = 100;
    public int Tier1TenPullCost { get; set; } = 900;

    /// <summary>3 pieces craft 1 token (of the same target tier), automatically.</summary>
    public int PiecesPerToken { get; set; } = 3;

    /// <summary>Chance a Tier 1 pull's piece grant jumps straight to a Tier 3
    /// piece instead of the normal Tier 2 piece.</summary>
    public double Tier1ToTier3TrickleChance { get; set; } = 0.03;

    /// <summary>Index 0-4 = Common..Legendary. Sacrificing an item always
    /// grants this much of its own tab's essence, regardless of which class
    /// the item's invested-essence refund (if any) is redirected to.</summary>
    public List<int> EssenceValueByRarityIndex { get; set; } = new() { 10, 25, 60, 150, 400 };

    /// <summary>Index 0-4 = Common..Legendary. EnhanceCostLevelGrowthPct
    /// compounds this per current level, so pushing a high-level item further
    /// costs more than the same rarity at level 1.</summary>
    public List<int> EnhanceBaseCostByRarityIndex { get; set; } = new() { 20, 45, 100, 220, 500 };

    public double EnhanceCostLevelGrowthPct { get; set; } = 0.08;

    public double SameClassRefundPct { get; set; } = 1.0;
    public double CrossClassRefundPct { get; set; } = 0.6;
}

// Prompt 9 — rewarded ads. Unlocked by floor-clear progress rather than a
// real-time timer, and capped per real-world day regardless of how many
// unlocks are banked.
public sealed class AdRewardBalance
{
    public int FloorClearsPerAdUnlock { get; set; } = 3;
    public int PullsPerAd { get; set; } = 4;
    public int MaxAdsPerDay { get; set; } = 6;
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
    public UltimateProjectilesBalance UltimateProjectiles { get; set; } = new();
    public BenchBalance Bench { get; set; } = new();
    public ArchetypeBalance Archetype { get; set; } = new();
    public EnemySpecialsBalance EnemySpecials { get; set; } = new();
    public ProgressionBalance Progression { get; set; } = new();
    public GachaBalance Gacha { get; set; } = new();
    public AdRewardBalance AdReward { get; set; } = new();
    public RewardsBalance Rewards { get; set; } = new();
}

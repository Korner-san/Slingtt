using Slingtt.Sim;

namespace Slingtt.Game;

public enum ResourceKind
{
    SlingCores,
    EvoOre,
    EvolutionCores,
    Sparks,
}

public sealed class RewardAmounts
{
    public int SlingCores;          // includes BossFirstClearBonus when applicable
    public int EvoOre;
    public int EvolutionCores;
    public int Sparks;
    public int BossFirstClearBonus; // portion of SlingCores that is the once-ever boss bonus
    public double HealPctMaxHp;     // healing fraction applied to surviving heroes
}

public readonly struct FloorClassification
{
    public bool IsBoss { get; init; }
    public bool IsCheckpoint { get; init; }
    public bool IsShop { get; init; }
}

public enum MilestoneKind
{
    BossDefeated,
    CheckpointReached,
    NewHighestFloor,
    EvolutionCoreAcquired,
    EnoughForPull,
    CoresUntilTenPull,
    EnoughEvoOreUpgrade,
    BossInFloors,
    CheckpointInFloors,
    ShopInFloors,
}

public readonly struct Milestone
{
    public MilestoneKind Kind { get; init; }
    public int Amount { get; init; }
}

// Pure, deterministic floor-clear reward calculation and progression helpers.
// No reward amount is hardcoded — everything comes from the balance config
// passed in, so the client and a future server compute identical results.
public static class Rewards
{
    public static readonly ResourceKind[] Order =
    {
        ResourceKind.SlingCores, ResourceKind.EvoOre, ResourceKind.EvolutionCores, ResourceKind.Sparks,
    };

    public static string Label(ResourceKind kind) => kind switch
    {
        ResourceKind.SlingCores => "Sling Cores",
        ResourceKind.EvoOre => "Evo Ore",
        ResourceKind.EvolutionCores => "Evolution Cores",
        _ => "Sparks",
    };

    /// <summary>Every N-th floor is a boss / checkpoint / shop. Checkpoints (÷10)
    /// and shops (÷20) always coincide with a boss (÷5) by construction.</summary>
    public static FloorClassification Classify(int floor, ProgressionBalance prog) => new()
    {
        IsBoss = floor > 0 && floor % prog.BossEvery == 0,
        IsCheckpoint = floor > 0 && floor % prog.CheckpointEvery == 0,
        IsShop = floor > 0 && floor % prog.ShopEvery == 0,
    };

    /// <summary>Floors from `floor` until the next multiple of `every` strictly
    /// ahead.</summary>
    public static int FloorsUntilNext(int floor, int every)
    {
        int next = (floor / every + 1) * every;
        return next - floor;
    }

    /// <summary>Reward amounts for clearing a floor. `isFirstClear` is all-time,
    /// used only to grant the once-ever boss first-clear bonus.</summary>
    public static RewardAmounts CalculateFloorRewards(int floor, bool isBoss, bool isFirstClear, RewardsBalance cfg)
    {
        FloorClearRewards fc = cfg.FloorClear;
        int bossBonus = isBoss && isFirstClear ? cfg.Boss.FirstClearSlingCores : 0;
        return new RewardAmounts
        {
            SlingCores = SimMath.RoundJsInt(fc.SlingCoresBase + fc.SlingCoresPerFloor * floor) + bossBonus,
            EvoOre = SimMath.RoundJsInt(fc.EvoOreBase + fc.EvoOrePerFloor * floor),
            Sparks = SimMath.RoundJsInt(fc.SparksBase + fc.SparksPerFloor * floor),
            EvolutionCores = isBoss ? cfg.Boss.EvolutionCores : 0,
            BossFirstClearBonus = bossBonus,
            HealPctMaxHp = fc.HealPctMaxHp,
        };
    }

    public static int AmountOf(RewardAmounts r, ResourceKind kind) => kind switch
    {
        ResourceKind.SlingCores => r.SlingCores,
        ResourceKind.EvoOre => r.EvoOre,
        ResourceKind.EvolutionCores => r.EvolutionCores,
        _ => r.Sparks,
    };

    /// <summary>Milestones supported by the player's ACTUAL state, most significant
    /// first. Only emits an entry when the underlying condition truly holds — no
    /// message is shown speculatively.</summary>
    public static List<Milestone> ComputeMilestones(
        Dictionary<ResourceKind, int> balancesAfter,
        RewardAmounts rewards,
        FloorClassification cls,
        bool isNewHighestFloor,
        int nextFloorNumber,
        bool hasNextFloor,
        Balance balance)
    {
        var outp = new List<Milestone>();
        GachaBalance gacha = balance.Gacha;
        ProgressionBalance prog = balance.Progression;
        int slingCores = balancesAfter.GetValueOrDefault(ResourceKind.SlingCores);
        int evoOre = balancesAfter.GetValueOrDefault(ResourceKind.EvoOre);

        if (cls.IsBoss)
        {
            outp.Add(new Milestone { Kind = MilestoneKind.BossDefeated });
        }
        if (isNewHighestFloor)
        {
            outp.Add(new Milestone { Kind = MilestoneKind.NewHighestFloor, Amount = nextFloorNumber - 1 });
        }
        if (cls.IsCheckpoint)
        {
            outp.Add(new Milestone { Kind = MilestoneKind.CheckpointReached });
        }
        if (rewards.EvolutionCores > 0)
        {
            outp.Add(new Milestone { Kind = MilestoneKind.EvolutionCoreAcquired, Amount = rewards.EvolutionCores });
        }

        if (slingCores >= gacha.TenPullCost)
        {
            outp.Add(new Milestone { Kind = MilestoneKind.EnoughForPull });
        }
        else if (slingCores >= gacha.SinglePullCost)
        {
            outp.Add(new Milestone { Kind = MilestoneKind.EnoughForPull });
            outp.Add(new Milestone { Kind = MilestoneKind.CoresUntilTenPull, Amount = gacha.TenPullCost - slingCores });
        }
        else
        {
            outp.Add(new Milestone { Kind = MilestoneKind.CoresUntilTenPull, Amount = gacha.TenPullCost - slingCores });
        }

        if (evoOre >= balance.Rewards.Milestones.EvoOreUpgradeThreshold)
        {
            outp.Add(new Milestone { Kind = MilestoneKind.EnoughEvoOreUpgrade });
        }

        if (hasNextFloor)
        {
            int cleared = nextFloorNumber - 1;
            int bossIn = FloorsUntilNext(cleared, prog.BossEvery);
            int cpIn = FloorsUntilNext(cleared, prog.CheckpointEvery);
            int shopIn = FloorsUntilNext(cleared, prog.ShopEvery);
            if (bossIn > 0)
            {
                outp.Add(new Milestone { Kind = MilestoneKind.BossInFloors, Amount = bossIn });
            }
            if (cpIn > 0)
            {
                outp.Add(new Milestone { Kind = MilestoneKind.CheckpointInFloors, Amount = cpIn });
            }
            if (shopIn > 0)
            {
                outp.Add(new Milestone { Kind = MilestoneKind.ShopInFloors, Amount = shopIn });
            }
        }

        return outp;
    }

    public static string Text(Milestone m) => m.Kind switch
    {
        MilestoneKind.BossDefeated => "Boss defeated",
        MilestoneKind.CheckpointReached => "Checkpoint reached",
        MilestoneKind.NewHighestFloor => $"New highest floor: {m.Amount}",
        MilestoneKind.EvolutionCoreAcquired => $"Evolution Core x{m.Amount} acquired",
        MilestoneKind.EnoughForPull => "Enough Sling Cores for a pull",
        MilestoneKind.CoresUntilTenPull => $"{m.Amount} Sling Cores until a 10-pull",
        MilestoneKind.EnoughEvoOreUpgrade => "Enough Evo Ore to upgrade",
        MilestoneKind.BossInFloors => $"Boss in {m.Amount} floor{(m.Amount == 1 ? "" : "s")}",
        MilestoneKind.CheckpointInFloors => $"Checkpoint in {m.Amount} floors",
        _ => $"Shop in {m.Amount} floors",
    };
}

using Slingtt.Game;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 8's cross-cutting integration point: RunState is the only thing that
// also touches the SlingCores balance (Tier 1's pull cost) and persistence —
// everything internal to a tab's own progress is GachaEconomyTests' concern.
// Uses the real Content.Load() singleton read-only (never mutating its
// Balance.Gacha), so a real pull here draws from the real, small-but-nonzero
// rarity tables rather than a forced one.
public class GachaRunStateTests
{
    private sealed class MemoryStore : ISaveStore
    {
        private string? _data;
        public string? Load() => _data;
        public void Save(string json) => _data = json;
    }

    [Fact]
    public void Pull_Tier1_DeductsSlingCores()
    {
        Content c = Content.Load();
        var run = new RunState(c, new MemoryStore());
        int cost = c.Balance.Gacha.Tier1PullCost;
        var hero = new PostBattleHero { HeroId = run.Team[0].HeroId, Name = "x", Hp = 500, MaxHp = 1000 };
        while (run.BalanceOf(ResourceKind.SlingCores) < cost)
        {
            run.ResolveFloorClear(new List<PostBattleHero> { hero }, turnsUsed: 3);
            run.ContinueToNextFloor();
        }
        int before = run.BalanceOf(ResourceKind.SlingCores);

        PullResult r = run.Pull(GachaTab.Weapon, GachaTier.Tier1);

        Assert.True(r.Success);
        Assert.Equal(before - cost, run.BalanceOf(ResourceKind.SlingCores));
    }

    [Fact]
    public void Pull_Tier1_FailsWithoutEnoughSlingCores_AndSpendsNothing()
    {
        Content c = Content.Load();
        var run = new RunState(c, new MemoryStore()); // fresh run: 0 Sling Cores
        Assert.Equal(0, run.BalanceOf(ResourceKind.SlingCores));

        PullResult r = run.Pull(GachaTab.Weapon, GachaTier.Tier1);

        Assert.False(r.Success);
        Assert.Equal("insufficient-sling-cores", r.FailureReason);
        Assert.Equal(0, run.BalanceOf(ResourceKind.SlingCores));
        Assert.Empty(run.GachaTabStateOf(GachaTab.Weapon).Items); // nothing pulled
    }

    [Fact]
    public void Pull_Tier2_NeverTouchesSlingCores_OnlyTheToken()
    {
        Content c = Content.Load();
        var run = new RunState(c, new MemoryStore());
        run.GachaTabStateOf(GachaTab.Weapon).Tier2Tokens = 1;
        int before = run.BalanceOf(ResourceKind.SlingCores); // 0, fresh run

        PullResult r = run.Pull(GachaTab.Weapon, GachaTier.Tier2);

        Assert.True(r.Success);
        Assert.Equal(before, run.BalanceOf(ResourceKind.SlingCores)); // untouched
        Assert.Equal(0, run.GachaTabStateOf(GachaTab.Weapon).Tier2Tokens);
    }

    [Fact]
    public void GachaProgress_SurvivesAReload()
    {
        Content c = Content.Load();
        var store = new MemoryStore();
        var run = new RunState(c, store);
        int cost = c.Balance.Gacha.Tier1PullCost;
        var hero = new PostBattleHero { HeroId = run.Team[0].HeroId, Name = "x", Hp = 500, MaxHp = 1000 };
        while (run.BalanceOf(ResourceKind.SlingCores) < cost)
        {
            run.ResolveFloorClear(new List<PostBattleHero> { hero }, turnsUsed: 3);
            run.ContinueToNextFloor();
        }
        PullResult pulled = run.Pull(GachaTab.Weapon, GachaTier.Tier1);
        Assert.True(pulled.Success); // otherwise this test would trivially pass without checking anything
        int essenceBefore = run.EssenceBalanceOf(GachaTab.Weapon);
        int itemCountBefore = run.GachaTabStateOf(GachaTab.Weapon).Items.Count;
        Assert.True(itemCountBefore > 0);

        var reloaded = new RunState(c, store);

        Assert.Equal(itemCountBefore, reloaded.GachaTabStateOf(GachaTab.Weapon).Items.Count);
        Assert.Equal(essenceBefore, reloaded.EssenceBalanceOf(GachaTab.Weapon));
    }

    [Fact]
    public void GachaProgress_CarriesForward_WhenStartingANewRunWithKeepMeta()
    {
        Content c = Content.Load();
        var run = new RunState(c, new MemoryStore());
        run.GachaTabStateOf(GachaTab.Weapon).Items.Add(new OwnedItem { InstanceId = "keep-me", TemplateId = "wpn_dawnbreaker", Rarity = "Rare" });

        run.ResetRun(keepMeta: true);

        Assert.Contains(run.GachaTabStateOf(GachaTab.Weapon).Items, i => i.InstanceId == "keep-me");
    }

    [Fact]
    public void GachaProgress_ResetsCleanly_WhenStartingANewRunWithoutKeepMeta()
    {
        Content c = Content.Load();
        var run = new RunState(c, new MemoryStore());
        run.GachaTabStateOf(GachaTab.Weapon).Items.Add(new OwnedItem { InstanceId = "drop-me", TemplateId = "wpn_dawnbreaker", Rarity = "Rare" });

        run.ResetRun(keepMeta: false);

        Assert.Empty(run.GachaTabStateOf(GachaTab.Weapon).Items);
    }

    // --- Prompt 9: rewarded ads ------------------------------------------------

    [Fact]
    public void ClearingFloors_BanksAnAdUnlock_ButOnlyOncePerFloor()
    {
        Content c = Content.Load();
        var run = new RunState(c, new MemoryStore());
        var hero = new PostBattleHero { HeroId = run.Team[0].HeroId, Name = "x", Hp = 500, MaxHp = 1000 };
        int perUnlock = c.Balance.AdReward.FloorClearsPerAdUnlock;

        for (int i = 0; i < perUnlock; i++)
        {
            run.ResolveFloorClear(new List<PostBattleHero> { hero }, turnsUsed: 3); // re-resolving floor 1 repeatedly
        }

        // Resolving the SAME already-granted floor again must not farm unlocks.
        Assert.Equal(0, run.AdRewardState.AdUnlocksAvailable);

        run.ContinueToNextFloor();
        run.ResolveFloorClear(new List<PostBattleHero> { hero }, turnsUsed: 3); // a genuinely new floor clear
        // 1 real clear from the repeated-floor-1 loop above (the other perUnlock-1
        // calls were no-ops) + 1 from this genuinely new floor = 2.
        Assert.Equal(2, run.AdRewardState.FloorClearsSinceLastUnlock);
    }

    [Fact]
    public void WatchAd_GrantsPullsPerAdFreePulls_WithoutTouchingSlingCores()
    {
        Content c = Content.Load();
        var run = new RunState(c, new MemoryStore());
        run.AdRewardState.AdUnlocksAvailable = 1;
        int slingCoresBefore = run.BalanceOf(ResourceKind.SlingCores);

        AdWatchResult r = run.WatchAd(GachaTab.Weapon, DateTime.UtcNow);

        Assert.True(r.Success);
        Assert.Equal(c.Balance.AdReward.PullsPerAd, r.Pulls.Count);
        Assert.Equal(slingCoresBefore, run.BalanceOf(ResourceKind.SlingCores)); // ad pulls are free
        Assert.Equal(0, run.AdRewardState.AdUnlocksAvailable);
    }
}

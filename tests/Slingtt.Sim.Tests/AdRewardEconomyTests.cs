using Slingtt.Game;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 9's rewarded-ad economy: unlocked by floor-clear progress rather
// than a real-time timer, capped per real-world day.
public class AdRewardEconomyTests
{
    private static AdRewardBalance TestBalance(int floorClearsPerUnlock = 3, int pullsPerAd = 4, int maxPerDay = 6) => new()
    {
        FloorClearsPerAdUnlock = floorClearsPerUnlock,
        PullsPerAd = pullsPerAd,
        MaxAdsPerDay = maxPerDay,
    };

    private static readonly DateTime Day1 = new(2026, 1, 1, 12, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Day2 = new(2026, 1, 2, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void OnFloorCleared_BanksAnUnlock_OnceEnoughFloorsClear()
    {
        AdRewardBalance bal = TestBalance(floorClearsPerUnlock: 3);
        var save = new AdRewardSave();

        AdRewardEconomy.OnFloorCleared(bal, save);
        AdRewardEconomy.OnFloorCleared(bal, save);
        Assert.Equal(0, save.AdUnlocksAvailable);
        Assert.Equal(2, save.FloorClearsSinceLastUnlock);

        AdRewardEconomy.OnFloorCleared(bal, save);
        Assert.Equal(1, save.AdUnlocksAvailable);
        Assert.Equal(0, save.FloorClearsSinceLastUnlock);
    }

    [Fact]
    public void CanWatchAd_FalseWithoutAnyBankedUnlock()
    {
        AdRewardBalance bal = TestBalance();
        var save = new AdRewardSave();

        Assert.False(AdRewardEconomy.CanWatchAd(bal, save, Day1));
    }

    [Fact]
    public void CanWatchAd_TrueWithABankedUnlock_UnderTheDailyCap()
    {
        AdRewardBalance bal = TestBalance(maxPerDay: 6);
        var save = new AdRewardSave { AdUnlocksAvailable = 1 };

        Assert.True(AdRewardEconomy.CanWatchAd(bal, save, Day1));
    }

    [Fact]
    public void CanWatchAd_FalseOnceTheDailyCapIsReached_EvenWithUnlocksBanked()
    {
        AdRewardBalance bal = TestBalance(maxPerDay: 2);
        var save = new AdRewardSave { AdUnlocksAvailable = 5, AdsWatchedToday = 2, LastResetDate = "2026-01-01" };

        Assert.False(AdRewardEconomy.CanWatchAd(bal, save, Day1));
    }

    [Fact]
    public void DailyCap_ResetsOnANewUtcDay()
    {
        AdRewardBalance bal = TestBalance(maxPerDay: 2);
        var save = new AdRewardSave { AdUnlocksAvailable = 5, AdsWatchedToday = 2, LastResetDate = "2026-01-01" };

        Assert.True(AdRewardEconomy.CanWatchAd(bal, save, Day2));
        Assert.Equal(0, save.AdsWatchedToday); // rolled over
        Assert.Equal("2026-01-02", save.LastResetDate);
    }

    [Fact]
    public void GrantReward_ConsumesOneUnlock_AndGrantsPullsPerAdPulls()
    {
        var c = new Content();
        c.Weapons["wpn_x"] = new WeaponDef { Id = "wpn_x", Rarity = "Common", Type = "sword", Atk = 100, UltimateId = "" };
        c.Balance.Gacha = new GachaBalance
        {
            TierRates = new List<RarityRateTable> { new() { Common = 1.0 }, new() { Common = 1.0 }, new() { Common = 1.0 } },
            PityPullsForLegendary = 999,
            EssenceValueByRarityIndex = new List<int> { 10, 25, 60, 150, 400 },
            EnhanceBaseCostByRarityIndex = new List<int> { 20, 45, 100, 220, 500 },
        };
        c.Balance.AdReward = TestBalance(pullsPerAd: 4);
        var adSave = new AdRewardSave { AdUnlocksAvailable = 1 };
        var gacha = new GachaSave();

        AdWatchResult r = AdRewardEconomy.GrantReward(c, adSave, gacha, GachaTab.Weapon, Day1);

        Assert.True(r.Success);
        Assert.Equal(4, r.Pulls.Count);
        Assert.All(r.Pulls, p => Assert.True(p.Success));
        Assert.Equal(0, adSave.AdUnlocksAvailable);
        Assert.Equal(1, adSave.AdsWatchedToday);
        // reused the real Pull() pipeline: the first pull's item is actually owned.
        Assert.Single(gacha.Weapon.Items);
    }

    [Fact]
    public void GrantReward_FailsWhenNotEligible_AndChangesNothing()
    {
        var c = new Content();
        c.Balance.AdReward = TestBalance();
        var adSave = new AdRewardSave(); // no banked unlocks
        var gacha = new GachaSave();

        AdWatchResult r = AdRewardEconomy.GrantReward(c, adSave, gacha, GachaTab.Weapon, Day1);

        Assert.False(r.Success);
        Assert.Equal("not-eligible", r.FailureReason);
        Assert.Empty(r.Pulls);
        Assert.Equal(0, adSave.AdsWatchedToday);
    }
}

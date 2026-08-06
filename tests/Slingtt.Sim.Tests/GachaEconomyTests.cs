using Slingtt.Game;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 8's economy, tested against isolated Content instances (never
// Content.Load()'s shared singleton — mutating its Balance.Gacha to force a
// specific rarity would leak into every other test sharing that process-wide
// instance) built with a single item per rarity, so a resolved rarity maps
// to exactly one predictable template id.
public class GachaEconomyTests
{
    private static Content BuildTestContent()
    {
        var c = new Content();
        foreach (string r in new[] { "Common", "Uncommon", "Rare", "Epic", "Legendary" })
        {
            c.Weapons[$"wpn_{r}"] = new WeaponDef { Id = $"wpn_{r}", Rarity = r, Type = "sword", Atk = 100, UltimateId = "" };
            c.Armor[$"arm_{r}"] = new ArmorDef { Id = $"arm_{r}", Rarity = r, Hp = 500, Def = 30, MoveDuration = 2.0, UltimateId = "" };
        }
        return c;
    }

    private static RarityRateTable AllRarity(string rarity) => rarity switch
    {
        "Common" => new RarityRateTable { Common = 1.0 },
        "Uncommon" => new RarityRateTable { Uncommon = 1.0 },
        "Rare" => new RarityRateTable { Rare = 1.0 },
        "Epic" => new RarityRateTable { Epic = 1.0 },
        _ => new RarityRateTable { Legendary = 1.0 },
    };

    private static GachaBalance TestBalance(RarityRateTable tier1, RarityRateTable tier2, RarityRateTable tier3, int pity = 80) => new()
    {
        TierRates = new List<RarityRateTable> { tier1, tier2, tier3 },
        PityPullsForLegendary = pity,
        Tier1PullCost = 100,
        Tier1TenPullCost = 900,
        PiecesPerToken = 3,
        Tier1ToTier3TrickleChance = 0.03,
        EssenceValueByRarityIndex = new List<int> { 10, 25, 60, 150, 400 },
        EnhanceBaseCostByRarityIndex = new List<int> { 20, 45, 100, 220, 500 },
        EnhanceCostLevelGrowthPct = 0.08,
        SameClassRefundPct = 1.0,
        CrossClassRefundPct = 0.6,
    };

    // --- Pull / tier gating ------------------------------------------------

    [Fact]
    public void Pull_Tier1_NeverGated_AlwaysSucceeds()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave();

        PullResult r = GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier1);

        Assert.True(r.Success);
        Assert.Equal("wpn_Common", r.Item!.TemplateId);
    }

    [Fact]
    public void Pull_Tier2_FailsWithoutAToken()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave();

        PullResult r = GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier2);

        Assert.False(r.Success);
        Assert.Equal("no-token", r.FailureReason);
        Assert.Null(r.Item);
    }

    [Fact]
    public void Pull_Tier2_ConsumesExactlyOneToken()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave();
        save.Weapon.Tier2Tokens = 1;

        PullResult r = GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier2);

        Assert.True(r.Success);
        Assert.Equal(0, save.Weapon.Tier2Tokens);
    }

    [Fact]
    public void WeaponAndArmorTabs_HaveFullyIndependentPity()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"), pity: 3);
        var save = new GachaSave();
        save.Weapon.PullsSinceLegendary = 2;

        GachaEconomy.Pull(c, save, GachaTab.Armor, GachaTier.Tier1);

        Assert.Equal(2, save.Weapon.PullsSinceLegendary); // untouched by the armor pull
        Assert.Equal(1, save.Armor.PullsSinceLegendary);
    }

    // --- Pieces / tokens ------------------------------------------------------

    [Fact]
    public void EveryTier1Pull_GrantsExactlyOnePiece_ConservedAcrossAnyTierUps()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common")); // default 3% trickle
        var save = new GachaSave();

        const int n = 25;
        for (int i = 0; i < n; i++)
        {
            GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier1);
        }

        int tier2Equivalent = save.Weapon.Tier2Pieces + 3 * save.Weapon.Tier2Tokens;
        int tier3Equivalent = save.Weapon.Tier3Pieces + 3 * save.Weapon.Tier3Tokens;
        Assert.Equal(n, tier2Equivalent + tier3Equivalent);
    }

    [Fact]
    public void ThreePieces_AutoCraftIntoOneTokenImmediately()
    {
        Content c = BuildTestContent();
        GachaBalance bal = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        bal.Tier1ToTier3TrickleChance = 0; // force every Tier 1 pull to grant a Tier 2 piece
        c.Balance.Gacha = bal;
        var save = new GachaSave();

        GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier1);
        GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier1);
        Assert.Equal(2, save.Weapon.Tier2Pieces);
        Assert.Equal(0, save.Weapon.Tier2Tokens);

        GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier1);
        Assert.Equal(0, save.Weapon.Tier2Pieces);
        Assert.Equal(1, save.Weapon.Tier2Tokens);
    }

    [Fact]
    public void Trickle_CanGrantATier3PieceDirectlyFromATier1Pull()
    {
        Content c = BuildTestContent();
        GachaBalance bal = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        bal.Tier1ToTier3TrickleChance = 1.0; // force it
        c.Balance.Gacha = bal;
        var save = new GachaSave();

        GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier1);

        Assert.Equal(1, save.Weapon.Tier3Pieces);
        Assert.Equal(0, save.Weapon.Tier2Pieces);
    }

    [Fact]
    public void Tier2Pull_AlwaysGrantsATier3Piece()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave();
        save.Weapon.Tier2Tokens = 1;

        GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier2);

        Assert.Equal(1, save.Weapon.Tier3Pieces);
    }

    [Fact]
    public void Tier3Pull_GrantsNoFurtherPiece()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave();
        save.Weapon.Tier3Tokens = 1;

        GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier3);

        Assert.Equal(0, save.Weapon.Tier2Pieces);
        Assert.Equal(0, save.Weapon.Tier3Pieces);
    }

    // --- Pity --------------------------------------------------------------

    [Fact]
    public void Pity_GuaranteesLegendary_AtTheThreshold()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"), pity: 5);
        var save = new GachaSave();
        save.Weapon.PullsSinceLegendary = 4;

        PullResult r = GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier1);

        Assert.Equal("Legendary", r.Item!.Rarity);
        Assert.True(r.PityTriggered);
        Assert.Equal(0, save.Weapon.PullsSinceLegendary);
    }

    [Fact]
    public void Pity_IncrementsButDoesNotTrigger_BeforeTheThreshold()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"), pity: 80);
        var save = new GachaSave();

        PullResult r = GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier1);

        Assert.False(r.PityTriggered);
        Assert.Equal(1, save.Weapon.PullsSinceLegendary);
    }

    [Fact]
    public void NaturalLegendary_ResetsPityWithoutBeingMarkedAsTriggered()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Legendary"), AllRarity("Legendary"), AllRarity("Legendary"), pity: 80);
        var save = new GachaSave();
        save.Weapon.PullsSinceLegendary = 10;

        PullResult r = GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier1);

        Assert.Equal("Legendary", r.Item!.Rarity);
        Assert.False(r.PityTriggered);
        Assert.Equal(0, save.Weapon.PullsSinceLegendary);
    }

    // --- Duplicates ----------------------------------------------------------

    [Fact]
    public void SecondPullOfTheSameTemplate_IsFlaggedAsADuplicate()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave();

        PullResult first = GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier1);
        PullResult second = GachaEconomy.Pull(c, save, GachaTab.Weapon, GachaTier.Tier1);

        Assert.False(first.WasDuplicate);
        Assert.True(second.WasDuplicate);
    }

    // --- Enhance -------------------------------------------------------------

    [Fact]
    public void EnhanceCost_ScalesByRarity()
    {
        GachaBalance bal = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));

        // baseCost=20, growth=0.08, level=1: 20*(1+0.08)=21.6 -> RoundJs -> 22.
        Assert.Equal(22, GachaEconomy.EnhanceCost(bal, "Common", level: 1));
        // baseCost=500 (Legendary): 500*1.08=540.0 -> 540.
        Assert.Equal(540, GachaEconomy.EnhanceCost(bal, "Legendary", level: 1));
    }

    [Fact]
    public void EnhanceCost_GrowsWithLevel()
    {
        GachaBalance bal = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));

        int atLevel1 = GachaEconomy.EnhanceCost(bal, "Common", level: 1);
        // level=10: 20*(1+0.8)=36.0 -> 36.
        int atLevel10 = GachaEconomy.EnhanceCost(bal, "Common", level: 10);

        Assert.Equal(36, atLevel10);
        Assert.True(atLevel10 > atLevel1);
    }

    [Fact]
    public void Enhance_SpendsEssenceAndLevelsUp_TrackingInvestedEssence()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave { WeaponEssence = 100 };
        save.Weapon.Items.Add(new OwnedItem { InstanceId = "i1", TemplateId = "wpn_Common", Rarity = "Common", Level = 1 });

        EnhanceResult r = GachaEconomy.Enhance(c, save, GachaTab.Weapon, "i1", maxLevel: 30);

        Assert.True(r.Success);
        Assert.Equal(22, r.CostPaid);
        Assert.Equal(2, r.NewLevel);
        Assert.Equal(100 - 22, save.WeaponEssence);
        Assert.Equal(2, save.Weapon.Items[0].Level);
        Assert.Equal(22, save.Weapon.Items[0].InvestedEssence);
    }

    [Fact]
    public void Enhance_FailsWithInsufficientEssence_AndChangesNothing()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave { WeaponEssence = 5 }; // cost is 22
        save.Weapon.Items.Add(new OwnedItem { InstanceId = "i1", TemplateId = "wpn_Common", Rarity = "Common", Level = 1 });

        EnhanceResult r = GachaEconomy.Enhance(c, save, GachaTab.Weapon, "i1", maxLevel: 30);

        Assert.False(r.Success);
        Assert.Equal("insufficient-essence", r.FailureReason);
        Assert.Equal(1, save.Weapon.Items[0].Level);
        Assert.Equal(5, save.WeaponEssence);
    }

    [Fact]
    public void Enhance_FailsAtMaxLevel()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave { WeaponEssence = 1_000_000 };
        save.Weapon.Items.Add(new OwnedItem { InstanceId = "i1", TemplateId = "wpn_Common", Rarity = "Common", Level = 30 });

        EnhanceResult r = GachaEconomy.Enhance(c, save, GachaTab.Weapon, "i1", maxLevel: 30);

        Assert.False(r.Success);
        Assert.Equal("max-level", r.FailureReason);
    }

    // --- Sacrifice -----------------------------------------------------------

    [Fact]
    public void Sacrifice_GrantsTheBaseRarityValue_InTheItemsOwnTab_AndRemovesIt()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave();
        save.Weapon.Items.Add(new OwnedItem { InstanceId = "i2", TemplateId = "wpn_Epic", Rarity = "Epic", Level = 1 });

        SacrificeResult r = GachaEconomy.Sacrifice(c, save, GachaTab.Weapon, "i2", GachaTab.Weapon);

        Assert.True(r.Success);
        Assert.Equal(150, r.BaseEssenceGranted); // EssenceValueByRarityIndex[Epic=3]
        Assert.Equal(0, r.InvestedEssenceRefunded); // never enhanced
        Assert.Equal(150, save.WeaponEssence);
        Assert.DoesNotContain(save.Weapon.Items, i => i.InstanceId == "i2");
    }

    [Fact]
    public void Sacrifice_RefundsInvestedEssenceAt100Percent_WithinClass()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave();
        save.Weapon.Items.Add(new OwnedItem { InstanceId = "i3", TemplateId = "wpn_Common", Rarity = "Common", Level = 3, InvestedEssence = 200 });

        SacrificeResult r = GachaEconomy.Sacrifice(c, save, GachaTab.Weapon, "i3", GachaTab.Weapon);

        Assert.Equal(200, r.InvestedEssenceRefunded);
        Assert.Equal(10 + 200, save.WeaponEssence); // base(Common=10) + full invested refund
        Assert.Equal(0, save.ArmorEssence);
    }

    [Fact]
    public void Sacrifice_RefundsInvestedEssenceAt60Percent_CrossClass()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave();
        save.Weapon.Items.Add(new OwnedItem { InstanceId = "i4", TemplateId = "wpn_Common", Rarity = "Common", Level = 3, InvestedEssence = 200 });

        SacrificeResult r = GachaEconomy.Sacrifice(c, save, GachaTab.Weapon, "i4", GachaTab.Armor);

        Assert.Equal(120, r.InvestedEssenceRefunded); // 60% of 200
        Assert.Equal(10, save.WeaponEssence); // base value only — invested refund went cross-class
        Assert.Equal(120, save.ArmorEssence);
    }

    [Fact]
    public void Sacrifice_NonexistentItem_Fails()
    {
        Content c = BuildTestContent();
        c.Balance.Gacha = TestBalance(AllRarity("Common"), AllRarity("Common"), AllRarity("Common"));
        var save = new GachaSave();

        SacrificeResult r = GachaEconomy.Sacrifice(c, save, GachaTab.Weapon, "nope", GachaTab.Weapon);

        Assert.False(r.Success);
    }

    // --- Debug readout ---------------------------------------------------------

    [Fact]
    public void ComputeTier1PullsPerLegendary_IsDeterministicForAGivenSeed()
    {
        Content c = Content.Load(); // real content, read-only use is safe to share

        var r1 = GachaDebug.ComputeTier1PullsPerLegendary(c, GachaTab.Weapon, trials: 200, seed: 42);
        var r2 = GachaDebug.ComputeTier1PullsPerLegendary(c, GachaTab.Weapon, trials: 200, seed: 42);

        Assert.Equal(r1.AverageTier1Pulls, r2.AverageTier1Pulls);
        Assert.Equal(r1.MinTier1Pulls, r2.MinTier1Pulls);
        Assert.Equal(r1.MaxTier1Pulls, r2.MaxTier1Pulls);
    }

    [Fact]
    public void ComputeTier1PullsPerLegendary_IsWithinTheHardPityBound()
    {
        Content c = Content.Load();
        GachaBalance gacha = c.Balance.Gacha;

        var report = GachaDebug.ComputeTier1PullsPerLegendary(c, GachaTab.Weapon, trials: 500, seed: 7);

        // Pity counts every pull on the tab (Tier 1, 2, or 3), so no trial can
        // exceed PityPullsForLegendary total pulls — and Tier 1 pulls are a
        // subset of that, so the average (and the max, per-trial) must be
        // bounded by it too.
        Assert.True(report.MaxTier1Pulls <= gacha.PityPullsForLegendary,
            $"max Tier 1 pulls in a trial ({report.MaxTier1Pulls}) exceeded hard pity ({gacha.PityPullsForLegendary})");
        Assert.InRange(report.AverageTier1Pulls, 1, gacha.PityPullsForLegendary);
    }
}

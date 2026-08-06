using Slingtt.Sim;

namespace Slingtt.Game;

public enum GachaTab
{
    Weapon,
    Armor,
}

public enum GachaTier
{
    Tier1 = 0,
    Tier2 = 1,
    Tier3 = 2,
}

/// <summary>One pulled/crafted item instance. TemplateId references
/// weapons.json/armor.json; Rarity is cached at pull time (items never change
/// rarity). Level/InvestedEssence mutate in place via Enhance.</summary>
public sealed class OwnedItem
{
    public string InstanceId { get; set; } = "";
    public string TemplateId { get; set; } = "";
    public string Rarity { get; set; } = "Common";
    public int Level { get; set; } = 1;

    /// <summary>Cumulative essence spent enhancing this instance — what
    /// Sacrifice refunds a percentage of.</summary>
    public int InvestedEssence { get; set; }
}

/// <summary>One tab's (weapon or armor) independent gacha progress: its own
/// pity counter, its own piece/token crafting ladder, its own inventory.</summary>
public sealed class GachaTabState
{
    public List<OwnedItem> Items { get; set; } = new();
    public int Tier2Pieces { get; set; }
    public int Tier3Pieces { get; set; }
    public int Tier2Tokens { get; set; }
    public int Tier3Tokens { get; set; }
    public int PullsSinceLegendary { get; set; }
}

/// <summary>Everything Prompt 8 persists. Embedded on RunSave so it round-trips
/// through the same save file and survives a "new run" the same way currency
/// balances do (RunState.ResetRun carries it forward when keepMeta is true).</summary>
public sealed class GachaSave
{
    public GachaTabState Weapon { get; set; } = new();
    public GachaTabState Armor { get; set; } = new();
    public int WeaponEssence { get; set; }
    public int ArmorEssence { get; set; }

    /// <summary>Persisted mulberry32 state, so pulls stay deterministic and
    /// replayable across app restarts the same way battle seeds are.</summary>
    public uint RngState { get; set; }

    /// <summary>Monotonic counter for generating unique OwnedItem.InstanceIds.</summary>
    public int NextInstanceSeq { get; set; }
}

public sealed class PullResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>What was drawn, for display — always populated on success even
    /// for a duplicate, which is shown then immediately converted rather than
    /// kept. See DuplicateEssenceGranted for what it became.</summary>
    public OwnedItem? Item { get; init; }

    public bool WasDuplicate { get; init; }

    /// <summary>Prompt 9 — "duplicates showing what they became": a duplicate
    /// never enters inventory at all, it's auto-sacrificed for its base
    /// rarity value (same tab, no invested essence to refund since it's
    /// brand new) the instant it's drawn. Zero when WasDuplicate is false.</summary>
    public int DuplicateEssenceGranted { get; init; }

    public bool PityTriggered { get; init; }
    public bool TrickleTriggered { get; init; }
}

public sealed class SacrificeResult
{
    public bool Success { get; init; }
    public int BaseEssenceGranted { get; init; }
    public int InvestedEssenceRefunded { get; init; }
    public int TotalGranted => BaseEssenceGranted + InvestedEssenceRefunded;
}

public sealed class EnhanceResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
    public int CostPaid { get; init; }
    public int NewLevel { get; init; }
}

// Prompt 8 — the restructured gacha economy. Pure over (Content, GachaSave):
// no engine types, no RunState dependency, so it's directly testable and
// directly reusable by GachaDebug's Monte Carlo derivation. RunState's thin
// wrapper methods are the only thing that also touches the SlingCores
// balance (Tier 1's pull cost) and persistence — everything internal to a
// tab's own progress (tokens, pity, pieces, inventory) lives here.
public static class GachaEconomy
{
    private static readonly string[] RarityOrder = { "Common", "Uncommon", "Rare", "Epic", "Legendary" };

    public static GachaTabState TabOf(GachaSave save, GachaTab tab) => tab == GachaTab.Weapon ? save.Weapon : save.Armor;

    /// <summary>Tier 1 is never gated (no token check). Tier 2/3 consume one
    /// token of that tier — the token IS the gate; there's no separate
    /// "unlocked" flag. Every pull's rarity roll first checks this tab's
    /// pity counter (hard pity only, no soft ramp), then on success grants a
    /// piece toward the tier above (Tier 1 pulls have a small chance to
    /// trickle a Tier 3 piece directly instead of the normal Tier 2 piece;
    /// Tier 3 pulls grant no further piece — there's no Tier 4).</summary>
    public static PullResult Pull(Content content, GachaSave save, GachaTab tab, GachaTier tier)
    {
        GachaTabState state = TabOf(save, tab);

        if (tier != GachaTier.Tier1)
        {
            int tokens = tier == GachaTier.Tier2 ? state.Tier2Tokens : state.Tier3Tokens;
            if (tokens < 1)
            {
                return new PullResult { Success = false, FailureReason = "no-token" };
            }
            if (tier == GachaTier.Tier2) { state.Tier2Tokens -= 1; } else { state.Tier3Tokens -= 1; }
        }

        GachaBalance balance = content.Balance.Gacha;
        var rng = new RngState(save.RngState);

        state.PullsSinceLegendary += 1;
        bool pity = state.PullsSinceLegendary >= balance.PityPullsForLegendary;
        string rarity = pity ? "Legendary" : RollRarity(balance.TierRates[(int)tier], ref rng);
        if (rarity == "Legendary")
        {
            state.PullsSinceLegendary = 0;
        }

        string templateId = PickRandomItem(content, tab, rarity, ref rng);
        bool wasDuplicate = state.Items.Any(i => i.TemplateId == templateId);

        save.NextInstanceSeq += 1;
        var owned = new OwnedItem
        {
            InstanceId = $"{tab}_{save.NextInstanceSeq}",
            TemplateId = templateId,
            Rarity = rarity,
            Level = 1,
        };

        // Prompt 9 — a duplicate never enters inventory: it's shown (Item is
        // still populated, for the reveal UI) but immediately converted to
        // this tab's essence at its base rarity value instead of being kept.
        // A brand-new pull has no invested essence to refund, so this is
        // just the same base value Sacrifice would grant.
        int duplicateEssence = 0;
        if (wasDuplicate)
        {
            duplicateEssence = balance.EssenceValueByRarityIndex[Math.Clamp(RarityIndexOf(rarity), 0, balance.EssenceValueByRarityIndex.Count - 1)];
            GrantEssence(save, tab, duplicateEssence);
        }
        else
        {
            state.Items.Add(owned);
        }

        bool trickle = false;
        if (tier == GachaTier.Tier1)
        {
            trickle = Rng.NextDouble(ref rng) < balance.Tier1ToTier3TrickleChance;
            GrantPiece(state, balance, trickle ? GachaTier.Tier3 : GachaTier.Tier2);
        }
        else if (tier == GachaTier.Tier2)
        {
            GrantPiece(state, balance, GachaTier.Tier3);
        }

        save.RngState = rng.S;

        return new PullResult
        {
            Success = true,
            Item = owned,
            WasDuplicate = wasDuplicate,
            DuplicateEssenceGranted = duplicateEssence,
            PityTriggered = pity,
            TrickleTriggered = trickle,
        };
    }

    /// <summary>3 pieces auto-craft into 1 token, immediately — no separate
    /// manual crafting step, so the tab's state is always "settled."</summary>
    private static void GrantPiece(GachaTabState state, GachaBalance balance, GachaTier targetTier)
    {
        if (targetTier == GachaTier.Tier2)
        {
            state.Tier2Pieces += 1;
            while (state.Tier2Pieces >= balance.PiecesPerToken)
            {
                state.Tier2Pieces -= balance.PiecesPerToken;
                state.Tier2Tokens += 1;
            }
        }
        else if (targetTier == GachaTier.Tier3)
        {
            state.Tier3Pieces += 1;
            while (state.Tier3Pieces >= balance.PiecesPerToken)
            {
                state.Tier3Pieces -= balance.PiecesPerToken;
                state.Tier3Tokens += 1;
            }
        }
    }

    private static string RollRarity(RarityRateTable table, ref RngState rng)
    {
        double roll = Rng.NextDouble(ref rng);
        double cumulative = 0;
        foreach (string rarity in RarityOrder)
        {
            cumulative += table.For(rarity);
            if (roll < cumulative)
            {
                return rarity;
            }
        }
        return RarityOrder[^1]; // floating-point rounding safety net: falls back to Legendary
    }

    private static string PickRandomItem(Content content, GachaTab tab, string rarity, ref RngState rng)
    {
        List<string> pool = tab == GachaTab.Weapon
            ? content.Weapons.Values.Where(w => w.Rarity == rarity).Select(w => w.Id).ToList()
            : content.Armor.Values.Where(a => a.Rarity == rarity).Select(a => a.Id).ToList();
        if (pool.Count == 0)
        {
            throw new InvalidOperationException($"gacha: no {tab} items of rarity {rarity} in content");
        }
        int idx = (int)(Rng.NextDouble(ref rng) * pool.Count);
        if (idx >= pool.Count)
        {
            idx = pool.Count - 1; // guards the (vanishingly unlikely) NextDouble() == 1.0 edge
        }
        return pool[idx];
    }

    /// <summary>Compounds EnhanceCostLevelGrowthPct per current level, so
    /// pushing a high-level item further costs more than the same rarity
    /// fresh at level 1.</summary>
    public static int EnhanceCost(GachaBalance balance, string rarity, int level)
    {
        int rarityIdx = RarityIndexOf(rarity);
        int baseCost = balance.EnhanceBaseCostByRarityIndex[Math.Clamp(rarityIdx, 0, balance.EnhanceBaseCostByRarityIndex.Count - 1)];
        return SimMath.RoundJsInt(baseCost * (1 + level * balance.EnhanceCostLevelGrowthPct));
    }

    public static EnhanceResult Enhance(Content content, GachaSave save, GachaTab tab, string instanceId, int maxLevel)
    {
        GachaTabState state = TabOf(save, tab);
        OwnedItem? item = state.Items.FirstOrDefault(i => i.InstanceId == instanceId);
        if (item is null)
        {
            return new EnhanceResult { Success = false, FailureReason = "not-found" };
        }
        if (item.Level >= maxLevel)
        {
            return new EnhanceResult { Success = false, FailureReason = "max-level" };
        }

        int cost = EnhanceCost(content.Balance.Gacha, item.Rarity, item.Level);
        int essence = EssenceBalanceOf(save, tab);
        if (essence < cost)
        {
            return new EnhanceResult { Success = false, FailureReason = "insufficient-essence" };
        }

        SpendEssence(save, tab, cost);
        item.Level += 1;
        item.InvestedEssence += cost;

        return new EnhanceResult { Success = true, CostPaid = cost, NewLevel = item.Level };
    }

    /// <summary>Sacrificing always grants the item's own tab's essence for
    /// its base rarity value. Its invested-essence refund (only nonzero if it
    /// was ever enhanced) is redirected to receiveInvestedAs — same tab as
    /// the item itself refunds at SameClassRefundPct (100%), the other tab
    /// at CrossClassRefundPct (60%).</summary>
    public static SacrificeResult Sacrifice(Content content, GachaSave save, GachaTab tab, string instanceId, GachaTab receiveInvestedAs)
    {
        GachaTabState state = TabOf(save, tab);
        OwnedItem? item = state.Items.FirstOrDefault(i => i.InstanceId == instanceId);
        if (item is null)
        {
            return new SacrificeResult { Success = false };
        }
        state.Items.Remove(item);

        GachaBalance balance = content.Balance.Gacha;
        int rarityIdx = RarityIndexOf(item.Rarity);
        int baseValue = balance.EssenceValueByRarityIndex[Math.Clamp(rarityIdx, 0, balance.EssenceValueByRarityIndex.Count - 1)];

        double refundPct = receiveInvestedAs == tab ? balance.SameClassRefundPct : balance.CrossClassRefundPct;
        int investedRefund = SimMath.RoundJsInt(item.InvestedEssence * refundPct);

        GrantEssence(save, tab, baseValue);
        GrantEssence(save, receiveInvestedAs, investedRefund);

        return new SacrificeResult { Success = true, BaseEssenceGranted = baseValue, InvestedEssenceRefunded = investedRefund };
    }

    public static int EssenceBalanceOf(GachaSave save, GachaTab tab) => tab == GachaTab.Weapon ? save.WeaponEssence : save.ArmorEssence;

    private static void GrantEssence(GachaSave save, GachaTab tab, int amount)
    {
        if (amount <= 0)
        {
            return;
        }
        if (tab == GachaTab.Weapon) { save.WeaponEssence += amount; } else { save.ArmorEssence += amount; }
    }

    private static void SpendEssence(GachaSave save, GachaTab tab, int amount)
    {
        if (tab == GachaTab.Weapon) { save.WeaponEssence -= amount; } else { save.ArmorEssence -= amount; }
    }

    private static int RarityIndexOf(string rarity)
    {
        int idx = Array.IndexOf(RarityOrder, rarity);
        return idx < 0 ? 0 : idx;
    }
}

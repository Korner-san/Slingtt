namespace Slingtt.Game;

/// <summary>Prompt 9 — rewarded ads. Embedded on RunSave, same pattern as
/// GachaSave.</summary>
public sealed class AdRewardSave
{
    /// <summary>Progress toward the next banked ad-watch opportunity —
    /// unlocked by floor clears, never a real-time timer.</summary>
    public int FloorClearsSinceLastUnlock { get; set; }

    /// <summary>Banked opportunities to watch an ad. Floor-clear progress
    /// keeps accumulating even past the daily cap; this just won't be
    /// spendable again until tomorrow (see AdsWatchedToday).</summary>
    public int AdUnlocksAvailable { get; set; }

    /// <summary>UTC "yyyy-MM-dd" of the last daily-cap reset.</summary>
    public string LastResetDate { get; set; } = "";

    public int AdsWatchedToday { get; set; }
}

public sealed class AdWatchResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
    public List<PullResult> Pulls { get; init; } = new();
}

// Prompt 9 — pure over (Content, AdRewardSave, GachaSave), same shape as
// GachaEconomy: no engine types, no actual ad-playback here. Showing the ad
// itself is a UI-layer concern (see IAdProvider) — this only resolves the
// reward once the caller confirms the ad actually finished.
public static class AdRewardEconomy
{
    /// <summary>Call once per floor clear (gated the same way currency
    /// rewards are, so retrying an already-cleared floor doesn't farm this).
    /// Banks a new ad-watch opportunity every FloorClearsPerAdUnlock floors.</summary>
    public static void OnFloorCleared(AdRewardBalance balance, AdRewardSave save)
    {
        save.FloorClearsSinceLastUnlock += 1;
        while (save.FloorClearsSinceLastUnlock >= balance.FloorClearsPerAdUnlock)
        {
            save.FloorClearsSinceLastUnlock -= balance.FloorClearsPerAdUnlock;
            save.AdUnlocksAvailable += 1;
        }
    }

    private static void RollDailyCapIfNeeded(AdRewardSave save, DateTime utcNow)
    {
        string today = utcNow.ToString("yyyy-MM-dd");
        if (save.LastResetDate != today)
        {
            save.LastResetDate = today;
            save.AdsWatchedToday = 0;
        }
    }

    public static bool CanWatchAd(AdRewardBalance balance, AdRewardSave save, DateTime utcNow)
    {
        RollDailyCapIfNeeded(save, utcNow);
        return save.AdUnlocksAvailable > 0 && save.AdsWatchedToday < balance.MaxAdsPerDay;
    }

    /// <summary>Only call after the ad has actually finished playing (the
    /// caller's IAdProvider completion callback) — re-validates eligibility
    /// itself, so a stale UI state can't grant a reward that isn't earned.</summary>
    public static AdWatchResult GrantReward(Content content, AdRewardSave save, GachaSave gacha, GachaTab tab, DateTime utcNow)
    {
        AdRewardBalance balance = content.Balance.AdReward;
        if (!CanWatchAd(balance, save, utcNow))
        {
            return new AdWatchResult { Success = false, FailureReason = "not-eligible" };
        }

        save.AdUnlocksAvailable -= 1;
        save.AdsWatchedToday += 1;

        var pulls = new List<PullResult>(balance.PullsPerAd);
        for (int i = 0; i < balance.PullsPerAd; i++)
        {
            pulls.Add(GachaEconomy.Pull(content, gacha, tab, GachaTier.Tier1));
        }

        return new AdWatchResult { Success = true, Pulls = pulls };
    }
}

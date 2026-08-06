using Slingtt.Sim;

namespace Slingtt.Game;

// Prompt 8's "debug readout of derived Tier-1-pulls-per-Legendary": not a
// closed-form formula — the piece -> token -> tier-up chain interacting with
// pity makes an exact expected value error-prone to derive by hand and hard
// to trust — but a Monte Carlo average over the REAL pull code path
// (GachaEconomy.Pull itself), so the number always reflects whatever the
// economy actually does, the same "run the real sim, don't approximate"
// principle Predict.Trajectory already follows for aim previews.
public static class GachaDebug
{
    public sealed class Tier1PullsPerLegendaryReport
    {
        public double AverageTier1Pulls { get; init; }
        public int Trials { get; init; }
        public int MinTier1Pulls { get; init; }
        public int MaxTier1Pulls { get; init; }
    }

    /// <summary>Simulates the natural play pattern: pull Tier 1 repeatedly,
    /// and the instant crafting produces a Tier 2 or Tier 3 token, spend it
    /// immediately (highest tier first, re-checking after each spend since a
    /// Tier 2 pull can itself mint a fresh Tier 3 token) — before the next
    /// Tier 1 pull. Counts only the Tier 1 pulls themselves (the one
    /// repeatable, SlingCores-gated action a player actually chooses to take
    /// over and over) until any pull, at any tier, lands a Legendary.
    /// Each trial gets a fresh GachaSave with unlimited SlingCores (this
    /// measures the rarity/crafting pacing, not the floor-reward income
    /// rate) and its own RNG stream derived from the master seed, so the
    /// whole report is deterministic for a given (content, trials, seed).</summary>
    public static Tier1PullsPerLegendaryReport ComputeTier1PullsPerLegendary(
        Content content, GachaTab tab, int trials, uint seed)
    {
        if (trials <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(trials), "gacha debug: trials must be positive");
        }

        var masterRng = new RngState(seed);
        long total = 0;
        int min = int.MaxValue;
        int max = 0;

        for (int t = 0; t < trials; t++)
        {
            var save = new GachaSave { RngState = Rng.NextUInt(ref masterRng) };
            int tier1Pulls = RunOneTrial(content, save, tab);

            total += tier1Pulls;
            min = Math.Min(min, tier1Pulls);
            max = Math.Max(max, tier1Pulls);
        }

        return new Tier1PullsPerLegendaryReport
        {
            AverageTier1Pulls = (double)total / trials,
            Trials = trials,
            MinTier1Pulls = min,
            MaxTier1Pulls = max,
        };
    }

    private static int RunOneTrial(Content content, GachaSave save, GachaTab tab)
    {
        GachaTabState state = GachaEconomy.TabOf(save, tab);
        int tier1Pulls = 0;

        while (true)
        {
            tier1Pulls += 1;
            PullResult tier1 = GachaEconomy.Pull(content, save, tab, GachaTier.Tier1);
            if (tier1.Item!.Rarity == "Legendary")
            {
                return tier1Pulls;
            }

            while (state.Tier3Tokens > 0 || state.Tier2Tokens > 0)
            {
                PullResult higher = state.Tier3Tokens > 0
                    ? GachaEconomy.Pull(content, save, tab, GachaTier.Tier3)
                    : GachaEconomy.Pull(content, save, tab, GachaTier.Tier2);
                if (higher.Item!.Rarity == "Legendary")
                {
                    return tier1Pulls;
                }
            }
        }
    }
}

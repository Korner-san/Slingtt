using Slingtt.Sim;

namespace Slingtt.Game;

// Formulas shared verbatim with the web original (src/shared/formulas.ts).
// Pure and dependency-free: if a server and the client ever disagree on these
// numbers, players see "server rejected my upgrade" — change them only here.
public static class Formulas
{
    /// <summary>Item primary stat at a given level. Roughly triples by Lv30.</summary>
    public static double StatAtLevel(double baseValue, int level, double levelCoeff)
        => SimMath.RoundJs(baseValue * (1 + levelCoeff * (level - 1)));

    /// <summary>Evolution tier from item level. Gates at 10/20/30.</summary>
    public static int EvolutionTier(int level)
        => level >= 30 ? 3 : level >= 20 ? 2 : level >= 10 ? 1 : 0;

    /// <summary>Damage multiplier per evolution tier. Applied inside the sim's
    /// damage formula.</summary>
    public static double EvolutionMultiplier(int tier, double perTierBonus)
        => 1 + tier * perTierBonus;
}

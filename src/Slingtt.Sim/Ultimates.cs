namespace Slingtt.Sim;

/// <summary>Weapon ultimates fire in the Ultimate phase, after travel fully
/// resolves, from the actor's final position — then the sim moves into
/// Phase.UltimateTravel until every spawned projectile (Projectiles.cs) has
/// hit or expired. This file is now just the entry point BattleSim.Step
/// calls; the actual spawn logic lives in Projectiles.Spawn.</summary>
public static class Ultimates
{
    /// <summary>scaleMultiplier composes with the shape's already-resolved
    /// rarity scale (BattleSetup) — 1.0 for a normal firing, 1.25 for Prompt
    /// 5's contact combo.</summary>
    public static void FireWeaponUltimate(World world, SimConfig cfg, Actor self, double scaleMultiplier = 1.0)
        => Projectiles.Spawn(world, cfg, self, scaleMultiplier);
}

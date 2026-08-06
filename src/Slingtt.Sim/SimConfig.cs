namespace Slingtt.Sim;

// All balance numbers the sim reads. Built by Slingtt.Game from balance.json —
// the sim never imports content (`Step(world, config)`).
public sealed class SimConfig
{
    public int TickRate { get; init; } = 120;

    public double MaxSpeed { get; init; } = 26.0;
    public double Friction { get; init; } = 0.32; // per-second retention base
    public double MinSpeed { get; init; } = 0.6;
    public double MinDrawRatio { get; init; } = 0.25;
    public double MaxDrag { get; init; } = 3.0;
    public double HardCapSeconds { get; init; } = 5.0;
    public double ContactCooldownSeconds { get; init; } = 0.08;
    public double DefK { get; init; } = 300.0;
    public double VarianceMin { get; init; } = 0.95;
    public double VarianceMax { get; init; } = 1.05;
    public double WallRestitution { get; init; } = 1.0;
    public int TurnLimit { get; init; } = 30; // rounds
    public double EvolutionDamagePerTier { get; init; } = 0.1;
    public double ArmorUltThreshold { get; init; } = 0.5; // hp ratio

    public double SwordFloorMult { get; init; } = 0.4;
    public double LancePierceMult { get; init; } = 0.9;
    public double HammerDirectMult { get; init; } = 0.6;
    public double HammerAoeCenterMult { get; init; } = 1.4;
    public double HammerAoeRimMult { get; init; } = 0.6;

    // Live-iteration rework — weapon ultimates are now real, tick-simulated
    // traveling projectiles (Projectiles.cs) instead of an instant shape
    // query. Speed is deliberately slow, shared across all three kinds
    // (Striker/Grenade/Boomerang), so the flight itself reads clearly instead
    // of looking instant. MaxRange only matters for Striker's bullets — the
    // distance at which one that never found a target simply expires;
    // Grenade always flies to a fixed target point and Boomerang always
    // resolves at its own FanRange, so neither needs this as a safety cap.
    public double UltimateProjectileSpeed { get; init; } = 3.5;
    public double UltimateProjectileMaxRange { get; init; } = 14.0;

    // Prompt 4 — fraction of max HP a benched hero regenerates every turn (any
    // actor's turn, hero or enemy — see TurnOrder.AdvanceRoundRobin).
    public double BenchRegenPctPerTurn { get; init; } = 0.05;

    // Prompt 6 — archetype trait balance. Siphon/Carry are read by the sim
    // (Damage.Apply / Ultimates.FireWeaponUltimate); PreviewFractionBase lives
    // here too even though only the Game-layer BattleController reads it,
    // since SimConfig is the one bag of balance numbers Content maps onto.
    public double SiphonRatio { get; init; } = 0.25;
    public double SiphonCapPctMaxHp { get; init; } = 0.15;
    public double CarryScalePerDistanceUnit { get; init; } = 0.05;
    public double CarryMaxBonus { get; init; } = 0.5;
    public double PreviewFractionBase { get; init; } = 0.6;

    // Prompt 7 — enemy specials. HazardDamageMult is against the trail-layer's
    // own Atk, mirroring how every other damage source here is Atk-relative.
    public double HazardDamageMult { get; init; } = 0.5;
    public double HazardRadius { get; init; } = 0.6;
    public int HazardDropIntervalTicks { get; init; } = 18; // ~0.15s at 120 ticks/sec
    public int MarkDurationTurns { get; init; } = 2;

    public double Dt => 1.0 / TickRate;

    private double _frictionPerTick = double.NaN;

    /// <summary>v *= FrictionPerTick, once per tick. Pow is only ever evaluated
    /// here, lazily on first read — never inside the hot loop.
    /// See determinism-spec §4 "The one legitimate pow".</summary>
    public double FrictionPerTick
    {
        get
        {
            if (double.IsNaN(_frictionPerTick))
            {
                _frictionPerTick = Math.Pow(Friction, 1.0 / TickRate);
            }
            return _frictionPerTick;
        }
    }
}

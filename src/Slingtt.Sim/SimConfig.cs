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

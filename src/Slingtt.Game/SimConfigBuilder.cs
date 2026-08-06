using Slingtt.Sim;

namespace Slingtt.Game;

/// <summary>The sim never imports content; this hands it the validated balance
/// numbers as an argument.</summary>
public static class SimConfigBuilder
{
    public static SimConfig Build(Balance balance)
    {
        SimBalance s = balance.Sim;
        return new SimConfig
        {
            TickRate = s.TickRate,
            MaxSpeed = s.MaxSpeed,
            Friction = s.Friction,
            MinSpeed = s.MinSpeed,
            MinDrawRatio = s.MinDrawRatio,
            MaxDrag = s.MaxDrag,
            HardCapSeconds = s.HardCapSeconds,
            ContactCooldownSeconds = s.ContactCooldownSeconds,
            DefK = s.DefK,
            VarianceMin = s.VarianceMin,
            VarianceMax = s.VarianceMax,
            WallRestitution = s.WallRestitution,
            TurnLimit = s.TurnLimit,
            EvolutionDamagePerTier = s.EvolutionDamagePerTier,
            ArmorUltThreshold = s.ArmorUltThreshold,
            SwordFloorMult = s.Sword.FloorMult,
            LancePierceMult = s.Lance.PierceMult,
            HammerDirectMult = s.Hammer.DirectMult,
            HammerAoeCenterMult = s.Hammer.AoeCenterMult,
            HammerAoeRimMult = s.Hammer.AoeRimMult,
            SweepDamageMult = balance.UltimateEscalation.SweepDamageMult,
            SweepMinBeatOffsetSeconds = balance.UltimateEscalation.SweepMinBeatOffsetSeconds,
        };
    }
}

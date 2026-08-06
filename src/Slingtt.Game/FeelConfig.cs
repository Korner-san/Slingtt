namespace Slingtt.Game;

// Game-feel tunables. These are PRESENTATION values, not simulation values: they
// change how impacts feel, never what the sim computes, so they live here rather
// than in balance.json and never touch determinism. Hit-stop pauses tick
// CONSUMPTION in the controller; it never changes dt.
public sealed class FeelConfig
{
    /// <summary>Clamp on real delta per frame, so a suspended app that resumes
    /// doesn't fast-forward the whole battle (spiral-of-death guard).</summary>
    public double MaxCatchupSeconds { get; init; } = 0.1;

    /// <summary>Cap on sim steps consumed per rendered frame.</summary>
    public int MaxStepsPerFrame { get; init; } = 8;

    /// <summary>Hit-stop: freeze 40-80ms scaled by damage.</summary>
    public double HitStopMinMs { get; init; } = 40;
    public double HitStopMaxMs { get; init; } = 80;

    /// <summary>Damage that maps to maximum hit-stop.</summary>
    public double HitStopRefDamage { get; init; } = 350;

    /// <summary>Screen-shake trauma added per hit (0..1) and seconds to fully
    /// decay. AOE and ultimate hits shake harder; capped so an ultimate can't be
    /// nauseating.</summary>
    public double ShakePerHit { get; init; } = 0.28;
    public double ShakeBurstMultiplier { get; init; } = 1.6;
    public double ShakeDecaySeconds { get; init; } = 0.5;
    public double ShakeMaxTrauma { get; init; } = 1.0;

    /// <summary>Camera punch (brief zoom pulse) added per ultimate, and its decay.</summary>
    public double PunchPerUltimate { get; init; } = 0.7;
    public double PunchDecaySeconds { get; init; } = 0.45;

    public static readonly FeelConfig Default = new();
}

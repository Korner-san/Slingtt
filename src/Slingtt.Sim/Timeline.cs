namespace Slingtt.Sim;

/// <summary>One beat within a resolution — e.g. a shape flashing, one target
/// taking damage — carrying a time offset relative to the resolution's start so
/// the render layer can stagger playback (a pierce hitting four enemies, a cross
/// hitting six) instead of showing everything in the same frame. Established
/// here for later prompts; nothing consumes OffsetSeconds yet — Prompt 1 only
/// establishes the shape and starts populating it from Ultimates.cs.</summary>
public readonly struct TimelineBeat
{
    public double OffsetSeconds { get; init; }
    public string Kind { get; init; } // "shape" | "hit" today; informal until a consumer needs a stricter contract
    public string? TargetId { get; init; }
}

/// <summary>The ordered beats belonging to one resolution (e.g. one weapon
/// ultimate firing). Attached to the triggering SimEvent so the render layer
/// finds it without a separate lookup.</summary>
public sealed class ResolutionTimeline
{
    public List<TimelineBeat> Beats { get; } = new();
}

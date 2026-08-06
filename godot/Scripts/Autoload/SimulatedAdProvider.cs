using Godot;
using Slingtt.Game;

namespace Slingtt.Render;

/// <summary>Prompt 9 — stand-in for a real rewarded-ad SDK (AdMob, IronSource,
/// whichever mediation layer ships eventually): simulates watching a short ad
/// and then always completes successfully. IAdProvider is the seam — swapping
/// this for a real SDK wrapper never touches AdRewardEconomy or any UI code
/// that calls ShowRewardedAd.</summary>
public sealed partial class SimulatedAdProvider : Node, IAdProvider
{
    private const double SimulatedAdSeconds = 1.2;

    public void ShowRewardedAd(Action onCompleted, Action onFailedOrCancelled)
    {
        SceneTreeTimer timer = GetTree().CreateTimer(SimulatedAdSeconds);
        timer.Timeout += () => onCompleted();
    }
}

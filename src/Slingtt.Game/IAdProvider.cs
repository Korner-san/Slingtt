namespace Slingtt.Game;

/// <summary>Prompt 9 — the seam between the ad-reward economy (AdRewardEconomy,
/// content+data only) and whatever actually shows a rewarded ad, which is
/// inherently an engine/SDK concern. The render layer supplies the real
/// implementation, same pattern as ISaveStore.</summary>
public interface IAdProvider
{
    void ShowRewardedAd(Action onCompleted, Action onFailedOrCancelled);
}

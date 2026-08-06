using Slingtt.Game;
using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 3's content -> BattleSetup wiring: shape structure and scale come
// from the wielding item's rarity, sweep degrees ramp with its level (capped),
// and dual activation is Legendary-only regardless of level. All values here
// are read back off the real embedded content (weapons.json), so this also
// guards against a future content edit silently breaking the escalation.
public class ItemRarityEscalationTests
{
    private static WeaponUltimateSpec UltimateFor(string weaponId, int weaponLevel)
    {
        Content c = Content.Load();
        var team = new List<LoadoutSlot>
        {
            new() { HeroId = "hero_bram", WeaponId = weaponId, ArmorId = "arm_squire_mail", WeaponLevel = weaponLevel, ArmorLevel = 10 },
        };
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 1, team, seed: 1);
        WeaponUltimateSpec? ult = setup.Actors[0].Weapon.Ultimate;
        Assert.True(ult.HasValue, $"{weaponId} at level {weaponLevel} should have an ultimate");
        return ult!.Value;
    }

    [Fact]
    public void HigherRarityCrossWeapon_HasMoreArmsAndAWiderShape()
    {
        // wpn_squire_blade = Common (rarity index 0), wpn_aurelions_requiem =
        // Legendary (index 4), both ult_crossing_slash.
        WeaponUltimateSpec common = UltimateFor("wpn_squire_blade", 30);
        WeaponUltimateSpec legendary = UltimateFor("wpn_aurelions_requiem", 30);

        Assert.Equal(2, common.Shape.ArmCount);   // ArmCountByRarityIndex[0]
        Assert.Equal(4, legendary.Shape.ArmCount); // ArmCountByRarityIndex[4]
        Assert.Equal(1.6, legendary.Shape.Width / common.Shape.Width, precision: 6); // ScalePerRarityIndex 1.6 / 1.0
    }

    [Fact]
    public void HigherRarityBeamWeapon_HasMoreLines()
    {
        // wpn_ash_lance = Common, wpn_skyrend_lance = Legendary, both ult_piercing_ray.
        WeaponUltimateSpec common = UltimateFor("wpn_ash_lance", 30);
        WeaponUltimateSpec legendary = UltimateFor("wpn_skyrend_lance", 30);

        Assert.Single(common.Shape.LineAngles!);         // LineCountByRarityIndex[0]
        Assert.Equal(3, legendary.Shape.LineAngles!.Count); // LineCountByRarityIndex[4]
    }

    [Fact]
    public void HigherRarityAftershockWeapon_HasALargerRadius()
    {
        // wpn_cobble_maul = Common, wpn_sunforge_hammer = Legendary, both ult_aftershock.
        WeaponUltimateSpec common = UltimateFor("wpn_cobble_maul", 30);
        WeaponUltimateSpec legendary = UltimateFor("wpn_sunforge_hammer", 30);

        Assert.Equal(1.6, legendary.Shape.Radius / common.Shape.Radius, precision: 6);
    }

    [Fact]
    public void SweepDegrees_RampsWithLevelAndCapsAtTwenty()
    {
        // wpn_aurelions_requiem: Legendary cross, 4 arms -> 45deg inter-arm gap.
        // level 10 (fraction 9/29): 9/29*45 = 13.9655... -- below the cap.
        WeaponUltimateSpec atUnlock = UltimateFor("wpn_aurelions_requiem", 10);
        Assert.Equal(9.0 / 29.0 * 45.0, atUnlock.SweepDegrees, precision: 4);

        // level 30 (fraction 1): 1*45 = 45, clamped down to the 20deg cap.
        WeaponUltimateSpec atMax = UltimateFor("wpn_aurelions_requiem", 30);
        Assert.Equal(20, atMax.SweepDegrees);
    }

    [Fact]
    public void SweepBidirectional_TrueAsSoonAsTheUltimateUnlocksAtLevelTen()
    {
        WeaponUltimateSpec ult = UltimateFor("wpn_squire_blade", 10);
        Assert.True(ult.SweepBidirectional);
    }

    [Fact]
    public void DualActivation_OnlyOnLegendaryRarity_RegardlessOfLevel()
    {
        // wpn_dawnbreaker = Rare, wpn_aurelions_requiem = Legendary (same ult_crossing_slash kind).
        Assert.False(UltimateFor("wpn_dawnbreaker", 30).DualActivation);
        Assert.True(UltimateFor("wpn_aurelions_requiem", 10).DualActivation); // Legendary from the moment it unlocks
        Assert.True(UltimateFor("wpn_aurelions_requiem", 30).DualActivation);
    }
}

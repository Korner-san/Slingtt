using Slingtt.Game;
using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 3's content -> BattleSetup wiring (reworked in a later live-iteration
// pass, from Cross/Beam/Aftershock shapes to Striker/Boomerang/Grenade
// projectiles): Striker's bullet count and Grenade/Boomerang's reach come
// from the wielding item's rarity, Striker's direction count and Boomerang's
// fan angle ramp with its evolution tier/level, and dual activation is
// Legendary-only regardless of level. All values here are read back off the
// real embedded content (weapons.json), so this also guards against a future
// content edit silently breaking the escalation.
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
    public void HigherRarityStrikerWeapon_FiresMoreBullets()
    {
        // wpn_squire_blade = Common (rarity index 0), wpn_aurelions_requiem =
        // Legendary (index 4), both ult_crossing_slash (Striker).
        WeaponUltimateSpec common = UltimateFor("wpn_squire_blade", 30);
        WeaponUltimateSpec legendary = UltimateFor("wpn_aurelions_requiem", 30);

        Assert.Equal(2, common.BulletCount);    // BulletCountByRarityIndex[0]
        Assert.Equal(4, legendary.BulletCount);  // BulletCountByRarityIndex[4]
    }

    [Fact]
    public void HigherRarityBoomerangWeapon_HasALongerRange()
    {
        // wpn_ash_lance = Common, wpn_skyrend_lance = Legendary, both ult_piercing_ray (Boomerang).
        WeaponUltimateSpec common = UltimateFor("wpn_ash_lance", 30);
        WeaponUltimateSpec legendary = UltimateFor("wpn_skyrend_lance", 30);

        Assert.Equal(1.6, legendary.FanRange / common.FanRange, precision: 6); // ScalePerRarityIndex 1.6 / 1.0
    }

    [Fact]
    public void HigherRarityGrenadeWeapon_HasALargerAoeRadius()
    {
        // wpn_cobble_maul = Common, wpn_sunforge_hammer = Legendary, both ult_aftershock (Grenade).
        WeaponUltimateSpec common = UltimateFor("wpn_cobble_maul", 30);
        WeaponUltimateSpec legendary = UltimateFor("wpn_sunforge_hammer", 30);

        Assert.Equal(1.6, legendary.AoeRadius / common.AoeRadius, precision: 6);
    }

    [Fact]
    public void FanHalfAngle_RampsWithLevelAndCapsAtTwentyDegrees()
    {
        // wpn_skyrend_lance: Legendary boomerang.
        // level 10 (fraction 9/29): 9/29*20deg, converted to radians.
        WeaponUltimateSpec atUnlock = UltimateFor("wpn_skyrend_lance", 10);
        Assert.Equal(9.0 / 29.0 * 20.0 * Math.PI / 180.0, atUnlock.FanHalfAngleRadians, precision: 6);

        // level 30 (fraction 1): exactly the 20deg cap.
        WeaponUltimateSpec atMax = UltimateFor("wpn_skyrend_lance", 30);
        Assert.Equal(20.0 * Math.PI / 180.0, atMax.FanHalfAngleRadians, precision: 6);
    }

    [Theory]
    [InlineData(10, 1)]
    [InlineData(20, 2)]
    [InlineData(30, 4)]
    public void DirectionCount_ScalesWithEvolutionTier_ForStrikerWeapons(int level, int expectedDirections)
    {
        // wpn_squire_blade: Common Striker. Direction 0 is always the exact
        // landing direction regardless of tier — only the count changes.
        WeaponUltimateSpec ult = UltimateFor("wpn_squire_blade", level);
        Assert.Equal(expectedDirections, ult.DirectionCount);
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

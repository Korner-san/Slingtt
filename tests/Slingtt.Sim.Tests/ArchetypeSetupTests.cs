using Slingtt.Game;
using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 6's content -> BattleSetup wiring (the rarity-driven moveDuration
// split) and BattleController's Focus aim-preview extension.
public class ArchetypeSetupTests
{
    private static ArmorArchetype ArchetypeOf(Content c, WorldSetup setup, string heroId)
        => setup.Actors.Single(a => a.Id == heroId).Armor!.Archetype;

    private static int MoveTicksOf(Content c, string weaponId, string armorId, int level)
    {
        var team = new List<LoadoutSlot> { new() { HeroId = "hero_bram", WeaponId = weaponId, ArmorId = armorId, WeaponLevel = level, ArmorLevel = level } };
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 1, team, seed: 1);
        return setup.Actors[0].Armor!.MoveDurationTicks;
    }

    [Fact]
    public void DefaultTeam_UsesOneOfEachArchetype()
    {
        // arm_squire_mail=Balanced, arm_traveler_garb=Light, arm_ironhide_plate=Heavy.
        Content c = Content.Load();
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 1, BattleSetup.DefaultTeam(), seed: 1);

        Assert.Equal(ArmorArchetype.Balanced, ArchetypeOf(c, setup, "hero_bram"));
        Assert.Equal(ArmorArchetype.Light, ArchetypeOf(c, setup, "hero_lyra"));
        Assert.Equal(ArmorArchetype.Heavy, ArchetypeOf(c, setup, "hero_tove"));
    }

    [Fact]
    public void HeavyArchetype_GetsAShorterMoveDuration_AtHigherRarity()
    {
        // arm_ironhide_plate (Common) vs arm_titanheart_aegis (Legendary), both
        // Heavy, both base moveDuration 1.4s. Common: adjust=1 -> 1.4*120=168.
        // Legendary: adjust=1-4*0.05=0.8 -> 1.4*0.8*120=134.4 -> RoundJs -> 134.
        Content c = Content.Load();
        int common = MoveTicksOf(c, "wpn_dawnbreaker", "arm_ironhide_plate", 10);
        int legendary = MoveTicksOf(c, "wpn_dawnbreaker", "arm_titanheart_aegis", 10);

        Assert.Equal(168, common);
        Assert.Equal(134, legendary);
        Assert.True(legendary < common, "higher rarity Heavy armor should be even shorter-duration (more extreme tank)");
    }

    [Fact]
    public void LightArchetype_GetsALongerMoveDuration_AtHigherRarity()
    {
        // arm_traveler_garb (Common) vs arm_zephyrs_embrace (Legendary), both
        // Light, both base moveDuration 3.0s. Common: 3.0*120=360.
        // Legendary: adjust=1+4*0.05=1.2 -> 3.0*1.2*120=432.
        Content c = Content.Load();
        int common = MoveTicksOf(c, "wpn_dawnbreaker", "arm_traveler_garb", 10);
        int legendary = MoveTicksOf(c, "wpn_dawnbreaker", "arm_zephyrs_embrace", 10);

        Assert.Equal(360, common);
        Assert.Equal(432, legendary);
        Assert.True(legendary > common, "higher rarity Light armor should be even longer-duration (more extreme mobility)");
    }

    [Fact]
    public void BalancedArchetype_MoveDuration_IsUntouchedByRarity()
    {
        // arm_squire_mail (Common) vs arm_aegis_of_dawn (Legendary), both
        // Balanced, both base moveDuration 2.2s -> 2.2*120=264 either way.
        Content c = Content.Load();
        int common = MoveTicksOf(c, "wpn_dawnbreaker", "arm_squire_mail", 10);
        int legendary = MoveTicksOf(c, "wpn_dawnbreaker", "arm_aegis_of_dawn", 10);

        Assert.Equal(264, common);
        Assert.Equal(264, legendary);
    }

    // --- Focus (BattleController's aim preview) --------------------------------

    private static WorldSetup OneHeroOneEnemy(ArmorArchetype archetype, int moveDurationTicks)
    {
        var weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 100, Tier = 0 };
        var armor = new ArmorStats { MoveDurationTicks = moveDurationTicks, Archetype = archetype };
        return new WorldSetup
        {
            Seed = 1,
            BoundsW = 200,
            BoundsH = 200,
            Actors = new List<ActorInit>
            {
                new() { Id = "hero", Team = Team.Hero, Pos = new Vec2(100, 100), Radius = 0.5, Hp = 1000, Def = 0, Weapon = weapon, Armor = armor, MoveDurationTicks = moveDurationTicks },
                new() { Id = "enemy", Team = Team.Enemy, Pos = new Vec2(100, 10), Radius = 0.5, Hp = 1000, Def = 0, Weapon = weapon, MoveDurationTicks = moveDurationTicks },
            },
        };
    }

    private static int PreviewPointCount(ArmorArchetype archetype)
    {
        SimConfig cfg = SimConfigBuilder.Build(Content.Load().Balance);
        var controller = new BattleController(OneHeroOneEnemy(archetype, moveDurationTicks: 240), cfg);

        controller.Advance(1.0 / 60.0); // Settling -> the hero's Aiming turn
        Assert.True(controller.IsAwaitingHeroInput());

        controller.BeginAim(new Vec2(100, 100));
        controller.UpdateAim(new Vec2(100, 97)); // drag south 3 units = full draw north (open field, no walls in range)

        return controller.GetAim()!.Prediction!.Points.Count;
    }

    [Fact]
    public void Focus_ExtendsTheAimPreview_ComparedToANonFocusArchetype()
    {
        int balanced = PreviewPointCount(ArmorArchetype.Balanced); // Focus
        int heavy = PreviewPointCount(ArmorArchetype.Heavy); // no Focus

        // previewFractionBase=0.6 vs 1.0 of the same 240-tick budget, in an open
        // field where nothing else (walls, bounce cap) truncates it sooner.
        Assert.True(balanced > heavy, $"expected Focus (Balanced) preview ({balanced}) to run longer than Heavy's ({heavy})");
        Assert.InRange(heavy, 130, 155); // ~0.6 * 240 = 144
        Assert.InRange(balanced, 230, 241); // ~1.0 * 240 = 240
    }
}

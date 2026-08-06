using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Locks the exact behavior of FireWeaponUltimate's shape geometry — Prompt 1
// moved this from a hardcoded per-Kind switch to ShapeResolver + content-driven
// ShapeDef, and this must not change what actually gets hit or how much damage
// lands. Each scenario hand-places targets on and off the shape so both the hit
// and the miss paths are exercised.
//
// The expected HP values below are not guesses: they were captured by running
// these exact scenarios against the pre-refactor Ultimates.cs (the commit
// before this one), via a temporary throwaway test that threw the actual
// values back at me through the xunit failure message. This file is that
// captured baseline turned into real assertions — if it passes, the refactor
// is byte-identical for everything these scenarios exercise.
public class UltimateShapeGoldenMasterTests
{
    private static SimConfig Cfg() => new();

    private static Actor Hero(string id, Vec2 pos, Vec2 lastTravelDir, WeaponUltimateSpec ult) => new()
    {
        Id = id,
        Team = Team.Hero,
        Pos = pos,
        Radius = 0.5,
        Hp = 1000,
        MaxHp = 1000,
        Def = 0,
        Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 100, Tier = 0, Ultimate = ult },
        LastTravelDir = lastTravelDir,
    };

    private static Actor Enemy(string id, Vec2 pos) => new()
    {
        Id = id,
        Team = Team.Enemy,
        Pos = pos,
        Radius = 0.45,
        Hp = 1000,
        MaxHp = 1000,
        Def = 30,
    };

    private static World BuildWorld(uint seed, Actor hero, params Actor[] enemies)
    {
        var w = new World { Rng = new RngState(seed), BoundsW = 20, BoundsH = 20 };
        w.Actors.Add(hero);
        w.Actors.AddRange(enemies);
        return w;
    }

    [Fact]
    public void Cross_Tier1_HitsOnAxisMissesOffAxis()
    {
        WeaponUltimateSpec ult = new()
        {
            Kind = WeaponUltKind.Cross,
            DmgMult = 1.5,
            Shape = new ShapeDef { Type = ShapeType.RadialArms, ArmCount = 2, Width = 1.0 },
        };
        Actor hero = Hero("hero", new Vec2(5, 5), new Vec2(0, 1), ult);
        Actor north = Enemy("north", new Vec2(5, 8));   // dx=0 -> on vertical arm
        Actor east = Enemy("east", new Vec2(9, 5));      // dy=0 -> on horizontal arm
        Actor diag = Enemy("diag", new Vec2(7, 7));      // off both arms, no doubleCross

        World w = BuildWorld(12345, hero, north, east, diag);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        Assert.Equal(857, north.Hp);
        Assert.Equal(866, east.Hp);
        Assert.Equal(1000, diag.Hp); // untouched — proves the shape doesn't over-hit
    }

    [Fact]
    public void Cross_Tier3_DoubleCrossAlsoHitsTheDiagonal()
    {
        WeaponUltimateSpec ult = new()
        {
            Kind = WeaponUltKind.Cross,
            DmgMult = 2.0,
            Shape = new ShapeDef { Type = ShapeType.RadialArms, ArmCount = 4, Width = 1.2 },
        };
        Actor hero = Hero("hero", new Vec2(5, 5), new Vec2(0, 1), ult);
        Actor diag = Enemy("diag", new Vec2(7, 7)); // exactly on the 45 degree arm

        World w = BuildWorld(999, hero, diag);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        Assert.Equal(810, diag.Hp);
    }

    [Fact]
    public void Beam_Tier1_HitsAlongTravelDirectionOnly()
    {
        WeaponUltimateSpec ult = new()
        {
            Kind = WeaponUltKind.Beam,
            DmgMult = 1.1,
            Shape = new ShapeDef { Type = ShapeType.Lines, LineAngles = null, Width = 0.7 },
        };
        Actor hero = Hero("hero", new Vec2(5, 5), new Vec2(0, 1), ult); // travelled north
        Actor north = Enemy("north", new Vec2(5, 9));  // on the primary beam
        Actor west = Enemy("west", new Vec2(1, 5));    // would be on the secondary beam, but tier 1 has none
        Actor off = Enemy("off", new Vec2(2, 8));       // off both

        World w = BuildWorld(42, hero, north, west, off);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        Assert.Equal(899, north.Hp);
        Assert.Equal(1000, west.Hp); // secondary beam not unlocked at tier 1
        Assert.Equal(1000, off.Hp);
    }

    [Fact]
    public void Beam_Tier3_SecondaryBeamAddsThePerpendicularLine()
    {
        WeaponUltimateSpec ult = new()
        {
            Kind = WeaponUltKind.Beam,
            DmgMult = 1.9,
            Shape = new ShapeDef { Type = ShapeType.Lines, LineAngles = new[] { 0.0, Math.PI / 2 }, Width = 1.0 },
        };
        Actor hero = Hero("hero", new Vec2(5, 5), new Vec2(0, 1), ult); // travelled north
        Actor north = Enemy("north", new Vec2(5, 9));  // primary beam
        Actor west = Enemy("west", new Vec2(1, 5));     // secondary (perpendicular) beam

        World w = BuildWorld(7, hero, north, west);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        Assert.Equal(836, north.Hp);
        Assert.Equal(835, west.Hp);
    }

    [Fact]
    public void Aftershock_HitsWithinRadiusAndAppliesStun()
    {
        WeaponUltimateSpec ult = new()
        {
            Kind = WeaponUltKind.Aftershock,
            DmgMult = 2.0,
            StunTurns = 1,
            Shape = new ShapeDef { Type = ShapeType.Rings, Radius = 2.0 },
        };
        Actor hero = Hero("hero", new Vec2(5, 5), new Vec2(0, 1), ult);
        Actor close = Enemy("close", new Vec2(6, 6));  // distance sqrt(2) ~= 1.41, inside radius 2
        Actor far = Enemy("far", new Vec2(9, 9));       // distance ~5.66, outside

        World w = BuildWorld(2026, hero, close, far);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        Assert.Equal(819, close.Hp);
        Assert.Equal(1, close.StunnedTurns);
        Assert.Equal(1000, far.Hp);
        Assert.Equal(0, far.StunnedTurns);
    }

    [Fact]
    public void WeaponUltimateEvent_CarriesAResolutionTimeline()
    {
        WeaponUltimateSpec ult = new()
        {
            Kind = WeaponUltKind.Aftershock,
            DmgMult = 1.0,
            Shape = new ShapeDef { Type = ShapeType.Rings, Radius = 5.0 },
        };
        Actor hero = Hero("hero", new Vec2(5, 5), new Vec2(0, 1), ult);
        Actor a = Enemy("a", new Vec2(6, 6));
        Actor b = Enemy("b", new Vec2(4, 4));

        World w = BuildWorld(1, hero, a, b);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        SimEvent ultEvent = w.Events.Single(e => e.Kind == SimEventKind.WeaponUltimate);
        Assert.NotNull(ultEvent.Timeline);
        Assert.Equal("shape", ultEvent.Timeline!.Beats[0].Kind);
        Assert.Equal(0, ultEvent.Timeline.Beats[0].OffsetSeconds);
        Assert.Equal(2, ultEvent.Timeline.Beats.Count(beat => beat.Kind == "hit"));
    }
}

using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 3's sim-layer payload: the sweep pass, its Aftershock concentric-ring
// variant, bidirectional escalation, and dual activation. Variance is pinned
// to 1.0 (SimConfig's VarianceMin/Max both 1) so every damage number below is
// an exact, hand-derived value from Damage.Apply's formula rather than a
// captured RNG draw: raw = Atk * hitMult, mitigated = raw * (1 - Def/(Def+DefK)),
// and with Def=30/DefK=300 that's always raw * 0.909090..., then JS-style
// round-half-up. Atk=100, DmgMult=2.0 -> mitigated 181.818... -> 182.
// Atk=100, DmgMult=2.0*0.6 (sweep) -> mitigated 109.0909... -> 109.
public class UltimateEscalationTests
{
    private static SimConfig Cfg() => new() { VarianceMin = 1.0, VarianceMax = 1.0 };

    private static Actor Hero(Vec2 pos, Vec2 lastTravelDir, WeaponUltimateSpec ult) => new()
    {
        Id = "hero",
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

    private static World BuildWorld(Actor hero, params Actor[] enemies)
    {
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20 };
        w.Actors.Add(hero);
        w.Actors.AddRange(enemies);
        return w;
    }

    // ArmCount=2 base arms sit at world 0deg/90deg exactly. A 30deg clockwise
    // sweep (RotationOffset -= sweepRad) moves them to -30deg/60deg; a 30deg
    // counter-clockwise sweep moves them to 30deg/120deg. These four targets
    // are hand-placed 4 units out from the hero on exactly one of those four
    // lines each, with >0.95 perpendicular distance from all the others, so
    // each assertion isolates a single line.
    private static readonly Vec2 HeroPos = new(5, 5);
    private static readonly Vec2 OnBaseArm = new(5, 9); // world 90deg, dist 4
    private static readonly Vec2 OnClockwiseSweep = new(8.4641016, 3); // world -30deg, dist 4
    private static readonly Vec2 OnCounterClockwiseSweep = new(3, 8.4641016); // world 120deg, dist 4

    private static WeaponUltimateSpec CrossUlt(double sweepDegrees, bool bidirectional, bool dual = false) => new()
    {
        Kind = WeaponUltKind.Cross,
        DmgMult = 2.0,
        Shape = new ShapeDef { Type = ShapeType.RadialArms, ArmCount = 2, Width = 1.0 },
        SweepDegrees = sweepDegrees,
        SweepBidirectional = bidirectional,
        DualActivation = dual,
    };

    [Fact]
    public void Sweep_HitsTargetTheBaseMissed_AtReducedDamage_WithoutDoubleHittingBaseTargets()
    {
        WeaponUltimateSpec ult = CrossUlt(sweepDegrees: 30, bidirectional: false);
        Actor hero = Hero(HeroPos, new Vec2(0, 1), ult);
        Actor onBase = Enemy("onBase", OnBaseArm);
        Actor swept = Enemy("swept", OnClockwiseSweep);
        Actor ccw = Enemy("ccw", OnCounterClockwiseSweep); // unidirectional: must stay untouched

        World w = BuildWorld(hero, onBase, swept, ccw);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        Assert.Equal(1000 - 182, onBase.Hp); // exactly one base hit, not base+sweep
        Assert.Equal(1000 - 109, swept.Hp); // sweep-only hit at 60% damage
        Assert.Equal(1000, ccw.Hp); // wrong direction, unidirectional sweep never reaches it
    }

    [Fact]
    public void SweepBidirectional_HitsBothClockwiseAndCounterClockwiseTargets()
    {
        WeaponUltimateSpec ult = CrossUlt(sweepDegrees: 30, bidirectional: true);
        Actor hero = Hero(HeroPos, new Vec2(0, 1), ult);
        Actor cw = Enemy("cw", OnClockwiseSweep);
        Actor ccw = Enemy("ccw", OnCounterClockwiseSweep);

        World w = BuildWorld(hero, cw, ccw);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        Assert.Equal(1000 - 109, cw.Hp);
        Assert.Equal(1000 - 109, ccw.Hp);
    }

    [Fact]
    public void Sweep_TimelineBeat_IsAtLeastTheConfiguredMinimumOffsetAfterTheBaseActivation()
    {
        WeaponUltimateSpec ult = CrossUlt(sweepDegrees: 30, bidirectional: false);
        Actor hero = Hero(HeroPos, new Vec2(0, 1), ult);
        Actor swept = Enemy("swept", OnClockwiseSweep);

        World w = BuildWorld(hero, swept);
        SimConfig cfg = Cfg();
        Ultimates.FireWeaponUltimate(w, cfg, hero);

        ResolutionTimeline timeline = w.Events.Single(e => e.Kind == SimEventKind.WeaponUltimate).Timeline!;
        List<TimelineBeat> shapeBeats = timeline.Beats.Where(b => b.Kind == "shape").ToList();

        Assert.Equal(0, shapeBeats[0].OffsetSeconds); // base activation
        Assert.Equal(cfg.SweepMinBeatOffsetSeconds, shapeBeats[1].OffsetSeconds); // sweep beat, floored to the minimum
    }

    private static WeaponUltimateSpec AftershockUlt(bool bidirectional, int stunTurns = 1) => new()
    {
        Kind = WeaponUltKind.Aftershock,
        DmgMult = 2.0,
        StunTurns = stunTurns,
        Shape = new ShapeDef { Type = ShapeType.Rings, Radius = 2.0 },
        SweepBidirectional = bidirectional,
    };

    [Fact]
    public void Aftershock_Escalation_AddsAConcentricRingInsteadOfRotating_AndOnlyBaseHitsStun()
    {
        // base radius 2.0; bonus ring 2.0*1.3=2.6.
        WeaponUltimateSpec ult = AftershockUlt(bidirectional: false);
        Actor hero = Hero(HeroPos, new Vec2(0, 1), ult);
        Actor inner = Enemy("inner", new Vec2(5, 6.5));   // dist 1.5, inside base radius
        Actor bonusRing = Enemy("bonusRing", new Vec2(5, 7.3)); // dist 2.3, between base and bonus radius
        Actor outside = Enemy("outside", new Vec2(5, 8.0));     // dist 3.0, outside even the bonus ring

        World w = BuildWorld(hero, inner, bonusRing, outside);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        Assert.Equal(1000 - 182, inner.Hp);
        Assert.Equal(1, inner.StunnedTurns); // base activation still stuns

        Assert.Equal(1000 - 109, bonusRing.Hp);
        Assert.Equal(0, bonusRing.StunnedTurns); // sweep-equivalent pass never stuns

        Assert.Equal(1000, outside.Hp);
    }

    [Fact]
    public void Aftershock_BidirectionalSweep_FiresASecondEvenLargerRing()
    {
        // second bonus ring: 2.0*1.3*1.3=3.38, reaches the dist-3.0 target that
        // the single-ring case above left untouched.
        WeaponUltimateSpec ult = AftershockUlt(bidirectional: true);
        Actor hero = Hero(HeroPos, new Vec2(0, 1), ult);
        Actor secondRingOnly = Enemy("secondRingOnly", new Vec2(5, 8.0)); // dist 3.0

        World w = BuildWorld(hero, secondRingOnly);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        Assert.Equal(1000 - 109, secondRingOnly.Hp);
        Assert.Equal(0, secondRingOnly.StunnedTurns);
    }

    [Fact]
    public void DualActivation_FiresAFullStrengthSecondPass_ThatCanReHitTheSameTarget()
    {
        WeaponUltimateSpec ult = CrossUlt(sweepDegrees: 0, bidirectional: false, dual: true);
        Actor hero = Hero(HeroPos, new Vec2(0, 1), ult);
        Actor target = Enemy("target", OnBaseArm);

        World w = BuildWorld(hero, target);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        Assert.Equal(1000 - 182 - 182, target.Hp); // hit twice, both at full DmgMult

        ResolutionTimeline timeline = w.Events.Single(e => e.Kind == SimEventKind.WeaponUltimate).Timeline!;
        Assert.Equal(2, timeline.Beats.Count(b => b.Kind == "hit" && b.TargetId == "target"));
    }

    [Fact]
    public void DualActivation_False_FiresOnlyOnce()
    {
        WeaponUltimateSpec ult = CrossUlt(sweepDegrees: 0, bidirectional: false, dual: false);
        Actor hero = Hero(HeroPos, new Vec2(0, 1), ult);
        Actor target = Enemy("target", OnBaseArm);

        World w = BuildWorld(hero, target);
        Ultimates.FireWeaponUltimate(w, Cfg(), hero);

        Assert.Equal(1000 - 182, target.Hp);
    }
}

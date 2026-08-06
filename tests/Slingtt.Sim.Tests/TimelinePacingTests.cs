using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 11 — "post-landing resolution capped at 1800ms worst case at 1x
// speed": Ultimates.FireWeaponUltimate rescales a whole ResolutionTimeline's
// beat offsets down together when the raw HitBeatStaggerSeconds/
// SweepMinBeatOffsetSeconds spacing would run past cfg.TimelineBudgetSeconds.
// Variance pinned to 1.0 so damage numbers aren't relevant noise here — only
// the timeline's OffsetSeconds are under test.
public class TimelinePacingTests
{
    private static SimConfig Cfg(double budgetSeconds = 1.8) => new()
    {
        VarianceMin = 1.0,
        VarianceMax = 1.0,
        TimelineBudgetSeconds = budgetSeconds,
    };

    private static Actor Hero(Vec2 pos, WeaponUltimateSpec ult) => new()
    {
        Id = "hero",
        Team = Team.Hero,
        Pos = pos,
        Radius = 0.5,
        Hp = 1000,
        MaxHp = 1000,
        Def = 0,
        Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 100, Tier = 0, Ultimate = ult },
        LastTravelDir = new Vec2(0, 1),
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
        var w = new World { Rng = new RngState(1), BoundsW = 200, BoundsH = 200 };
        w.Actors.Add(hero);
        w.Actors.AddRange(enemies);
        return w;
    }

    private static ResolutionTimeline TimelineOf(World w)
        => w.Events.Single(e => e.Kind == SimEventKind.WeaponUltimate).Timeline!;

    [Fact]
    public void Timeline_LeavesOffsetsUnchanged_WhenTheRawStaggerAlreadyFitsTheBudget()
    {
        // Single world-anchored arm (angle 0, the +X line through the hero) —
        // every enemy placed on it, well within budget at only 3 hits.
        var ult = new WeaponUltimateSpec
        {
            Kind = WeaponUltKind.Cross,
            DmgMult = 2.0,
            Shape = new ShapeDef { Type = ShapeType.RadialArms, ArmCount = 1, Width = 1.0 },
        };
        Actor hero = Hero(new Vec2(0, 0), ult);
        Actor a = Enemy("a", new Vec2(2, 0));
        Actor b = Enemy("b", new Vec2(4, 0));
        Actor c = Enemy("c", new Vec2(6, 0));

        World w = BuildWorld(hero, a, b, c);
        SimConfig cfg = Cfg();
        Ultimates.FireWeaponUltimate(w, cfg, hero);

        List<TimelineBeat> hits = TimelineOf(w).Beats.Where(x => x.Kind == "hit").OrderBy(x => x.OffsetSeconds).ToList();
        Assert.Equal(3, hits.Count);
        Assert.Equal(0.05, hits[0].OffsetSeconds, 9);
        Assert.Equal(0.10, hits[1].OffsetSeconds, 9);
        Assert.Equal(0.15, hits[2].OffsetSeconds, 9);
    }

    [Fact]
    public void Timeline_CompressesHitStagger_WhenManyTargetsWouldExceedTheBudget()
    {
        var ult = new WeaponUltimateSpec
        {
            Kind = WeaponUltKind.Cross,
            DmgMult = 2.0,
            Shape = new ShapeDef { Type = ShapeType.RadialArms, ArmCount = 1, Width = 1.0 },
        };
        Actor hero = Hero(new Vec2(0, 0), ult);
        var enemies = new Actor[40];
        for (int i = 0; i < enemies.Length; i++)
        {
            enemies[i] = Enemy($"e{i}", new Vec2((i + 1) * 2.0, 0));
        }

        World w = BuildWorld(hero, enemies);
        SimConfig cfg = Cfg(budgetSeconds: 1.8);
        Ultimates.FireWeaponUltimate(w, cfg, hero);

        List<TimelineBeat> hits = TimelineOf(w).Beats.Where(x => x.Kind == "hit").OrderBy(x => x.OffsetSeconds).ToList();
        Assert.Equal(40, hits.Count);

        // Raw (uncompressed) last offset would be 40 * 0.05 = 2.0s, over budget
        // -> scale = 1.8 / 2.0 = 0.9 -> hit i (1-indexed) lands at i * 0.05 * 0.9 = i * 0.045.
        Assert.Equal(0.045, hits[0].OffsetSeconds, 9);
        Assert.Equal(1.8, hits[^1].OffsetSeconds, 9); // clamped to exactly the budget

        // Every enemy actually still took damage — compression changes WHEN a
        // beat plays, never WHETHER the hit itself happened.
        Assert.All(enemies, e => Assert.True(e.Hp < e.MaxHp));
    }

    [Fact]
    public void Timeline_AftershockBothRings_ShareOneClampedBudget_InsteadOfEachGettingItsOwn()
    {
        // Radius 1.0 -> ring1 radius 1.3, ring2 (bidirectional) radius 1.69.
        var ult = new WeaponUltimateSpec
        {
            Kind = WeaponUltKind.Aftershock,
            DmgMult = 2.0,
            SweepBidirectional = true,
            Shape = new ShapeDef { Type = ShapeType.Rings, Radius = 1.0 },
        };
        Actor hero = Hero(new Vec2(0, 0), ult);

        // Rings is a pure distance-from-origin check, so every target must sit
        // exactly on its band's circle (spread by angle, not raw XY offset) or
        // it silently drifts out of range and the hit count assertions below
        // go quiet instead of failing loudly.
        var ring1Targets = new Actor[20]; // dist 1.2: inside ring1 (1.3), outside base (1.0)
        for (int i = 0; i < ring1Targets.Length; i++)
        {
            double a = i * (2 * Math.PI / ring1Targets.Length);
            ring1Targets[i] = Enemy($"r1_{i}", new Vec2(1.2 * Math.Cos(a), 1.2 * Math.Sin(a)));
        }
        var ring2Targets = new Actor[20]; // dist 1.5: inside ring2 (1.69), outside ring1 (1.3)
        for (int i = 0; i < ring2Targets.Length; i++)
        {
            double a = i * (2 * Math.PI / ring2Targets.Length);
            ring2Targets[i] = Enemy($"r2_{i}", new Vec2(1.5 * Math.Cos(a), 1.5 * Math.Sin(a)));
        }

        var enemies = ring1Targets.Concat(ring2Targets).ToArray();
        World w = BuildWorld(hero, enemies);
        SimConfig cfg = Cfg(budgetSeconds: 1.8);
        Ultimates.FireWeaponUltimate(w, cfg, hero);

        ResolutionTimeline timeline = TimelineOf(w);
        Assert.Equal(1.8, timeline.Beats.Max(b => b.OffsetSeconds), 9); // shared cap, not per-ring

        double lastRing1Offset = timeline.Beats
            .Where(b => b.Kind == "hit" && b.TargetId!.StartsWith("r1_"))
            .Max(b => b.OffsetSeconds);
        double firstRing2Offset = timeline.Beats
            .Where(b => b.Kind == "hit" && b.TargetId!.StartsWith("r2_"))
            .Min(b => b.OffsetSeconds);
        Assert.True(firstRing2Offset > lastRing1Offset); // ring2 still plays strictly after ring1, just compressed

        Assert.All(ring1Targets, e => Assert.True(e.Hp < e.MaxHp));
        Assert.All(ring2Targets, e => Assert.True(e.Hp < e.MaxHp));
    }
}

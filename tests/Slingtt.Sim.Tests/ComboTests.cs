using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 5 — the contact combo. Touching your other active hero mid-travel
// fires their ultimate at 1.25x scale and ticks a global stack counter that
// buffs active-hero damage (+6%/stack, capped at 10) and decays one stack per
// turn without a proc. Damage numbers are hand-derived, not RNG-captured:
// variance is pinned to 1.0, so raw = Atk*hitMult*comboMult, mitigated =
// raw*(1-Def/(Def+DefK)). Atk=100, Def=30, DefK=300 -> factor 0.909090...
// No stacks: 90.909 -> RoundJs -> 91. 5 stacks (1.3x): 118.18 -> 118.
public class ComboTests
{
    private static SimConfig Cfg() => new() { VarianceMin = 1.0, VarianceMax = 1.0 };

    private static Actor Hero(string id, Vec2 pos, WeaponUltimateSpec? ult = null, bool benched = false) => new()
    {
        Id = id,
        Team = Team.Hero,
        Pos = pos,
        Radius = 0.5,
        Hp = 1000,
        MaxHp = 1000,
        Def = 20,
        Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 100, Tier = 0, Ultimate = ult },
        IsBenched = benched,
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
        Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 50, Tier = 0 },
    };

    private static World BuildWorld(params Actor[] actors)
    {
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20 };
        w.Actors.AddRange(actors);
        return w;
    }

    private static WeaponUltimateSpec Grenade(double aoeRadius) => new()
    {
        Kind = WeaponUltKind.Grenade,
        DmgMult = 1.0,
        AoeRadius = aoeRadius,
    };

    [Fact]
    public void CheckContact_FiresTheTouchedHeroUltimate_At1_25xScale_AndIncrementsTheCounter()
    {
        Actor toucher = Hero("toucher", new Vec2(5, 5));
        Actor teammate = Hero("teammate", new Vec2(5.3, 5), ult: Grenade(2.0)); // within touch range
        World w = BuildWorld(toucher, teammate);

        Combo.CheckContact(w, Cfg(), toucher);

        Assert.Equal(1, w.ComboStacks);
        Assert.True(w.ComboContactThisTurn);
        Assert.True(toucher.ComboFiredThisTravel);

        SimEvent ult = w.Events.Single(e => e.Kind == SimEventKind.WeaponUltimate);
        Assert.Equal("teammate", ult.ActorId);
        Assert.Equal(2.5, ult.WeaponUlt!.Value.AoeRadius, precision: 6); // 2.0 * 1.25

        Assert.Contains(w.Events, e => e.Kind == SimEventKind.ComboContact && e.ActorId == "toucher" && e.TargetId == "teammate");
    }

    [Fact]
    public void CheckContact_FiresAtMostOncePerTravel()
    {
        Actor toucher = Hero("toucher", new Vec2(5, 5));
        Actor teammate = Hero("teammate", new Vec2(5.3, 5), ult: Grenade(2.0));
        World w = BuildWorld(toucher, teammate);

        Combo.CheckContact(w, Cfg(), toucher);
        Combo.CheckContact(w, Cfg(), toucher); // still overlapping, same travel

        Assert.Equal(1, w.ComboStacks);
        Assert.Equal(1, w.Events.Count(e => e.Kind == SimEventKind.WeaponUltimate));
    }

    [Fact]
    public void CheckContact_CapsAtTenStacks()
    {
        Actor toucher = Hero("toucher", new Vec2(5, 5));
        Actor teammate = Hero("teammate", new Vec2(5.3, 5), ult: Grenade(2.0));
        World w = BuildWorld(toucher, teammate);

        for (int i = 0; i < 15; i++)
        {
            toucher.ComboFiredThisTravel = false; // simulate a fresh travel each time
            Combo.CheckContact(w, Cfg(), toucher);
        }

        Assert.Equal(Combo.MaxStacks, w.ComboStacks);
    }

    [Fact]
    public void CheckContact_NotTouchingYet_DoesNothing()
    {
        Actor toucher = Hero("toucher", new Vec2(5, 5));
        Actor teammate = Hero("teammate", new Vec2(9, 9), ult: Grenade(2.0)); // far away
        World w = BuildWorld(toucher, teammate);

        Combo.CheckContact(w, Cfg(), toucher);

        Assert.Equal(0, w.ComboStacks);
        Assert.False(w.ComboContactThisTurn);
        Assert.False(toucher.ComboFiredThisTravel);
        Assert.DoesNotContain(w.Events, e => e.Kind == SimEventKind.WeaponUltimate);
    }

    [Fact]
    public void CheckContact_BlockedEntirely_AgainstAMarkedHero()
    {
        Actor toucher = Hero("toucher", new Vec2(5, 5));
        Actor teammate = Hero("teammate", new Vec2(5.3, 5), ult: Grenade(2.0));
        teammate.MarkedTurns = 2; // Prompt 7 — IsMarked is now MarkedTurns > 0
        World w = BuildWorld(toucher, teammate);

        Combo.CheckContact(w, Cfg(), toucher);

        Assert.Equal(0, w.ComboStacks);
        Assert.False(toucher.ComboFiredThisTravel);
        Assert.DoesNotContain(w.Events, e => e.Kind == SimEventKind.WeaponUltimate);
    }

    [Fact]
    public void CheckContact_NoOtherActiveHero_DoesNothing()
    {
        // The only other hero is benched — nobody to touch.
        Actor toucher = Hero("toucher", new Vec2(5, 5));
        Actor benched = Hero("benched", new Vec2(5.3, 5), ult: Grenade(2.0), benched: true);
        World w = BuildWorld(toucher, benched);

        Combo.CheckContact(w, Cfg(), toucher);

        Assert.Equal(0, w.ComboStacks);
    }

    [Fact]
    public void DamageMultiplier_ScalesActiveHeroDamage_ByStackCount()
    {
        Actor source = Hero("source", new Vec2(0, 0));
        Actor target = Enemy("target", new Vec2(1, 0));
        World w = BuildWorld(source, target);
        w.ComboStacks = 5; // 1 + 5*0.06 = 1.3x

        double? dealt = Damage.Apply(w, Cfg(), source, target, hitMult: 1.0, HitKind.Contact, target.Pos);

        Assert.Equal(118, dealt);
    }

    [Fact]
    public void DamageMultiplier_AtZeroStacks_MatchesTheUnbuffedBaseline()
    {
        Actor source = Hero("source", new Vec2(0, 0));
        Actor target = Enemy("target", new Vec2(1, 0));
        World w = BuildWorld(source, target);

        double? dealt = Damage.Apply(w, Cfg(), source, target, hitMult: 1.0, HitKind.Contact, target.Pos);

        Assert.Equal(91, dealt);
    }

    [Fact]
    public void DamageMultiplier_NeverAppliesToEnemyDamage()
    {
        Actor source = Enemy("source", new Vec2(0, 0));
        Actor target = Hero("target", new Vec2(1, 0));
        World w = BuildWorld(source, target);
        w.ComboStacks = 5;

        double? dealt = Damage.Apply(w, Cfg(), source, target, hitMult: 1.0, HitKind.Contact, target.Pos);

        // source.Atk=50 here (the Enemy helper), Def=20 (the Hero helper's target).
        // raw=50, mitigated=50*(1-20/320)=46.875 -> RoundJs -> 47, unaffected by ComboStacks.
        Assert.Equal(47, dealt);
    }

    [Fact]
    public void DamageMultiplier_NeverAppliesToABenchedHerosDamage()
    {
        Actor source = Hero("source", new Vec2(0, 0), benched: true);
        Actor target = Enemy("target", new Vec2(1, 0));
        World w = BuildWorld(source, target);
        w.ComboStacks = 5;

        double? dealt = Damage.Apply(w, Cfg(), source, target, hitMult: 1.0, HitKind.Contact, target.Pos);

        Assert.Equal(91, dealt); // unbuffed baseline, same as zero stacks
    }

    [Fact]
    public void TurnOrder_DecaysOneStack_WhenATurnPassesWithoutContact()
    {
        Actor active1 = Hero("active1", new Vec2(2, 2));
        Actor active2 = Hero("active2", new Vec2(4, 2));
        Actor enemy = Enemy("enemy", new Vec2(2, 8));
        World w = BuildWorld(active1, active2, enemy);
        w.ComboStacks = 5;

        TurnOrder.AdvanceRoundRobin(w, Cfg());

        Assert.Equal(4, w.ComboStacks);
    }

    [Fact]
    public void TurnOrder_DoesNotDecay_WhenContactHappenedThisTurn()
    {
        Actor active1 = Hero("active1", new Vec2(2, 2));
        Actor active2 = Hero("active2", new Vec2(4, 2));
        Actor enemy = Enemy("enemy", new Vec2(2, 8));
        World w = BuildWorld(active1, active2, enemy);
        w.ComboStacks = 5;
        w.ComboContactThisTurn = true; // simulates a proc during the turn that just ended

        TurnOrder.AdvanceRoundRobin(w, Cfg());

        Assert.Equal(5, w.ComboStacks);
        Assert.False(w.ComboContactThisTurn); // reset for the new turn
    }

    [Fact]
    public void TurnOrder_ComboStacks_NeverGoNegative()
    {
        Actor active1 = Hero("active1", new Vec2(2, 2));
        Actor active2 = Hero("active2", new Vec2(4, 2));
        Actor enemy = Enemy("enemy", new Vec2(2, 8));
        World w = BuildWorld(active1, active2, enemy);

        TurnOrder.AdvanceRoundRobin(w, Cfg());

        Assert.Equal(0, w.ComboStacks);
    }

    [Fact]
    public void ComboStacks_PersistThroughASwap()
    {
        Actor active = Hero("active", new Vec2(3, 3));
        Actor benched = Hero("benched", new Vec2(6, 6), benched: true, ult: Grenade(1.0));
        World w = BuildWorld(active, benched);
        w.ActiveActorId = "active";
        w.Phase = Phase.Aiming;
        w.ComboStacks = 4;

        BattleSim.TrySwap(w, Cfg());

        Assert.Equal(4, w.ComboStacks); // swap itself never touches the counter
    }
}

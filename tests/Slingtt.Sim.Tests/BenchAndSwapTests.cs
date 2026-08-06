using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 4's sim-layer payload: party of 2 active / 1 benched. TurnOrder skips
// benched heroes (and regenerates them each turn), BattleSim.TrySwap moves the
// benched hero onto the outgoing hero's tile and fires its ultimate on arrival,
// and Collision/EnemyAi/Ultimates all treat a benched hero as untargetable.
public class BenchAndSwapTests
{
    private static SimConfig Cfg(double benchRegenPct = 0.05) => new()
    {
        VarianceMin = 1.0,
        VarianceMax = 1.0,
        BenchRegenPctPerTurn = benchRegenPct,
    };

    private static Actor Hero(string id, Vec2 pos, bool benched = false, WeaponUltimateSpec? ult = null) => new()
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

    [Fact]
    public void TurnOrder_NeverGrantsATurnToABenchedHero_WhileAnActiveHeroLives()
    {
        Actor active1 = Hero("active1", new Vec2(2, 2));
        Actor active2 = Hero("active2", new Vec2(4, 2));
        Actor benched = Hero("benched", new Vec2(6, 2), benched: true);
        Actor enemy = Enemy("enemy", new Vec2(2, 8));
        World w = BuildWorld(active1, active2, benched, enemy);
        SimConfig cfg = Cfg();

        for (int i = 0; i < 20; i++)
        {
            TurnOrder.AdvanceRoundRobin(w, cfg);
            Assert.NotEqual("benched", w.ActiveActorId);
        }
    }

    [Fact]
    public void TurnOrder_RegeneratesTheBenchedHero_EveryTurnGranted()
    {
        Actor active1 = Hero("active1", new Vec2(2, 2));
        Actor benched = Hero("benched", new Vec2(6, 2), benched: true);
        benched.Hp = 500; // half HP, room to regen
        Actor enemy = Enemy("enemy", new Vec2(2, 8));
        World w = BuildWorld(active1, benched, enemy);
        SimConfig cfg = Cfg(benchRegenPct: 0.05); // 5% of 1000 max = 50/turn

        TurnOrder.AdvanceRoundRobin(w, cfg); // active1's turn: bench regen fires once
        Assert.Equal(550, benched.Hp);

        TurnOrder.AdvanceRoundRobin(w, cfg); // enemy's turn: regen fires again, "every turn" includes enemy turns
        Assert.Equal(600, benched.Hp);
    }

    [Fact]
    public void TurnOrder_BenchedHeroRegen_NeverExceedsMaxHp()
    {
        Actor active1 = Hero("active1", new Vec2(2, 2));
        Actor benched = Hero("benched", new Vec2(6, 2), benched: true);
        benched.Hp = 980; // within one regen tick of max
        Actor enemy = Enemy("enemy", new Vec2(2, 8));
        World w = BuildWorld(active1, benched, enemy);

        TurnOrder.AdvanceRoundRobin(w, Cfg(benchRegenPct: 0.05));

        Assert.Equal(1000, benched.Hp);
    }

    [Fact]
    public void TrySwap_MovesIncomingToTheOutgoingTile_BenchesTheOutgoing_AndFiresTheIncomingUltimateOnArrival()
    {
        WeaponUltimateSpec incomingUlt = new()
        {
            Kind = WeaponUltKind.Aftershock,
            DmgMult = 2.0,
            Shape = new ShapeDef { Type = ShapeType.Rings, Radius = 3.0 },
        };
        Actor active = Hero("active", new Vec2(3, 3));
        Actor benched = Hero("benched", new Vec2(6, 6), benched: true, ult: incomingUlt);
        Actor enemyInRange = Enemy("enemyInRange", new Vec2(3, 4)); // within 3.0 of (3,3), the outgoing tile
        World w = BuildWorld(active, benched, enemyInRange);
        w.ActiveActorId = "active";
        w.Phase = Phase.Aiming;

        bool ok = BattleSim.TrySwap(w, Cfg());

        Assert.True(ok);
        Assert.True(active.IsBenched); // outgoing benched
        Assert.False(benched.IsBenched); // incoming activated
        Assert.Equal(new Vec2(3, 3), benched.Pos); // entered at the outgoing tile
        Assert.Equal(Phase.Settling, w.Phase); // consumed the whole turn

        Assert.Contains(w.Events, e => e.Kind == SimEventKind.Swap && e.ActorId == "active" && e.TargetId == "benched");
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.WeaponUltimate && e.ActorId == "benched");
        Assert.True(enemyInRange.Hp < 1000); // arrival ultimate actually fired from the new position
    }

    [Fact]
    public void TrySwap_ReturnsFalse_WhenNoLivingBenchedTeammateExists()
    {
        Actor active1 = Hero("active1", new Vec2(2, 2));
        Actor active2 = Hero("active2", new Vec2(4, 2));
        World w = BuildWorld(active1, active2);
        w.ActiveActorId = "active1";
        w.Phase = Phase.Aiming;

        bool ok = BattleSim.TrySwap(w, Cfg());

        Assert.False(ok);
        Assert.Equal(Phase.Aiming, w.Phase); // nothing consumed
    }

    [Fact]
    public void TrySwap_ReturnsFalse_WhenTheOnlyBenchedTeammateIsDead()
    {
        Actor active = Hero("active", new Vec2(2, 2));
        Actor benchedDead = Hero("benchedDead", new Vec2(6, 2), benched: true);
        benchedDead.Hp = 0;
        World w = BuildWorld(active, benchedDead);
        w.ActiveActorId = "active";
        w.Phase = Phase.Aiming;

        Assert.False(BattleSim.TrySwap(w, Cfg()));
    }

    [Fact]
    public void TurnOrder_AutoActivatesTheBenchedHero_WhenBothActiveHeroesDie()
    {
        Actor active1 = Hero("active1", new Vec2(2, 2));
        active1.Hp = 0; // dead
        Actor active2 = Hero("active2", new Vec2(4, 2));
        active2.Hp = 0; // dead
        Actor benched = Hero("benched", new Vec2(6, 2), benched: true);
        Actor enemy = Enemy("enemy", new Vec2(2, 8));
        World w = BuildWorld(active1, active2, benched, enemy);

        // Without the fallback this would either soft-lock (never grant a hero
        // turn again) or hit the "failed to find a living actor" guard.
        for (int i = 0; i < 8 && w.ActiveActorId != "benched"; i++)
        {
            TurnOrder.AdvanceRoundRobin(w, Cfg());
        }

        Assert.Equal("benched", w.ActiveActorId);
        Assert.False(benched.IsBenched);
    }

    [Fact]
    public void Collision_NeverHitsABenchedHero_EvenWhenDirectlyInThePath()
    {
        Actor benched = Hero("benched", new Vec2(5, 5), benched: true);
        Actor enemy = new()
        {
            Id = "enemy",
            Team = Team.Enemy,
            Pos = new Vec2(5, 9),
            Radius = 0.45,
            Hp = 1000,
            MaxHp = 1000,
            Def = 0,
            Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 100, Tier = 0 },
            Vel = new Vec2(0, -20), // straight at the benched hero
            TravelTicksRemaining = 60,
        };
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20 };
        w.Actors.Add(benched);
        w.Actors.Add(enemy);
        SimConfig cfg = Cfg();

        for (int i = 0; i < 60; i++)
        {
            Collision.IntegrateTravel(w, cfg, enemy, cfg.Dt);
        }

        Assert.Equal(1000, benched.Hp); // passed straight through, untouched
    }
}

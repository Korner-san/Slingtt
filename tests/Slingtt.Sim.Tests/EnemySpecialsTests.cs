using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 7's sim-layer payload: Trail hazards, Chain's two-hit halt, Marker's
// mark, and boss split-on-death. Damage numbers are hand-derived, not
// RNG-captured: variance pinned to 1.0.
public class EnemySpecialsTests
{
    private static SimConfig Cfg() => new()
    {
        VarianceMin = 1.0,
        VarianceMax = 1.0,
        HazardDamageMult = 0.5,
        HazardRadius = 0.6,
        HazardDropIntervalTicks = 18,
        MarkDurationTurns = 2,
    };

    private static Actor Hero(string id, Vec2 pos, bool benched = false) => new()
    {
        Id = id,
        Team = Team.Hero,
        Pos = pos,
        Radius = 0.5,
        Hp = 1000,
        MaxHp = 1000,
        Def = 30,
        Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 100, Tier = 0 },
        IsBenched = benched,
    };

    private static Actor Enemy(string id, Vec2 pos, WeaponType type = WeaponType.Sword, bool markerSpit = false) => new()
    {
        Id = id,
        Team = Team.Enemy,
        Pos = pos,
        Radius = 0.45,
        Hp = 1000,
        MaxHp = 1000,
        Def = 30,
        Weapon = new WeaponStats { Type = type, Atk = 100, Tier = 0, HasMarkerSpit = markerSpit },
    };

    private static World BuildWorld(params Actor[] actors)
    {
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20 };
        w.Actors.AddRange(actors);
        return w;
    }

    // --- Hazard ------------------------------------------------------------

    [Fact]
    public void MaybeDropTrail_DropsImmediatelyOnTheFirstTravelTick()
    {
        Actor trail = Enemy("trail", new Vec2(5, 5), WeaponType.Trail);
        World w = BuildWorld(trail); // TrailDropCooldownTicks defaults to 0

        Hazards.MaybeDropTrail(w, Cfg(), trail);

        Assert.Single(w.Hazards);
        Assert.Equal(new Vec2(5, 5), w.Hazards[0].Pos);
        Assert.Equal(Team.Hero, w.Hazards[0].HostileTo);
    }

    [Fact]
    public void MaybeDropTrail_RespectsTheDropInterval()
    {
        Actor trail = Enemy("trail", new Vec2(5, 5), WeaponType.Trail);
        World w = BuildWorld(trail);
        SimConfig cfg = Cfg();

        Hazards.MaybeDropTrail(w, cfg, trail); // tick 1: drops, cooldown reset to 18
        for (int i = 0; i < cfg.HazardDropIntervalTicks - 1; i++)
        {
            Hazards.MaybeDropTrail(w, cfg, trail);
        }

        Assert.Single(w.Hazards); // still just the one, interval not yet elapsed
    }

    [Fact]
    public void MaybeDropTrail_NeverFiresForNonTrailWeapons()
    {
        Actor sword = Enemy("sword", new Vec2(5, 5), WeaponType.Sword);
        World w = BuildWorld(sword);

        Hazards.MaybeDropTrail(w, Cfg(), sword);

        Assert.Empty(w.Hazards);
    }

    [Fact]
    public void CheckContact_DamagesAHostileVictim_OnceOnly()
    {
        Actor victim = Hero("victim", new Vec2(5, 5));
        World w = BuildWorld(victim);
        w.Hazards.Add(new Hazard { Id = "h1", Pos = new Vec2(5, 5), Radius = 0.6, ExpiresRound = 1, HostileTo = Team.Hero, Atk = 100 });
        SimConfig cfg = Cfg();

        Hazards.CheckContact(w, cfg, victim); // raw=100*0.5=50, mitigated=50*(1-30/330)=45.45 -> 45
        double afterFirst = victim.Hp;
        Hazards.CheckContact(w, cfg, victim); // same hazard point, same victim: no second hit

        Assert.Equal(1000 - 45, afterFirst);
        Assert.Equal(afterFirst, victim.Hp);
    }

    [Fact]
    public void CheckContact_IgnoresHazardsHostileToTheOtherTeam()
    {
        Actor enemy = Enemy("enemy", new Vec2(5, 5));
        World w = BuildWorld(enemy);
        w.Hazards.Add(new Hazard { Id = "h1", Pos = new Vec2(5, 5), Radius = 0.6, ExpiresRound = 1, HostileTo = Team.Hero, Atk = 100 });

        Hazards.CheckContact(w, Cfg(), enemy);

        Assert.Equal(1000, enemy.Hp);
    }

    [Fact]
    public void CheckContact_IgnoresExpiredHazards()
    {
        Actor victim = Hero("victim", new Vec2(5, 5));
        World w = BuildWorld(victim);
        w.Round = 3;
        w.Hazards.Add(new Hazard { Id = "h1", Pos = new Vec2(5, 5), Radius = 0.6, ExpiresRound = 1, HostileTo = Team.Hero, Atk = 100 });

        Hazards.CheckContact(w, Cfg(), victim);

        Assert.Equal(1000, victim.Hp);
    }

    [Fact]
    public void CheckContact_NeverHitsABenchedHero()
    {
        Actor victim = Hero("victim", new Vec2(5, 5), benched: true);
        World w = BuildWorld(victim);
        w.Hazards.Add(new Hazard { Id = "h1", Pos = new Vec2(5, 5), Radius = 0.6, ExpiresRound = 1, HostileTo = Team.Hero, Atk = 100 });

        Hazards.CheckContact(w, Cfg(), victim);

        Assert.Equal(1000, victim.Hp);
    }

    [Fact]
    public void PruneExpired_RemovesHazardsPastTheirRound()
    {
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20, Round = 3 };
        w.Hazards.Add(new Hazard { Id = "old", Pos = Vec2.Zero, ExpiresRound = 1 });
        w.Hazards.Add(new Hazard { Id = "fresh", Pos = Vec2.Zero, ExpiresRound = 5 });

        Hazards.PruneExpired(w);

        Assert.Equal(new[] { "fresh" }, w.Hazards.ConvertAll(h => h.Id));
    }

    // --- TrailBehavior / ChainBehavior --------------------------------------

    [Fact]
    public void TrailBehavior_DealsNoDamageAndNeverDeflectsOrStops()
    {
        Actor trail = Enemy("trail", new Vec2(5, 5), WeaponType.Trail);
        Actor target = Hero("target", new Vec2(5.4, 5));
        World w = BuildWorld(trail, target);

        ContactResult result = Weapons.Behavior(WeaponType.Trail).OnEnemyContact(w, Cfg(), trail, target, new ContactInfo { Pos = target.Pos });

        Assert.False(result.Deflect);
        Assert.False(result.Stop);
        Assert.Equal(1000, target.Hp);
    }

    [Fact]
    public void ChainBehavior_BouncesOnTheFirstHeroContact_ThenHaltsOnTheSecond()
    {
        Actor chain = Enemy("chain", new Vec2(5, 5), WeaponType.Chain);
        Actor a = Hero("a", new Vec2(5.4, 5));
        Actor b = Hero("b", new Vec2(5.8, 5));
        World w = BuildWorld(chain, a, b);
        SimConfig cfg = Cfg();
        IWeaponBehavior behavior = Weapons.Behavior(WeaponType.Chain);

        ContactResult first = behavior.OnEnemyContact(w, cfg, chain, a, new ContactInfo { Pos = a.Pos });
        Assert.True(first.Deflect);
        Assert.False(first.Stop);

        ContactResult second = behavior.OnEnemyContact(w, cfg, chain, b, new ContactInfo { Pos = b.Pos });
        Assert.False(second.Deflect);
        Assert.True(second.Stop);

        // Both hits deal full, undecayed damage: raw=100, mitigated=100*(1-30/330)=90.9 -> 91.
        Assert.Equal(1000 - 91, a.Hp);
        Assert.Equal(1000 - 91, b.Hp);
    }

    // --- EnemyAi (Chain targeting) -------------------------------------------

    [Fact]
    public void ChainAi_AimsBetweenTheTwoActiveHeroes()
    {
        Actor chain = Enemy("chain", new Vec2(5, 0), WeaponType.Chain);
        Actor a = Hero("a", new Vec2(0, 10));
        Actor b = Hero("b", new Vec2(10, 10));
        World w = BuildWorld(chain, a, b); // midpoint (5, 10): straight north of the chain enemy

        LaunchInput input = EnemyAi.DecideLaunch(w, chain);

        double angle = Math.Atan2(input.DirY, input.DirX);
        Assert.InRange(angle, Math.PI / 2 - 0.1, Math.PI / 2 + 0.1); // ~90deg (straight "north"), plus jitter
    }

    [Fact]
    public void ChainAi_FallsBackToNearestHero_WhenOnlyOneActiveHeroExists()
    {
        Actor chain = Enemy("chain", new Vec2(5, 0), WeaponType.Chain);
        Actor onlyActive = Hero("onlyActive", new Vec2(5, 10));
        Actor benched = Hero("benched", new Vec2(0, 10), benched: true);
        World w = BuildWorld(chain, onlyActive, benched);

        LaunchInput input = EnemyAi.DecideLaunch(w, chain);

        double angle = Math.Atan2(input.DirY, input.DirX);
        Assert.InRange(angle, Math.PI / 2 - 0.1, Math.PI / 2 + 0.1); // still points at onlyActive, not the benched one
    }

    // --- MarkerSpit ----------------------------------------------------------

    [Fact]
    public void MarkerSpit_MarksTheFarthestActiveHero()
    {
        Actor marker = Enemy("marker", new Vec2(0, 0), markerSpit: true);
        Actor near = Hero("near", new Vec2(1, 0));
        Actor far = Hero("far", new Vec2(10, 0));
        World w = BuildWorld(marker, near, far);

        MarkerSpit.FireIfApplicable(w, Cfg(), marker);

        Assert.Equal(2, far.MarkedTurns);
        Assert.Equal(0, near.MarkedTurns);
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.MarkApplied && e.ActorId == "marker" && e.TargetId == "far");
    }

    [Fact]
    public void MarkerSpit_IgnoresBenchedHeroes_WhenPickingTheFarthest()
    {
        Actor marker = Enemy("marker", new Vec2(0, 0), markerSpit: true);
        Actor near = Hero("near", new Vec2(1, 0));
        Actor farButBenched = Hero("farButBenched", new Vec2(10, 0), benched: true);
        World w = BuildWorld(marker, near, farButBenched);

        MarkerSpit.FireIfApplicable(w, Cfg(), marker);

        Assert.Equal(2, near.MarkedTurns);
        Assert.Equal(0, farButBenched.MarkedTurns);
    }

    [Fact]
    public void MarkerSpit_NoOps_WithoutTheFlag()
    {
        Actor notAMarker = Enemy("notAMarker", new Vec2(0, 0), markerSpit: false);
        Actor hero = Hero("hero", new Vec2(10, 0));
        World w = BuildWorld(notAMarker, hero);

        MarkerSpit.FireIfApplicable(w, Cfg(), notAMarker);

        Assert.Equal(0, hero.MarkedTurns);
    }

    [Fact]
    public void TurnOrder_DecaysMarkedTurns_EveryTurnGranted()
    {
        Actor hero1 = Hero("hero1", new Vec2(2, 2));
        hero1.MarkedTurns = 2;
        Actor hero2 = Hero("hero2", new Vec2(4, 2));
        Actor enemy = Enemy("enemy", new Vec2(2, 8));
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20 };
        w.Actors.Add(hero1);
        w.Actors.Add(hero2);
        w.Actors.Add(enemy);

        TurnOrder.AdvanceRoundRobin(w, Cfg());

        Assert.Equal(1, hero1.MarkedTurns);
    }

    // --- Split on death --------------------------------------------------------

    [Fact]
    public void SpawnSplitChildren_SpawnsEachBlueprint_OffsetAroundTheDeathPosition()
    {
        var boss = new Actor
        {
            Id = "boss",
            Team = Team.Enemy,
            Pos = new Vec2(5, 5),
            Radius = 0.9,
            Hp = 0,
            MaxHp = 1000,
            Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 50 },
            SplitOnDeath = new List<ActorInit>
            {
                new() { Id = "boss_split0", Team = Team.Enemy, Radius = 0.45, Hp = 200, Weapon = new WeaponStats { Type = WeaponType.Trail, Atk = 40 } },
                new() { Id = "boss_split1", Team = Team.Enemy, Radius = 0.45, Hp = 200, Weapon = new WeaponStats { Type = WeaponType.Trail, Atk = 40 } },
            },
        };
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20 };
        w.Actors.Add(boss);

        Damage.SpawnSplitChildren(w, boss);

        Assert.Equal(3, w.Actors.Count); // boss + 2 children
        Actor c0 = w.GetActor("boss_split0");
        Actor c1 = w.GetActor("boss_split1");
        Assert.Equal(200, c0.Hp);
        Assert.NotEqual(boss.Pos, c0.Pos); // spread out, not stacked on the death point
        Assert.NotEqual(c0.Pos, c1.Pos); // and not stacked on each other either
        Assert.Equal(2, w.Events.Count(e => e.Kind == SimEventKind.EnemySplit && e.ActorId == "boss"));
    }

    [Fact]
    public void SpawnSplitChildren_NoOps_ForActorsWithoutSplitBlueprints()
    {
        var enemy = new Actor { Id = "enemy", Team = Team.Enemy, Pos = new Vec2(5, 5), Weapon = new WeaponStats { Type = WeaponType.Sword } };
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20 };
        w.Actors.Add(enemy);

        Damage.SpawnSplitChildren(w, enemy);

        Assert.Single(w.Actors);
    }

    [Fact]
    public void MultiTargetUltimate_KillingASplitCapableEnemy_DoesNotThrow_AndStillHitsOtherTargets()
    {
        // Regression test: a hero ultimate that hits several targets in one
        // pass used to crash if one of them died and spawned split children —
        // Damage.Apply calling World.SpawnActor added to world.Actors while
        // Ultimates.FireShapeQuery was still enumerating it. Caught by the
        // floor 10/15 full-battle smoke tests; this pins the exact scenario.
        WeaponUltimateSpec ult = new()
        {
            Kind = WeaponUltKind.Aftershock,
            DmgMult = 10.0, // guarantees a kill regardless of variance
            Shape = new ShapeDef { Type = ShapeType.Rings, Radius = 5.0 },
        };
        var hero = new Actor
        {
            Id = "hero",
            Team = Team.Hero,
            Pos = new Vec2(0, 0),
            Radius = 0.5,
            Hp = 1000,
            MaxHp = 1000,
            Def = 30,
            Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 500, Tier = 0, Ultimate = ult },
        };
        var splitCapable = new Actor
        {
            Id = "splitCapable",
            Team = Team.Enemy,
            Pos = new Vec2(1, 0),
            Radius = 0.45,
            Hp = 1,
            MaxHp = 1000,
            Def = 0,
            Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 50 },
            SplitOnDeath = new List<ActorInit>
            {
                new() { Id = "splitCapable_split0", Team = Team.Enemy, Radius = 0.4, Hp = 100, Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 30 } },
            },
        };
        Actor other = Enemy("other", new Vec2(2, 0));
        World w = BuildWorld(hero, splitCapable, other);

        Ultimates.FireWeaponUltimate(w, Cfg(), hero); // must not throw

        Assert.Equal(0, splitCapable.Hp);
        Assert.Contains(w.Actors, a => a.Id == "splitCapable_split0"); // split child actually spawned
        Assert.True(other.Hp < 1000); // the query kept going and still hit the second target
    }

    [Fact]
    public void DyingToDamage_ActuallySpawnsTheSplitChildren_EndToEnd()
    {
        var boss = new Actor
        {
            Id = "boss",
            Team = Team.Enemy,
            Pos = new Vec2(5, 5),
            Radius = 0.9,
            Hp = 10, // one hit from killing it
            MaxHp = 1000,
            Def = 0,
            Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 50 },
            SplitOnDeath = new List<ActorInit>
            {
                new() { Id = "boss_split0", Team = Team.Enemy, Radius = 0.45, Hp = 200, Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 40 } },
            },
        };
        Actor attacker = Hero("attacker", new Vec2(5, 6));
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20 };
        w.Actors.Add(attacker);
        w.Actors.Add(boss);

        Damage.Apply(w, Cfg(), attacker, boss, hitMult: 5.0, HitKind.Contact, boss.Pos);

        Assert.Equal(0, boss.Hp);
        Assert.Contains(w.Actors, a => a.Id == "boss_split0");
    }
}

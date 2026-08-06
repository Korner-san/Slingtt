using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

public class BattleSimTests
{
    [Fact]
    public void FirstStep_StartsRoundOneOnFirstActor()
    {
        World w = TestWorlds.OneOnOne();
        SimConfig cfg = TestWorlds.Config();

        BattleSim.Step(w, cfg);

        Assert.Equal(Phase.Aiming, w.Phase);
        Assert.Equal(1, w.Round);
        Assert.Equal("hero", w.ActiveActorId);
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.RoundStart);
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.TurnStart);
    }

    [Fact]
    public void HeroAimingPhase_ConsumesNoTicks()
    {
        World w = TestWorlds.OneOnOne();
        SimConfig cfg = TestWorlds.Config();
        TestWorlds.StartFirstTurn(w, cfg);

        int before = w.Tick;
        for (int i = 0; i < 50; i++)
        {
            BattleSim.Step(w, cfg);
        }

        Assert.Equal(before, w.Tick); // the sim waits for player input
        Assert.Equal(Phase.Aiming, w.Phase);
    }

    [Fact]
    public void Launch_BelowMinDrawRatio_IsRejectedAsCancel()
    {
        World w = TestWorlds.OneOnOne();
        SimConfig cfg = TestWorlds.Config();
        TestWorlds.StartFirstTurn(w, cfg);

        bool launched = BattleSim.TryLaunch(w, cfg, new LaunchInput { DirX = 0, DirY = -1, DrawRatio = 0.1 });

        Assert.False(launched);
        Assert.Equal(Phase.Aiming, w.Phase);
    }

    [Fact]
    public void Launch_ThenTravel_EventuallyStops()
    {
        World w = TestWorlds.OneOnOne();
        SimConfig cfg = TestWorlds.Config();
        TestWorlds.StartFirstTurn(w, cfg);

        Assert.True(BattleSim.TryLaunch(w, cfg, new LaunchInput { DirX = 0, DirY = -1, DrawRatio = 1.0 }));
        Assert.Equal(Phase.Travelling, w.Phase);

        for (int i = 0; i < 5000 && w.Phase == Phase.Travelling; i++)
        {
            BattleSim.Step(w, cfg);
        }

        Assert.NotEqual(Phase.Travelling, w.Phase);
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.Stopped);
    }

    [Fact]
    public void TravellingActor_NeverLeavesTheArena()
    {
        World w = TestWorlds.OneOnOne();
        SimConfig cfg = TestWorlds.Config();
        TestWorlds.StartFirstTurn(w, cfg);
        BattleSim.TryLaunch(w, cfg, new LaunchInput { DirX = 0.7, DirY = -0.7, DrawRatio = 1.0 });

        Actor hero = w.GetActor("hero");
        for (int i = 0; i < 5000 && w.Phase != Phase.Ended; i++)
        {
            BattleSim.Step(w, cfg);
            Assert.InRange(hero.Pos.X, hero.Radius - 1e-6, w.BoundsW - hero.Radius + 1e-6);
            Assert.InRange(hero.Pos.Y, hero.Radius - 1e-6, w.BoundsH - hero.Radius + 1e-6);
        }
    }

    [Fact]
    public void SwordContact_DamagesTheEnemy()
    {
        World w = TestWorlds.OneOnOne(WeaponType.Sword);
        SimConfig cfg = TestWorlds.Config();
        TestWorlds.StartFirstTurn(w, cfg);

        Actor enemy = w.GetActor("enemy");
        double hpBefore = enemy.Hp;
        BattleSim.TryLaunch(w, cfg, new LaunchInput { DirX = 0, DirY = -1, DrawRatio = 1.0 });

        for (int i = 0; i < 5000 && w.Phase == Phase.Travelling; i++)
        {
            BattleSim.Step(w, cfg);
        }

        Assert.True(enemy.Hp < hpBefore, "a straight shot down the arena should connect");
    }

    [Fact]
    public void HammerContact_StopsTravelAndDetonates()
    {
        World w = TestWorlds.OneOnOne(WeaponType.Hammer);
        SimConfig cfg = TestWorlds.Config();
        TestWorlds.StartFirstTurn(w, cfg);
        BattleSim.TryLaunch(w, cfg, new LaunchInput { DirX = 0, DirY = -1, DrawRatio = 1.0 });

        for (int i = 0; i < 5000 && w.Phase == Phase.Travelling; i++)
        {
            BattleSim.Step(w, cfg);
        }

        Assert.Equal(0, w.GetActor("hero").Vel.Length());
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.Hit && e.HitKind == HitKind.Aoe);
    }

    [Fact]
    public void KillingTheLastEnemy_EndsTheBattleForHeroes()
    {
        World w = TestWorlds.OneOnOne(WeaponType.Sword, enemyHp: 1);
        SimConfig cfg = TestWorlds.Config();
        TestWorlds.StartFirstTurn(w, cfg);
        BattleSim.TryLaunch(w, cfg, new LaunchInput { DirX = 0, DirY = -1, DrawRatio = 1.0 });

        for (int i = 0; i < 20000 && w.Phase != Phase.Ended; i++)
        {
            BattleSim.Step(w, cfg);
        }

        Assert.Equal(Phase.Ended, w.Phase);
        Assert.Equal(Team.Hero, w.Winner);
    }

    [Fact]
    public void SameSeedAndInputs_ProduceIdenticalOutcomes()
    {
        static (int Tick, double Hp, double X, double Y) Run()
        {
            World w = TestWorlds.OneOnOne();
            SimConfig cfg = TestWorlds.Config();
            TestWorlds.StartFirstTurn(w, cfg);
            BattleSim.TryLaunch(w, cfg, new LaunchInput { DirX = 0.3, DirY = -1, DrawRatio = 0.9 });
            for (int i = 0; i < 3000; i++)
            {
                BattleSim.Step(w, cfg);
            }
            Actor e = w.GetActor("enemy");
            return (w.Tick, e.Hp, e.Pos.X, e.Pos.Y);
        }

        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Prediction_DoesNotMutateTheLiveWorld()
    {
        World w = TestWorlds.OneOnOne();
        SimConfig cfg = TestWorlds.Config();
        TestWorlds.StartFirstTurn(w, cfg);

        Actor hero = w.GetActor("hero");
        Actor enemy = w.GetActor("enemy");
        Vec2 heroPos = hero.Pos;
        double enemyHp = enemy.Hp;
        uint rngBefore = w.Rng.S;
        int tickBefore = w.Tick;

        Prediction? p = Predict.Trajectory(w, cfg, new LaunchInput { DirX = 0, DirY = -1, DrawRatio = 1.0 });

        Assert.NotNull(p);
        Assert.True(p!.Points.Count > 1);
        Assert.Equal(heroPos, hero.Pos);
        Assert.Equal(enemyHp, enemy.Hp);
        Assert.Equal(rngBefore, w.Rng.S); // no RNG draws: damage is disabled in the clone
        Assert.Equal(tickBefore, w.Tick);
        Assert.Equal(Phase.Aiming, w.Phase);
    }

    [Fact]
    public void TurnLimit_EndsTheBattleForEnemies()
    {
        World w = TestWorlds.OneOnOne(enemyHp: 1_000_000);
        var cfg = new SimConfig { TurnLimit = 2 };
        TestWorlds.StartFirstTurn(w, cfg);

        for (int i = 0; i < 500_000 && w.Phase != Phase.Ended; i++)
        {
            if (w.Phase == Phase.Aiming && w.ActiveActor().Team == Team.Hero)
            {
                BattleSim.TryLaunch(w, cfg, new LaunchInput { DirX = 0, DirY = -1, DrawRatio = 1.0 });
            }
            BattleSim.Step(w, cfg);
        }

        Assert.Equal(Phase.Ended, w.Phase);
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.BattleEnd && e.Reason == EndReason.TurnLimit);
    }
}

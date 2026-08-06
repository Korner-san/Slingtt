using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 10 — Predict.Trajectory's enemy-bounce capture. Wall-bounce capture
// was already covered indirectly elsewhere; this pins the new EnemyBounces
// list specifically, since it's the thing the aim-preview extension actually
// needs new sim-layer support for.
public class PredictTests
{
    private static Actor Hero(Vec2 pos) => new()
    {
        Id = "hero",
        Team = Team.Hero,
        Pos = pos,
        Radius = 0.5,
        Hp = 1000,
        MaxHp = 1000,
        Def = 0,
        Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 100, Tier = 0 },
        MoveDurationTicks = 240,
    };

    private static Actor Enemy(Vec2 pos, WeaponType type = WeaponType.Sword) => new()
    {
        Id = "enemy",
        Team = Team.Enemy,
        Pos = pos,
        Radius = 0.5,
        Hp = 1000,
        MaxHp = 1000,
        Def = 0,
        Weapon = new WeaponStats { Type = type, Atk = 50 },
    };

    private static World AimingWorld(params Actor[] actors)
    {
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20, ActiveActorId = "hero", Phase = Phase.Aiming };
        w.Actors.AddRange(actors);
        return w;
    }

    [Fact]
    public void Trajectory_CapturesEnemyBouncePoints_ForASwordTypeDeflection()
    {
        World world = AimingWorld(Hero(new Vec2(5, 5)), Enemy(new Vec2(5, 8)));
        SimConfig cfg = new();

        Prediction? prediction = Predict.Trajectory(world, cfg, new LaunchInput { DirX = 0, DirY = 1, DrawRatio = 1.0 });

        Assert.NotNull(prediction);
        Assert.NotEmpty(prediction!.EnemyBounces);
    }

    [Fact]
    public void Trajectory_ReportsNoEnemyBounces_WhenNothingIsInThePath()
    {
        World world = AimingWorld(Hero(new Vec2(5, 5)), Enemy(new Vec2(15, 15))); // well clear of the shot
        SimConfig cfg = new();

        Prediction? prediction = Predict.Trajectory(world, cfg, new LaunchInput { DirX = 0, DirY = 1, DrawRatio = 1.0 });

        Assert.NotNull(prediction);
        Assert.Empty(prediction!.EnemyBounces);
    }

    [Fact]
    public void Trajectory_ReportsNoEnemyBounces_ForNonDeflectingWeapons()
    {
        // Lance passes through, so no EnemyBounce event ever fires for it —
        // the hero here is Sword, but the ENEMY's type is irrelevant to
        // whether the hero deflects; what matters is the shooter's own type.
        // Swap the shooter to a Lance-equivalent by using the enemy as the
        // one being predicted isn't possible (prediction is always the active
        // actor), so instead verify indirectly: a Lance hero never deflects.
        var lanceHero = new Actor
        {
            Id = "hero",
            Team = Team.Hero,
            Pos = new Vec2(5, 5),
            Radius = 0.5,
            Hp = 1000,
            MaxHp = 1000,
            Def = 0,
            Weapon = new WeaponStats { Type = WeaponType.Lance, Atk = 100, Tier = 0, PierceCount = 3 },
            MoveDurationTicks = 240,
        };
        World world = AimingWorld(lanceHero, Enemy(new Vec2(5, 8)));
        SimConfig cfg = new();

        Prediction? prediction = Predict.Trajectory(world, cfg, new LaunchInput { DirX = 0, DirY = 1, DrawRatio = 1.0 });

        Assert.NotNull(prediction);
        Assert.Empty(prediction!.EnemyBounces); // Lance pierces through, never deflects
    }
}

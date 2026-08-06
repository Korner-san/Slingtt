using Slingtt.Sim;

namespace Slingtt.Sim.Tests;

/// <summary>Hand-built worlds so sim tests never depend on the content catalog.</summary>
internal static class TestWorlds
{
    public static SimConfig Config() => new();

    public static WeaponStats Weapon(
        WeaponType type = WeaponType.Sword,
        double atk = 100,
        int tier = 0,
        WeaponUltimateSpec? ult = null)
        => new()
        {
            Type = type,
            Atk = atk,
            Tier = tier,
            BounceDecay = 0.1,
            PierceCount = 5,
            AoeRadius = 1.8,
            Ultimate = ult,
        };

    public static World OneOnOne(
        WeaponType heroWeapon = WeaponType.Sword,
        double enemyHp = 400,
        uint seed = 12345)
        => World.Create(new WorldSetup
        {
            Seed = seed,
            BoundsW = 9,
            BoundsH = 16,
            Actors = new List<ActorInit>
            {
                new()
                {
                    Id = "hero", Team = Team.Hero, Pos = new Vec2(4.5, 13.5), Radius = 0.5,
                    Hp = 1000, Def = 50, Weapon = Weapon(heroWeapon), MoveDurationTicks = 240,
                },
                new()
                {
                    Id = "enemy", Team = Team.Enemy, Pos = new Vec2(4.5, 3.0), Radius = 0.45,
                    Hp = enemyHp, Def = 30, Weapon = Weapon(WeaponType.Sword, 55), MoveDurationTicks = 190,
                },
            },
        });

    /// <summary>Run Settling once so the world lands on the first actor's turn.</summary>
    public static void StartFirstTurn(World w, SimConfig cfg)
    {
        BattleSim.Step(w, cfg);
    }
}

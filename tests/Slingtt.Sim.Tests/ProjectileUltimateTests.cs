using Slingtt.Game;
using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Live-iteration rework — weapon ultimates fire as real, tick-simulated
// traveling projectiles (Projectiles.cs) instead of an instant shape query.
// Variance pinned to 1.0 throughout, so every damage number is hand-derived,
// not RNG-captured: raw = Atk*DmgMult, mitigated = raw*(1-Def/(Def+DefK)),
// and with Def=0 that's always raw unmitigated.
public class ProjectileUltimateTests
{
    private static SimConfig Cfg() => new()
    {
        VarianceMin = 1.0,
        VarianceMax = 1.0,
        UltimateProjectileSpeed = 3.5,
        UltimateProjectileMaxRange = 14.0,
    };

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
        Def = 0,
    };

    private static World BuildWorld(Actor hero, params Actor[] enemies)
    {
        var w = new World { Rng = new RngState(1), BoundsW = 200, BoundsH = 200 };
        w.Actors.Add(hero);
        w.Actors.AddRange(enemies);
        return w;
    }

    private static void AdvanceUntilResolved(World w, SimConfig cfg, int maxTicks = 500)
    {
        for (int i = 0; i < maxTicks && w.Projectiles.Count > 0; i++)
        {
            Projectiles.Advance(w, cfg);
        }
    }

    // --- Striker ---------------------------------------------------------------

    [Fact]
    public void Striker_SpawnsBulletCountTimesDirectionCount_TotalBullets()
    {
        var ult = new WeaponUltimateSpec { Kind = WeaponUltKind.Striker, DmgMult = 1.0, BulletCount = 3, DirectionCount = 2 };
        Actor hero = Hero("hero", new Vec2(0, 0), new Vec2(0, 1), ult);
        World w = BuildWorld(hero);

        Projectiles.Spawn(w, Cfg(), hero);

        Assert.Equal(6, w.Projectiles.Count);
        Assert.All(w.Projectiles, p => Assert.Equal(ProjectileKind.Bullet, p.Kind));
    }

    [Fact]
    public void Striker_FallsBackToTheLandingDirection_WhenNoLivingEnemyExists()
    {
        var ult = new WeaponUltimateSpec { Kind = WeaponUltKind.Striker, DmgMult = 1.0, BulletCount = 1, DirectionCount = 3 };
        var landingDir = new Vec2(0.6, 0.8); // arbitrary, already unit length
        Actor hero = Hero("hero", new Vec2(0, 0), landingDir, ult);
        World w = BuildWorld(hero); // no enemies at all — the edge case landingDir exists for

        Projectiles.Spawn(w, Cfg(), hero);

        Vec2 firstBulletDir = w.Projectiles[0].Vel.Normalized();
        Assert.Equal(landingDir.X, firstBulletDir.X, precision: 9);
        Assert.Equal(landingDir.Y, firstBulletDir.Y, precision: 9);
    }

    [Fact]
    public void Striker_AimsAtTheNearestLivingEnemy_NotTheLandingDirection()
    {
        var ult = new WeaponUltimateSpec { Kind = WeaponUltKind.Striker, DmgMult = 1.0, BulletCount = 1, DirectionCount = 1 };
        // Landing direction points due north (0,1), but the nearest enemy is
        // off to the east — the bullet should aim at the enemy, not follow
        // the landing direction at all.
        Actor hero = Hero("hero", new Vec2(0, 0), new Vec2(0, 1), ult);
        Actor near = Enemy("near", new Vec2(3, 0));
        Actor far = Enemy("far", new Vec2(0, 8));
        World w = BuildWorld(hero, near, far);

        Projectiles.Spawn(w, Cfg(), hero);

        Vec2 dir = w.Projectiles[0].Vel.Normalized();
        Assert.Equal(1.0, dir.X, precision: 9); // straight toward "near" at (3,0), i.e. +X
        Assert.Equal(0.0, dir.Y, precision: 9);
    }

    [Fact]
    public void Striker_BulletStopsAtTheFirstEnemyInItsPath_NeverReachingASecondOneBehindIt()
    {
        var ult = new WeaponUltimateSpec { Kind = WeaponUltKind.Striker, DmgMult = 1.0, BulletCount = 1, DirectionCount = 1 };
        Actor hero = Hero("hero", new Vec2(0, 0), new Vec2(0, 1), ult); // fires straight along +Y
        Actor near = Enemy("near", new Vec2(0, 2));
        Actor far = Enemy("far", new Vec2(0, 5)); // directly behind "near" on the same line
        World w = BuildWorld(hero, near, far);
        SimConfig cfg = Cfg();

        Projectiles.Spawn(w, cfg, hero);
        AdvanceUntilResolved(w, cfg);

        Assert.Empty(w.Projectiles); // consumed on its one hit, not still flying
        Assert.Equal(900, near.Hp); // 100 Atk * 1.0 DmgMult, Def 0
        Assert.Equal(1000, far.Hp); // never reached — no pierce
    }

    // --- Grenade -----------------------------------------------------------------

    [Fact]
    public void Grenade_TargetsTheFurthestLivingEnemy_AndItsAoeHitsEveryoneNearThatPoint()
    {
        var ult = new WeaponUltimateSpec { Kind = WeaponUltKind.Grenade, DmgMult = 2.0, StunTurns = 1, AoeRadius = 2.0 };
        Actor hero = Hero("hero", new Vec2(0, 0), new Vec2(0, 1), ult);
        Actor furthest = Enemy("furthest", new Vec2(5, 0));     // dist 5 — becomes the target point
        Actor nearTarget = Enemy("nearTarget", new Vec2(4, 0));  // dist 1 from the target point — inside AoeRadius 2.0
        Actor outOfRange = Enemy("outOfRange", new Vec2(0, 3));  // dist ~5.83 from the target point — outside it
        World w = BuildWorld(hero, furthest, nearTarget, outOfRange);
        SimConfig cfg = Cfg();

        Projectiles.Spawn(w, cfg, hero);
        UltimateProjectile grenade = Assert.Single(w.Projectiles);
        Assert.Equal(furthest.Pos.X, grenade.TargetPos.X, precision: 9);
        Assert.Equal(furthest.Pos.Y, grenade.TargetPos.Y, precision: 9);

        AdvanceUntilResolved(w, cfg);

        Assert.Empty(w.Projectiles);
        Assert.Equal(800, furthest.Hp);   // 100 * 2.0, Def 0
        Assert.Equal(1, furthest.StunnedTurns);
        Assert.Equal(800, nearTarget.Hp); // caught by the same AoE
        Assert.Equal(1, nearTarget.StunnedTurns);
        Assert.Equal(1000, outOfRange.Hp); // untouched — outside the blast radius
        Assert.Equal(0, outOfRange.StunnedTurns);
    }

    [Fact]
    public void Grenade_StillExplodesAtItsRecordedTargetPoint_EvenIfThatEnemyDiedFirst()
    {
        Actor hero = Hero("hero", new Vec2(0, 0), new Vec2(0, 1),
            new WeaponUltimateSpec { Kind = WeaponUltKind.Grenade, DmgMult = 1.0, AoeRadius = 1.0 });
        Actor originalTarget = Enemy("originalTarget", new Vec2(5, 0));
        Actor bystander = Enemy("bystander", new Vec2(5.5, 0)); // still near (5,0) once the original target is gone
        World w = BuildWorld(hero, originalTarget, bystander);
        originalTarget.Hp = 0; // died before the grenade could arrive (e.g. an earlier bullet in the same cast)
        SimConfig cfg = Cfg();

        var grenade = new UltimateProjectile
        {
            OwnerId = "hero",
            Team = Team.Hero,
            Kind = ProjectileKind.Grenade,
            Pos = new Vec2(4.9, 0),
            Vel = new Vec2(1, 0) * cfg.UltimateProjectileSpeed,
            DmgMult = 1.0,
            TargetPos = new Vec2(5, 0),
            AoeRadius = 1.0,
        };
        w.Projectiles.Add(grenade);

        AdvanceUntilResolved(w, cfg);

        Assert.Empty(w.Projectiles);
        Assert.True(bystander.Hp < 1000); // exploded at (5,0) regardless, hitting whoever's there now
    }

    // --- Boomerang -----------------------------------------------------------------

    [Fact]
    public void Boomerang_AimsAtTheLivingEnemyCentroid_HittingBothOfASymmetricPair()
    {
        var ult = new WeaponUltimateSpec
        {
            Kind = WeaponUltKind.Boomerang,
            DmgMult = 1.5,
            FanRange = 4.0,
            FanHalfAngleRadians = Deg(30),
        };
        // Landing direction is due north, but neither enemy sits on that
        // axis — placed symmetrically at +/-20 degrees from it instead, so
        // their centroid falls exactly back on the north axis (the X
        // components cancel exactly in floating point) and the boomerang
        // should still catch both, proving it aimed at the pair's midpoint
        // rather than the landing direction or either enemy individually.
        Actor hero = Hero("hero", new Vec2(0, 0), new Vec2(0, 1), ult);
        double r = 3.0;
        Actor left = Enemy("left", new Vec2(-r * Math.Sin(Deg(20)), r * Math.Cos(Deg(20))));
        Actor right = Enemy("right", new Vec2(r * Math.Sin(Deg(20)), r * Math.Cos(Deg(20))));
        World w = BuildWorld(hero, left, right);
        SimConfig cfg = Cfg();

        Projectiles.Spawn(w, cfg, hero);
        AdvanceUntilResolved(w, cfg);

        Assert.Equal(850, left.Hp);  // 100 * 1.5, Def 0
        Assert.Equal(850, right.Hp);
    }

    [Fact]
    public void Boomerang_HitsInsideItsFanAngleAndRange_MissesOutsideEither_AndOnlyEverResolvesOnce()
    {
        // Constructs the projectile directly instead of going through
        // Spawn's own centroid targeting (covered above) — this is purely
        // about AdvanceBoomerang's fan hit-test geometry.
        Actor hero = Hero("hero", new Vec2(0, 0), new Vec2(0, 1),
            new WeaponUltimateSpec { Kind = WeaponUltKind.Boomerang, DmgMult = 1.5 });

        // Polar around the +Y axis at radius 3 (comfortably inside FanRange 4):
        // inFan at 10 degrees off-axis (inside the 30-degree half-angle),
        // outFan at 45 degrees (outside it, despite being the same distance).
        double r = 3.0;
        Actor inFan = Enemy("inFan", new Vec2(r * Math.Sin(Deg(10)), r * Math.Cos(Deg(10))));
        Actor outFan = Enemy("outFan", new Vec2(r * Math.Sin(Deg(45)), r * Math.Cos(Deg(45))));
        Actor tooFar = Enemy("tooFar", new Vec2(0, 6)); // perfectly on-axis, but beyond FanRange 4
        World w = BuildWorld(hero, inFan, outFan, tooFar);
        SimConfig cfg = Cfg();

        var boomerang = new UltimateProjectile
        {
            OwnerId = "hero",
            Team = Team.Hero,
            Kind = ProjectileKind.Boomerang,
            Pos = new Vec2(0, 0),
            Vel = new Vec2(0, 1) * cfg.UltimateProjectileSpeed,
            DmgMult = 1.5,
            Origin = new Vec2(0, 0),
            Dir = new Vec2(0, 1),
            FanRange = 4.0,
            CosFanHalfAngle = Math.Cos(Deg(30)),
        };
        w.Projectiles.Add(boomerang);

        AdvanceUntilResolved(w, cfg);

        Assert.Empty(w.Projectiles); // resolves exactly once, then removed — can't hit again
        Assert.Equal(850, inFan.Hp);    // 100 * 1.5, Def 0 — inside angle and range
        Assert.Equal(1000, outFan.Hp);  // right range, wrong angle
        Assert.Equal(1000, tooFar.Hp);  // right angle, wrong range
    }

    private static double Deg(double degrees) => degrees * Math.PI / 180.0;

    // --- Dual activation -----------------------------------------------------------

    [Fact]
    public void DualActivation_SpawnsASecondWave_DelayedAfterTheFirst()
    {
        var ult = new WeaponUltimateSpec
        {
            Kind = WeaponUltKind.Striker,
            DmgMult = 1.0,
            BulletCount = 1,
            DirectionCount = 1,
            DualActivation = true,
        };
        Actor hero = Hero("hero", new Vec2(0, 0), new Vec2(0, 1), ult);
        World w = BuildWorld(hero);

        Projectiles.Spawn(w, Cfg(), hero);

        Assert.Equal(2, w.Projectiles.Count);
        Assert.Equal(0, w.Projectiles[0].DelaySeconds);      // first wave fires immediately
        Assert.True(w.Projectiles[1].DelaySeconds > 0);       // second wave is genuinely, visibly later
    }

    // --- Balance: Striker hits harder, since it can never multi-target -------------

    [Fact]
    public void StrikerDamage_IsHigherThanGrenadeAndBoomerang_AtTheSameEvolutionTier()
    {
        Content c = Content.Load();
        var team = new List<LoadoutSlot>
        {
            new() { HeroId = "hero_bram", WeaponId = "wpn_dawnbreaker", ArmorId = "arm_squire_mail", WeaponLevel = 30, ArmorLevel = 10 },
        };
        WeaponUltimateSpec striker = BattleSetup.Build(c, 1, team, seed: 1).Actors[0].Weapon.Ultimate!.Value;

        team[0] = new LoadoutSlot { HeroId = "hero_bram", WeaponId = "wpn_sunforge_hammer", ArmorId = "arm_squire_mail", WeaponLevel = 30, ArmorLevel = 10 };
        WeaponUltimateSpec grenade = BattleSetup.Build(c, 1, team, seed: 1).Actors[0].Weapon.Ultimate!.Value;

        team[0] = new LoadoutSlot { HeroId = "hero_bram", WeaponId = "wpn_stormpike", ArmorId = "arm_squire_mail", WeaponLevel = 30, ArmorLevel = 10 };
        WeaponUltimateSpec boomerang = BattleSetup.Build(c, 1, team, seed: 1).Actors[0].Weapon.Ultimate!.Value;

        Assert.Equal(WeaponUltKind.Striker, striker.Kind);
        Assert.Equal(WeaponUltKind.Grenade, grenade.Kind);
        Assert.Equal(WeaponUltKind.Boomerang, boomerang.Kind);
        Assert.True(striker.DmgMult > grenade.DmgMult, "a single bullet should hit harder than a single grenade hit, since it can never multi-target");
        Assert.True(striker.DmgMult > boomerang.DmgMult, "a single bullet should hit harder than a single fan hit, since it can never multi-target");
    }
}

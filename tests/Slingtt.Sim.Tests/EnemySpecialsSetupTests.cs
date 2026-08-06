using Slingtt.Game;
using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 7's content -> BattleSetup wiring: the new weapon types parse
// correctly, HasMarkerSpit/SplitOnDeath get pre-resolved onto the sim actors,
// and floors 6-15 (including the new boss floors) actually build and play to
// completion without throwing — the real "did this content break the sim"
// check, more rigorous than any screenshot since it runs the deterministic
// simulation thousands of ticks deep.
public class EnemySpecialsSetupTests
{
    [Fact]
    public void ParseWeaponType_RecognizesTrailAndChain()
    {
        Assert.Equal(WeaponType.Trail, BattleSetup.ParseWeaponType("trail"));
        Assert.Equal(WeaponType.Chain, BattleSetup.ParseWeaponType("chain"));
    }

    [Fact]
    public void Floor6_TrailLayer_HasTheTrailWeaponType()
    {
        Content c = Content.Load();
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 6, BattleSetup.DefaultTeam(), seed: 1);

        ActorInit trail = setup.Actors.Single(a => a.Id.StartsWith("enm_trail_layer"));
        Assert.Equal(WeaponType.Trail, trail.Weapon.Type);
    }

    [Fact]
    public void Floor8_Marker_HasMarkerSpitSet()
    {
        Content c = Content.Load();
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 8, BattleSetup.DefaultTeam(), seed: 1);

        ActorInit marker = setup.Actors.Single(a => a.Id.StartsWith("enm_marker"));
        Assert.True(marker.Weapon.HasMarkerSpit);
    }

    [Fact]
    public void Floor10Boss_HasSplitOnDeathChildren_ScaledForThatFloor()
    {
        Content c = Content.Load();
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 10, BattleSetup.DefaultTeam(), seed: 1);

        ActorInit boss = setup.Actors.Single(a => a.Team == Team.Enemy);
        Assert.NotNull(boss.SplitOnDeath);
        Assert.Equal(2, boss.SplitOnDeath!.Count);
        foreach (ActorInit child in boss.SplitOnDeath)
        {
            Assert.Equal(WeaponType.Trail, child.Weapon.Type);
            Assert.True(child.Hp > 0);
            // Split children are the base enm_trail_layer, not another boss — a
            // fresh trail-layer at floor 10's scale should be far weaker than the
            // boss itself, not a second copy of it.
            Assert.True(child.Hp < boss.Hp);
        }
    }

    [Fact]
    public void Floor15_HasBothChainAndMarkerBosses()
    {
        Content c = Content.Load();
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 15, BattleSetup.DefaultTeam(), seed: 1);

        List<ActorInit> enemies = setup.Actors.Where(a => a.Team == Team.Enemy).ToList();
        Assert.Equal(2, enemies.Count);
        Assert.Contains(enemies, e => e.Weapon.Type == WeaponType.Chain && e.SplitOnDeath is { Count: > 0 });
        Assert.Contains(enemies, e => e.Weapon.HasMarkerSpit && e.SplitOnDeath is { Count: > 0 });
    }

    [Theory]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    public void EnemySpecialFloors_PlayToCompletion_WithoutThrowing(int floor)
    {
        Content c = Content.Load();
        SimConfig cfg = SimConfigBuilder.Build(c.Balance);
        var controller = new BattleController(
            BattleSetup.Build(c, floor, BattleSetup.DefaultTeam(), seed: (uint)(1000 + floor)), cfg);

        for (int frame = 0; frame < 120_000 && controller.Phase != Phase.Ended; frame++)
        {
            if (controller.IsAwaitingHeroInput())
            {
                Actor self = controller.World.ActiveActor();
                Actor? target = null;
                double best = double.PositiveInfinity;
                foreach (Actor a in controller.World.Actors)
                {
                    if (a.Team == self.Team || !a.IsAlive)
                    {
                        continue;
                    }
                    double d = (a.Pos - self.Pos).LengthSquared();
                    if (d < best)
                    {
                        best = d;
                        target = a;
                    }
                }
                if (target is not null)
                {
                    Vec2 toward = (target.Pos - self.Pos).Normalized();
                    controller.BeginAim(self.Pos);
                    controller.Release(self.Pos - toward * controller.MaxDrag);
                }
            }
            controller.Advance(1.0 / 60.0);
        }

        Assert.Equal(Phase.Ended, controller.Phase);

        // Prompt 12 — floors 6-15's real balance pass. Reaching Ended alone
        // only proves the content doesn't crash the sim; a floor that
        // reliably beats the reference kit (this loadout, the same one
        // Prompt 7 used to verify floor 1) would still pass that check while
        // being unplayable. Winning is the actual bar.
        Assert.Equal(Team.Hero, controller.Winner);
    }
}

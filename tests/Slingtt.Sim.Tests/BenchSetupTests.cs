using Slingtt.Game;
using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 4's content -> BattleSetup wiring: which roster slot starts benched,
// and that a shorter team (nobody to bench) doesn't try to bench anyone.
public class BenchSetupTests
{
    [Fact]
    public void DefaultTeam_StartsWithTheThirdHeroBenched()
    {
        Content c = Content.Load();
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 1, BattleSetup.DefaultTeam(), seed: 1);

        List<ActorInit> heroes = setup.Actors.Where(a => a.Team == Team.Hero).ToList();
        Assert.Equal(3, heroes.Count);
        Assert.False(heroes[0].IsBenched);
        Assert.False(heroes[1].IsBenched);
        Assert.True(heroes[2].IsBenched);
    }

    [Fact]
    public void TeamOfTwo_HasNobodyBenched()
    {
        Content c = Content.Load();
        List<LoadoutSlot> team = BattleSetup.DefaultTeam().Take(2).ToList();
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 1, team, seed: 1);

        Assert.All(setup.Actors.Where(a => a.Team == Team.Hero), h => Assert.False(h.IsBenched));
    }

    [Fact]
    public void BenchedHero_StillHasAValidInArenaPosition()
    {
        Content c = Content.Load();
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 1, BattleSetup.DefaultTeam(), seed: 1);

        ActorInit benched = setup.Actors.First(a => a.Team == Team.Hero && a.IsBenched);
        Assert.InRange(benched.Pos.X, 0, setup.BoundsW);
        Assert.InRange(benched.Pos.Y, 0, setup.BoundsH);
    }

    [Fact]
    public void BenchedHero_AndActiveHero_DoNotShareATile()
    {
        // The turn-order strip and the render layer both key positions off this;
        // an overlap reads as one actor sitting on top of another.
        Content c = Content.Load();
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 1, BattleSetup.DefaultTeam(), seed: 1);
        List<ActorInit> heroes = setup.Actors.Where(a => a.Team == Team.Hero).ToList();

        ActorInit benched = heroes.Single(h => h.IsBenched);
        foreach (ActorInit active in heroes.Where(h => !h.IsBenched))
        {
            double dist = Math.Sqrt(Math.Pow(active.Pos.X - benched.Pos.X, 2) + Math.Pow(active.Pos.Y - benched.Pos.Y, 2));
            Assert.True(dist > 1.0, $"benched hero at {benched.Pos.X},{benched.Pos.Y} overlaps active hero at {active.Pos.X},{active.Pos.Y}");
        }
    }

    [Fact]
    public void Upcoming_NeverListsTheBenchedHero()
    {
        Content c = Content.Load();
        WorldSetup setup = BattleSetup.Build(c, floorNumber: 1, BattleSetup.DefaultTeam(), seed: 1);
        var controller = new BattleController(setup, SimConfigBuilder.Build(c.Balance));

        string benchedId = setup.Actors.First(a => a.Team == Team.Hero && a.IsBenched).Id;

        for (int frame = 0; frame < 20_000 && controller.Phase != Phase.Ended; frame++)
        {
            Assert.DoesNotContain(benchedId, controller.Upcoming(5));

            if (controller.IsAwaitingHeroInput())
            {
                Actor self = controller.World.ActiveActor();
                controller.BeginAim(self.Pos);
                controller.Release(new Vec2(self.Pos.X, self.Pos.Y + 3.0));
            }
            controller.Advance(1.0 / 60.0);
        }
    }
}

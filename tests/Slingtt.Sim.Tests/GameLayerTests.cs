using Slingtt.Game;
using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Covers the content -> setup -> controller path that the Godot layer depends on.
// If content fails to load or a floor can't be built, the app has nothing to draw —
// which is exactly the failure mode this port exists to eliminate.
public class GameLayerTests
{
    private sealed class MemoryStore : ISaveStore
    {
        private string? _data;
        public string? Load() => _data;
        public void Save(string json) => _data = json;
    }

    [Fact]
    public void Content_LoadsEveryEmbeddedCatalog()
    {
        Content c = Content.Load();

        Assert.NotEmpty(c.Heroes);
        Assert.NotEmpty(c.Weapons);
        Assert.NotEmpty(c.Armor);
        Assert.NotEmpty(c.Enemies);
        Assert.NotEmpty(c.Ultimates);
        Assert.NotEmpty(c.Floors);
        Assert.Equal(9, c.Balance.Arena.W);
        Assert.Equal(16, c.Balance.Arena.H);
        Assert.Equal(120, c.Balance.Sim.TickRate);
        Assert.Equal("Bram", c.Name("hero_bram.name"));
    }

    [Fact]
    public void EveryFloor_BuildsAValidBattle()
    {
        Content c = Content.Load();
        List<LoadoutSlot> team = BattleSetup.DefaultTeam();

        for (int floor = 1; floor <= c.MaxFloor; floor++)
        {
            WorldSetup setup = BattleSetup.Build(c, floor, team, seed: 42);
            Assert.Equal(3, setup.Actors.Count(a => a.Team == Team.Hero));
            Assert.True(setup.Actors.Any(a => a.Team == Team.Enemy), $"floor {floor} has no enemies");
            foreach (ActorInit a in setup.Actors)
            {
                Assert.True(a.Hp > 0, $"floor {floor}: {a.Id} spawned with no HP");
                Assert.InRange(a.Pos.X, 0, setup.BoundsW);
                Assert.InRange(a.Pos.Y, 0, setup.BoundsH);
            }
        }
    }

    [Fact]
    public void DefaultTeam_UnlocksTierOneUltimates()
    {
        Content c = Content.Load();
        WorldSetup setup = BattleSetup.Build(c, 1, BattleSetup.DefaultTeam(), seed: 1);

        foreach (ActorInit hero in setup.Actors.Where(a => a.Team == Team.Hero))
        {
            Assert.True(hero.Weapon.Ultimate.HasValue, $"{hero.Id} should have a weapon ultimate at level 10");
            Assert.NotNull(hero.Armor);
            Assert.True(hero.Armor!.Ultimate.HasValue, $"{hero.Id} should have an armor ultimate at level 10");
        }
    }

    [Fact]
    public void Controller_PlaysFloorOneToCompletion()
    {
        Content c = Content.Load();
        SimConfig cfg = SimConfigBuilder.Build(c.Balance);
        var controller = new BattleController(
            BattleSetup.Build(c, 1, BattleSetup.DefaultTeam(), seed: 777), cfg);

        // Drive it like a player who always fires straight at the enemy side.
        for (int frame = 0; frame < 60_000 && controller.Phase != Phase.Ended; frame++)
        {
            if (controller.IsAwaitingHeroInput())
            {
                Actor self = controller.World.ActiveActor();
                controller.BeginAim(self.Pos);
                controller.Release(new Vec2(self.Pos.X, self.Pos.Y + 3.0)); // drag back = fire forward
            }
            controller.Advance(1.0 / 60.0);
        }

        Assert.Equal(Phase.Ended, controller.Phase);
        Assert.NotNull(controller.Winner);
    }

    [Fact]
    public void RenderActors_StayInsideTheArena()
    {
        Content c = Content.Load();
        SimConfig cfg = SimConfigBuilder.Build(c.Balance);
        var controller = new BattleController(
            BattleSetup.Build(c, 2, BattleSetup.DefaultTeam(), seed: 99), cfg);

        for (int frame = 0; frame < 20_000 && controller.Phase != Phase.Ended; frame++)
        {
            if (controller.IsAwaitingHeroInput())
            {
                Actor self = controller.World.ActiveActor();
                controller.BeginAim(self.Pos);
                controller.Release(new Vec2(self.Pos.X + 1.5, self.Pos.Y + 2.5));
            }
            controller.Advance(1.0 / 60.0);

            foreach (RenderActor ra in controller.GetRenderActors())
            {
                Assert.InRange(ra.X, -0.5, controller.ArenaW + 0.5);
                Assert.InRange(ra.Y, -0.5, controller.ArenaH + 0.5);
            }
        }
    }

    [Fact]
    public void RunState_GrantsFloorRewardsExactlyOnce()
    {
        Content c = Content.Load();
        var run = new RunState(c, new MemoryStore());
        var heroes = new List<PostBattleHero>
        {
            new() { HeroId = "hero_bram", Name = "Bram", Hp = 500, MaxHp = 1000 },
        };

        FloorResult first = run.ResolveFloorClear(heroes, turnsUsed: 4);
        int coresAfterFirst = run.BalanceOf(ResourceKind.SlingCores);
        Assert.True(coresAfterFirst > 0);

        // Re-resolving the same floor (a crash mid-results, a double tap) must not pay twice.
        run.ResolveFloorClear(heroes, turnsUsed: 4);
        Assert.Equal(coresAfterFirst, run.BalanceOf(ResourceKind.SlingCores));
        Assert.Equal(1, first.FloorNumber);
    }

    [Fact]
    public void RunState_CarriesHealedHpToTheNextFloor()
    {
        Content c = Content.Load();
        var run = new RunState(c, new MemoryStore());
        string heroId = run.Team[0].HeroId;

        run.ResolveFloorClear(new List<PostBattleHero>
        {
            new() { HeroId = heroId, Name = "x", Hp = 400, MaxHp = 1000 },
        }, turnsUsed: 3);
        run.ContinueToNextFloor();

        Assert.Equal(2, run.CurrentFloor);
        Assert.NotNull(run.Team[0].CurrentHp);
        Assert.True(run.Team[0].CurrentHp > 400, "surviving heroes heal on floor clear");
    }

    [Fact]
    public void RunState_SurvivesAReload()
    {
        Content c = Content.Load();
        var store = new MemoryStore();
        var run = new RunState(c, store);
        run.ResolveFloorClear(new List<PostBattleHero>
        {
            new() { HeroId = run.Team[0].HeroId, Name = "x", Hp = 400, MaxHp = 1000 },
        }, turnsUsed: 3);
        run.ContinueToNextFloor();

        var reloaded = new RunState(c, store);

        Assert.Equal(2, reloaded.CurrentFloor);
        Assert.Equal(run.BalanceOf(ResourceKind.SlingCores), reloaded.BalanceOf(ResourceKind.SlingCores));
        Assert.Equal(1, reloaded.HighestFloorCleared);
    }

    [Fact]
    public void CorruptSave_FallsBackToAFreshRunInsteadOfThrowing()
    {
        Content c = Content.Load();
        var store = new MemoryStore();
        store.Save("{ this is not json");

        var run = new RunState(c, store);

        Assert.Equal(1, run.CurrentFloor);
        Assert.Equal(3, run.Team.Count);
    }
}

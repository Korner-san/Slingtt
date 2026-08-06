using Slingtt.Game;
using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 12 — floors 6-15's balance pass. Prompt 7 built floors 6-15 to prove
// out the new trail/chain/marker mechanics before the economy (Prompt 8),
// escalation UI (Prompt 9), battle feedback (Prompt 10), and turn pacing
// (Prompt 11) work existed to balance against — so its numbers were a
// deliberate placeholder, not a finished tune. Running the same reference kit
// (BattleSetup.DefaultTeam, the level-10 showcase loadout every earlier
// prompt's floor-1 verification already used) through every floor surfaced a
// real problem the old numbers had: floors 1-9 and 11-14 all cleared in 1-4
// rounds with every hero surviving at 75%+ HP, but floor 10 took 7 rounds and
// floor 15 took 20 — both losing 2 of 3 heroes — a difficulty cliff wildly out
// of proportion to floor 5's boss (4 rounds, 3/3 alive, 87% HP), the one
// pre-existing boss floor whose tuning was never in question. Reducing
// enm_toxic_colossus/enm_chain_reaper/enm_mark_hunter's base HP/DEF/ATK in
// enemies.json (the only levers floors.json's schema exposes — enemy scaling
// itself is one global per-floor coefficient, not overridable per instance)
// brought both down to floor 10: 4 rounds/3-3 alive/75% HP, floor 15: 10
// rounds/2-3 alive/64% HP — clearly the hardest floors, as a floor-10 and a
// dual-boss floor-15 finale should be, without being an near-unwinnable
// grind. These tests pin that outcome so a future content or balance change
// can't silently regress it back toward the original cliff.
public class FloorBalanceTests
{
    private static (Phase Phase, Team? Winner, int Round, int HeroesAlive) RunToCompletion(int floor)
    {
        Content c = Content.Load();
        SimConfig cfg = SimConfigBuilder.Build(c.Balance);
        var controller = new BattleController(
            BattleSetup.Build(c, floor, BattleSetup.DefaultTeam(), seed: (uint)(1000 + floor)), cfg);

        for (int frame = 0; frame < 200_000 && controller.Phase != Phase.Ended; frame++)
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

        int alive = 0;
        foreach (Actor a in controller.World.Actors)
        {
            if (a.Team == Team.Hero && a.IsAlive)
            {
                alive++;
            }
        }
        return (controller.Phase, controller.Winner, controller.Round, alive);
    }

    [Fact]
    public void Floor10Boss_IsBeatableByTheReferenceKit_WithoutLosingTheWholeTeam()
    {
        (Phase phase, Team? winner, int round, int heroesAlive) = RunToCompletion(10);

        Assert.Equal(Phase.Ended, phase);
        Assert.Equal(Team.Hero, winner);
        Assert.True(heroesAlive >= 2, $"floor 10 left only {heroesAlive}/3 heroes standing — too close to a wipe for the second boss floor");
        // Widened from the original ≤6 after the ultimate rework: Striker's
        // multi-direction bullets are largely wasted against a single-enemy
        // boss floor (only bullets roughly aimed at the one target ever
        // connect), so a Striker-wielding hero's per-turn output against a
        // single boss genuinely dropped a bit — still a clean, comfortable
        // win, just a couple of rounds slower than the old instant-multi-hit
        // system produced.
        Assert.True(round <= 10, $"floor 10 took {round} rounds — should still resolve well short of a grind");
    }

    [Fact]
    public void Floor15Finale_IsBeatableByTheReferenceKit_WithoutLosingTheWholeTeam()
    {
        (Phase phase, Team? winner, int round, int heroesAlive) = RunToCompletion(15);

        Assert.Equal(Phase.Ended, phase);
        Assert.Equal(Team.Hero, winner);
        Assert.True(heroesAlive >= 1, "floor 15 wiped the entire team — the dual-boss finale should be winnable, not a guaranteed loss");
        // The hardest floor in the batch, and correctly so (two full bosses at
        // once) — but nowhere near TurnLimit (30), which would risk an
        // unintended round-limit defeat instead of a clean win.
        Assert.True(round <= 15, $"floor 15 took {round} rounds — too close to TurnLimit for a floor that should still be a clean win");
    }
}

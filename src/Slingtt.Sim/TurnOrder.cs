namespace Slingtt.Sim;

/// <summary>Advances the world to the next actor's turn (setting ActiveActorId +
/// Phase.Aiming), or ends the battle on the turn limit. The round-robin resolver
/// ships; an initiative-accumulator model would be a second method here, swapped
/// at the one call site in BattleSim, without touching combat code.</summary>
public static class TurnOrder
{
    public static void AdvanceRoundRobin(World world, SimConfig cfg)
    {
        int n = world.Actors.Count;
        for (int guard = 0; guard < n * (cfg.TurnLimit + 2); guard++)
        {
            world.TurnCursor += 1;
            if (world.TurnCursor >= n)
            {
                world.TurnCursor = 0;
            }

            if (world.TurnCursor == 0)
            {
                world.Round += 1;
                if (world.Round > cfg.TurnLimit)
                {
                    world.Phase = Phase.Ended;
                    world.Winner = Team.Enemy;
                    world.ActiveActorId = null;
                    world.Events.Add(new SimEvent
                    {
                        Kind = SimEventKind.BattleEnd,
                        Winner = Team.Enemy,
                        Reason = EndReason.TurnLimit,
                    });
                    return;
                }
                world.Events.Add(new SimEvent { Kind = SimEventKind.RoundStart, Round = world.Round });
            }

            Actor actor = world.Actors[world.TurnCursor];
            if (!actor.IsAlive)
            {
                continue;
            }

            if (actor.IsBenched)
            {
                // A benched hero the player never swapped back in still has to
                // fight if both active heroes went down — otherwise the turn
                // order would skip it forever (it never volunteers for a turn on
                // its own) while the player has no one left to act with. Forcing
                // it active here has no arrival-ultimate perk: that's reserved
                // for a deliberate BattleSim.TrySwap, not this fallback.
                if (!HasLivingActiveHero(world, actor.Team))
                {
                    actor.IsBenched = false;
                }
                else
                {
                    continue;
                }
            }

            RegenBenchedHeroes(world, cfg);

            if (actor.StunnedTurns > 0)
            {
                actor.StunnedTurns -= 1; // stunned actors lose the turn
                continue;
            }

            world.ActiveActorId = actor.Id;
            world.Phase = Phase.Aiming;
            world.Events.Add(new SimEvent { Kind = SimEventKind.TurnStart, ActorId = actor.Id });
            return;
        }
        throw new InvalidOperationException("sim: turn order failed to find a living actor");
    }

    private static bool HasLivingActiveHero(World world, Team team)
    {
        foreach (Actor a in world.Actors)
        {
            if (a.Team == team && a.IsAlive && !a.IsBenched)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>"Regenerating HP per turn": every turn actually granted (hero or
    /// enemy), any living benched hero heals a fraction of its max HP.</summary>
    private static void RegenBenchedHeroes(World world, SimConfig cfg)
    {
        foreach (Actor a in world.Actors)
        {
            if (a.Team != Team.Hero || !a.IsAlive || !a.IsBenched)
            {
                continue;
            }
            double amount = SimMath.RoundJs(a.MaxHp * cfg.BenchRegenPctPerTurn);
            if (amount <= 0)
            {
                continue;
            }
            double healed = Math.Min(amount, a.MaxHp - a.Hp);
            if (healed <= 0)
            {
                continue;
            }
            a.Hp += healed;
            world.Events.Add(new SimEvent { Kind = SimEventKind.Heal, ActorId = a.Id, Amount = healed, Pos = a.Pos });
        }
    }
}

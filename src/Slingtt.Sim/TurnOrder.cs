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
}

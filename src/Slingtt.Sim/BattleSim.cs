namespace Slingtt.Sim;

// The sim contract: Step(world, config) mutates in place and appends events;
// TryLaunch(...) returns false on rejection. Nothing here touches the engine.
public static class BattleSim
{
    /// <summary>Launch the active actor. Returns false (and consumes nothing) when
    /// the draw is below the cancel threshold — a sub-0.25 release is a cancel,
    /// not a weak shot.</summary>
    public static bool TryLaunch(World world, SimConfig cfg, LaunchInput input)
    {
        if (world.Phase != Phase.Aiming)
        {
            return false;
        }
        if (input.DrawRatio < cfg.MinDrawRatio)
        {
            return false;
        }

        Actor self = world.ActiveActor();
        double len = Math.Sqrt(input.DirX * input.DirX + input.DirY * input.DirY);
        if (len == 0)
        {
            len = 1;
        }
        double dirX = input.DirX / len;
        double dirY = input.DirY / len;
        double drawRatio = Math.Min(input.DrawRatio, 1);
        double speed = cfg.MaxSpeed * drawRatio;

        self.Vel = new Vec2(dirX * speed, dirY * speed);
        self.LastTravelDir = new Vec2(dirX, dirY);
        self.TravelTicksRemaining = SimMath.RoundJsInt(self.MoveDurationTicks * self.MoveMultNextTurn);
        self.MoveMultNextTurn = 1;
        self.TravelTickCount = 0;
        self.HitsThisTravel = 0;
        self.PiercedIds.Clear();
        self.PierceBudgetUsed = 0;
        self.ComboFiredThisTravel = false;

        world.Phase = Phase.Travelling;
        world.Events.Add(new SimEvent
        {
            Kind = SimEventKind.Launch,
            ActorId = self.Id,
            Dir = new Vec2(dirX, dirY),
            Amount = drawRatio,
            Pos = self.Pos,
        });
        return true;
    }

    /// <summary>Prompt 4 — swap the active hero out for the benched teammate
    /// instead of launching. Consumes the whole turn: the incoming hero enters at
    /// the outgoing hero's tile and immediately fires their weapon ultimate from
    /// there (a full, unfiltered activation — arrival is a reward for swapping,
    /// not a weaker echo of it), then the turn passes on exactly as if a launch
    /// had resolved. Returns false (no state change) when there's no living
    /// benched teammate to swap in.</summary>
    public static bool TrySwap(World world, SimConfig cfg)
    {
        if (world.Phase != Phase.Aiming)
        {
            return false;
        }

        Actor outgoing = world.ActiveActor();
        if (outgoing.Team != Team.Hero)
        {
            return false;
        }

        Actor? incoming = null;
        foreach (Actor a in world.Actors)
        {
            if (a.Team == Team.Hero && a.IsBenched && a.IsAlive)
            {
                incoming = a;
                break;
            }
        }
        if (incoming is null)
        {
            return false;
        }

        incoming.Pos = outgoing.Pos;
        incoming.IsBenched = false;
        outgoing.IsBenched = true;

        world.Events.Add(new SimEvent
        {
            Kind = SimEventKind.Swap,
            ActorId = outgoing.Id,
            TargetId = incoming.Id,
            Pos = incoming.Pos,
        });

        Ultimates.FireWeaponUltimate(world, cfg, incoming);

        world.Phase = Phase.Settling;
        world.Tick += 1;
        return true;
    }

    /// <summary>Advance the simulation one fixed tick. In Aiming with a hero active
    /// this is a no-op (the sim waits for player input); enemy turns resolve their
    /// own launch from the world RNG.</summary>
    public static void Step(World world, SimConfig cfg)
    {
        switch (world.Phase)
        {
            case Phase.Ended:
                return;

            case Phase.Aiming:
            {
                Actor self = world.ActiveActor();
                if (self.Team == Team.Enemy)
                {
                    TryLaunch(world, cfg, EnemyAi.DecideLaunch(world, self));
                    world.Tick += 1;
                }
                return; // hero: wait for input, no tick consumed
            }

            case Phase.Travelling:
            {
                Actor self = world.ActiveActor();
                double dt = cfg.Dt;

                self.TravelTicksRemaining -= 1;
                self.TravelTickCount += 1;
                Collision.MaintainPierceMarks(world, self);

                bool stoppedByWeapon = Collision.IntegrateTravel(world, cfg, self, dt);

                // Prompt 5 — teammate contact is checked independently of the
                // opposing-team collision Collision.IntegrateTravel resolves
                // above; touching your other active hero never deflects or
                // stops the shot, it only procs the combo.
                if (self.Team == Team.Hero)
                {
                    Combo.CheckContact(world, cfg, self);
                }

                // Friction after integration, not before.
                self.Vel *= cfg.FrictionPerTick;

                double speed = self.Vel.Length();
                double capTicks = cfg.HardCapSeconds * cfg.TickRate;
                if (stoppedByWeapon
                    || speed < cfg.MinSpeed
                    || self.TravelTicksRemaining <= 0
                    || self.TravelTickCount >= capTicks)
                {
                    self.Vel = Vec2.Zero;
                    world.Events.Add(new SimEvent
                    {
                        Kind = SimEventKind.Stopped,
                        ActorId = self.Id,
                        Pos = self.Pos,
                    });
                    world.Phase = Phase.Ultimate;
                }
                world.Tick += 1;
                return;
            }

            case Phase.Ultimate:
                Ultimates.FireWeaponUltimate(world, cfg, world.ActiveActor());
                world.Phase = Phase.Settling;
                world.Tick += 1;
                return;

            case Phase.Settling:
                if (world.LivingCount(Team.Enemy) == 0)
                {
                    world.Phase = Phase.Ended;
                    world.Winner = Team.Hero;
                    world.ActiveActorId = null;
                    world.Events.Add(new SimEvent
                    {
                        Kind = SimEventKind.BattleEnd,
                        Winner = Team.Hero,
                        Reason = EndReason.Elimination,
                    });
                }
                else if (world.LivingCount(Team.Hero) == 0)
                {
                    world.Phase = Phase.Ended;
                    world.Winner = Team.Enemy;
                    world.ActiveActorId = null;
                    world.Events.Add(new SimEvent
                    {
                        Kind = SimEventKind.BattleEnd,
                        Winner = Team.Enemy,
                        Reason = EndReason.Elimination,
                    });
                }
                else
                {
                    TurnOrder.AdvanceRoundRobin(world, cfg);
                }
                world.Tick += 1;
                return;
        }
    }
}

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
        self.DistanceTravelledThisTravel = 0;
        self.TrailDropCooldownTicks = 0; // Prompt 7 — first travel tick always drops a hazard point

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

        // Not straight to Settling: the arrival ultimate can spawn real
        // projectiles (Projectiles.cs) that need further ticks to resolve,
        // same as the normal Ultimate-phase firing does.
        world.Phase = Phase.UltimateTravel;
        world.Tick += 1;
        return true;
    }

    /// <summary>Advance the simulation one fixed tick. In Aiming with a hero active
    /// this is a no-op (the sim waits for player input); enemy turns resolve their
    /// own launch from the world RNG.</summary>
    public static void Step(World world, SimConfig cfg)
    {
        if (world.Phase == Phase.Ended)
        {
            return;
        }

        // In-flight ultimate projectiles advance every tick regardless of the
        // dominant phase, not just Phase.UltimateTravel: Prompt 5's contact
        // combo can fire a teammate's weapon ultimate mid-flight while the
        // ACTIVE actor is still Phase.Travelling, and those projectiles need
        // to keep flying independently of whatever the active actor is doing.
        // Phase.UltimateTravel's own case below only cares about *waiting*
        // for the list to empty before handing the turn off.
        Projectiles.Advance(world, cfg);

        switch (world.Phase)
        {
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

                Vec2 posBeforeIntegrate = self.Pos;
                bool stoppedByWeapon = Collision.IntegrateTravel(world, cfg, self, dt);

                // Prompt 6 — Carry's distance tracking: net displacement this
                // tick, summed tick over tick across the whole travel. Ticks are
                // ~0.22 units of movement at MaxSpeed, so a same-tick wall bounce
                // undercounting its true sub-step path length is negligible.
                self.DistanceTravelledThisTravel += (self.Pos - posBeforeIntegrate).Length();

                // Prompt 5 — teammate contact is checked independently of the
                // opposing-team collision Collision.IntegrateTravel resolves
                // above; touching your other active hero never deflects or
                // stops the shot, it only procs the combo.
                if (self.Team == Team.Hero)
                {
                    Combo.CheckContact(world, cfg, self);
                }

                // Prompt 7 — trail hazards: a Trail-type actor drops a hazard
                // point periodically along its own path; whoever is currently
                // travelling (either side) can walk into one laid by the other
                // team. Both are independent of the opposing-team collision
                // above — a hazard never deflects or stops anything.
                Hazards.MaybeDropTrail(world, cfg, self);
                Hazards.CheckContact(world, cfg, self);

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
            {
                Actor self = world.ActiveActor();
                Ultimates.FireWeaponUltimate(world, cfg, self);
                MarkerSpit.FireIfApplicable(world, cfg, self); // Prompt 7 — enemies never have Weapon.Ultimate, so this never doubles up with the line above
                world.Phase = Phase.UltimateTravel;
                world.Tick += 1;
                return;
            }

            case Phase.UltimateTravel:
                // Projectiles.Advance already ran unconditionally above, for
                // this tick — just check whether that emptied the list.
                if (world.Projectiles.Count == 0)
                {
                    world.Phase = Phase.Settling;
                }
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

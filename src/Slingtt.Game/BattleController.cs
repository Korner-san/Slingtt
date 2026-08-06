using Slingtt.Sim;

namespace Slingtt.Game;

/// <summary>Interpolated per-actor state for the render layer.</summary>
public sealed class RenderActor
{
    public string Id = "";

    /// <summary>See Actor.ContentId — non-empty only for actors the render
    /// layer might not already have a pre-built visual for (split-on-death
    /// children), so it can build one lazily by looking this id up.</summary>
    public string ContentId = "";

    public Team Team;
    public WeaponType WeaponType;
    public int Tier;
    public double Radius;
    public double X; // interpolated sim position
    public double Y;
    public double Speed;
    public double Hp;
    public double MaxHp;
    public double ShieldHp;
    public bool ShieldActive;
    public bool Alive;
    public bool Active;
    public bool Benched;

    /// <summary>Prompt 10 — battle feedback layer's "mark visual": true while
    /// MarkedTurns > 0 (Prompt 7's Marker enemies).</summary>
    public bool Marked;
}

/// <summary>Live-iteration rework — a weapon-ultimate projectile in flight
/// (bullet/grenade/boomerang). Read directly from World.Projectiles, no
/// interpolation: projectiles travel slowly and the sim ticks at 120Hz, so
/// per-frame steps stay small enough that this looks smooth without the
/// Prev/Curr interpolation RenderActor uses — an accepted v1 simplification.</summary>
public sealed class RenderProjectile
{
    public string Id = "";
    public ProjectileKind Kind;
    public double X;
    public double Y;
    public double DirX;
    public double DirY;
}

public sealed class AimState
{
    public Vec2 Self;       // sim position of the active hero
    public double DirX;     // normalized launch direction (opposite the drag)
    public double DirY;
    public double DrawRatio;
    public bool Legal;      // false below the cancel threshold
    public Prediction? Prediction; // sim-space points/bounces
}

// Framework-free owner of a running battle: no engine types here. The render
// layer drives Advance() from its frame callback and reads interpolated state.
// This is the single loop implementation, so there is no drift between the
// gameplay the player sees and the simulation that decides it.
public sealed class BattleController
{
    private sealed class Interp
    {
        public double Px, Py, Cx, Cy;
    }

    public World World { get; private set; }

    private readonly SimConfig _cfg;
    private readonly FeelConfig _feel;
    private readonly double _dt;
    private readonly double _maxDrag;

    private double _accumulator;
    private readonly Dictionary<string, Interp> _interp = new();
    private readonly List<SimEvent> _pending = new();
    private double _hitStopRemaining;

    public double Alpha { get; private set; }
    public double ShakeTrauma { get; private set; }
    public double CameraPunch { get; private set; }

    private double _aimStartX, _aimStartY;
    private bool _aiming;
    private AimState? _aimState;

    public BattleController(WorldSetup setup, SimConfig cfg, FeelConfig? feel = null, double? maxDrag = null)
    {
        _cfg = cfg;
        _dt = cfg.Dt;
        _feel = feel ?? FeelConfig.Default;
        _maxDrag = maxDrag ?? cfg.MaxDrag;
        World = World.Create(setup);
        ResetInterp();
    }

    public void Restart(WorldSetup setup)
    {
        World = World.Create(setup);
        _accumulator = 0;
        Alpha = 0;
        _hitStopRemaining = 0;
        ShakeTrauma = 0;
        CameraPunch = 0;
        _pending.Clear();
        _aiming = false;
        _aimState = null;
        ResetInterp();
    }

    private void ResetInterp()
    {
        _interp.Clear();
        foreach (Actor a in World.Actors)
        {
            _interp[a.Id] = new Interp { Px = a.Pos.X, Py = a.Pos.Y, Cx = a.Pos.X, Cy = a.Pos.Y };
        }
    }

    private void ShiftInterp()
    {
        foreach (Actor a in World.Actors)
        {
            if (!_interp.TryGetValue(a.Id, out Interp? r))
            {
                r = new Interp { Px = a.Pos.X, Py = a.Pos.Y, Cx = a.Pos.X, Cy = a.Pos.Y };
                _interp[a.Id] = r;
            }
            r.Px = r.Cx;
            r.Py = r.Cy;
        }
    }

    private void CaptureInterp()
    {
        foreach (Actor a in World.Actors)
        {
            if (_interp.TryGetValue(a.Id, out Interp? r))
            {
                r.Cx = a.Pos.X;
                r.Cy = a.Pos.Y;
            }
        }
    }

    // --- loop ---------------------------------------------------------------

    /// <summary>Advance real time. Steps the sim a fixed number of ticks, applies
    /// hit-stop by NOT consuming ticks (never by changing dt), and decays feel
    /// state.</summary>
    public void Advance(double realDt)
    {
        // Feel decays on wall-clock time even while the sim is frozen.
        ShakeTrauma = Math.Max(0, ShakeTrauma - realDt / _feel.ShakeDecaySeconds);
        CameraPunch = Math.Max(0, CameraPunch - realDt / _feel.PunchDecaySeconds);

        if (_hitStopRemaining > 0)
        {
            _hitStopRemaining -= realDt;
            return; // frozen mid-impact; hold the interpolated frame
        }
        if (World.Phase == Phase.Ended || IsAwaitingHeroInput())
        {
            _accumulator = 0;
            return;
        }

        _accumulator += Math.Min(realDt, _feel.MaxCatchupSeconds);
        int steps = 0;
        while (_accumulator >= _dt && steps < _feel.MaxStepsPerFrame)
        {
            ShiftInterp();
            BattleSim.Step(World, _cfg);
            CaptureInterp();
            _accumulator -= _dt;
            steps += 1;
            ProcessEvents();
            if (_hitStopRemaining > 0)
            {
                break;
            }
            if (World.Phase == Phase.Ended)
            {
                break;
            }
            if (IsAwaitingHeroInput())
            {
                _accumulator = 0;
                break;
            }
        }
        Alpha = SimMath.Clamp(_accumulator / _dt, 0, 1);
    }

    private void ProcessEvents()
    {
        foreach (SimEvent ev in World.Events)
        {
            _pending.Add(ev);
            if (ev.Kind == SimEventKind.Hit && ev.Amount > 0)
            {
                double norm = Math.Min(ev.Amount / _feel.HitStopRefDamage, 1);
                double ms = _feel.HitStopMinMs + (_feel.HitStopMaxMs - _feel.HitStopMinMs) * norm;
                _hitStopRemaining = Math.Max(_hitStopRemaining, ms / 1000.0);
                double burst = ev.HitKind == HitKind.Aoe || ev.HitKind == HitKind.Ultimate
                    ? _feel.ShakeBurstMultiplier
                    : 1;
                ShakeTrauma = Math.Min(_feel.ShakeMaxTrauma, ShakeTrauma + _feel.ShakePerHit * norm * burst);
            }
            else if (ev.Kind == SimEventKind.WeaponUltimate || ev.Kind == SimEventKind.ArmorUltimate)
            {
                CameraPunch = Math.Min(1, CameraPunch + _feel.PunchPerUltimate);
            }
        }
        World.Events.Clear();
    }

    /// <summary>Render/audio drain queued sim events each frame (VFX, damage
    /// numbers, sound).</summary>
    public List<SimEvent> DrainEvents()
    {
        if (_pending.Count == 0)
        {
            return EmptyEvents;
        }
        var outp = new List<SimEvent>(_pending);
        _pending.Clear();
        return outp;
    }

    private static readonly List<SimEvent> EmptyEvents = new();

    // --- render state -------------------------------------------------------

    public List<RenderActor> GetRenderActors()
    {
        var outp = new List<RenderActor>(World.Actors.Count);
        double a = Alpha;
        foreach (Actor actor in World.Actors)
        {
            double x = actor.Pos.X;
            double y = actor.Pos.Y;
            if (_interp.TryGetValue(actor.Id, out Interp? r))
            {
                x = r.Px + (r.Cx - r.Px) * a;
                y = r.Py + (r.Cy - r.Py) * a;
            }
            outp.Add(new RenderActor
            {
                Id = actor.Id,
                ContentId = actor.ContentId,
                Team = actor.Team,
                WeaponType = actor.Weapon.Type,
                Tier = actor.Weapon.Tier,
                Radius = actor.Radius,
                X = x,
                Y = y,
                Speed = actor.Vel.Length(),
                Hp = actor.Hp,
                MaxHp = actor.MaxHp,
                ShieldHp = actor.ShieldHp,
                ShieldActive = actor.ShieldHp > 0 && World.Round <= actor.ShieldExpiresRound,
                Alive = actor.IsAlive,
                Active = World.ActiveActorId == actor.Id,
                Benched = actor.IsBenched,
                Marked = actor.IsMarked,
            });
        }
        return outp;
    }

    public Phase Phase => World.Phase;
    public int Round => World.Round;
    public Team? Winner => World.Winner;
    public string? ActiveActorId => World.ActiveActorId;
    public double MaxDrag => _maxDrag;
    public double MinDrawRatio => _cfg.MinDrawRatio;
    public double ArenaW => World.BoundsW;
    public double ArenaH => World.BoundsH;

    /// <summary>Prompt 10 — battle feedback layer's "trail visual": the
    /// render layer syncs against this every frame the same way it syncs
    /// actor positions, rather than reacting to a one-shot spawn event (a
    /// hazard has no spawn event of its own to react to; it just appears in
    /// this list).</summary>
    public IReadOnlyList<Hazard> Hazards => World.Hazards;

    /// <summary>Live-iteration rework — the render layer syncs against this
    /// every frame the same way it syncs Hazards, rather than reacting to a
    /// one-shot spawn event: a projectile updates continuously while it's
    /// alive, so there's no single moment a spawn event alone would capture.</summary>
    public List<RenderProjectile> GetRenderProjectiles()
    {
        var outp = new List<RenderProjectile>(World.Projectiles.Count);
        foreach (UltimateProjectile p in World.Projectiles)
        {
            if (p.DelaySeconds > 0)
            {
                continue; // not yet active — mirrors the sim's own gate in Projectiles.Advance
            }
            Vec2 dir = p.Vel.Normalized();
            outp.Add(new RenderProjectile
            {
                Id = p.Id,
                Kind = p.Kind,
                X = p.Pos.X,
                Y = p.Pos.Y,
                DirX = dir.X,
                DirY = dir.Y,
            });
        }
        return outp;
    }

    public bool IsAwaitingHeroInput()
        => World.Phase == Phase.Aiming
           && World.ActiveActorId is not null
           && World.ActiveActor().Team == Team.Hero;

    /// <summary>Prompt 4 — true when the active hero could swap out for a living
    /// benched teammate right now (used to show/enable the swap button).</summary>
    public bool CanSwap()
    {
        if (!IsAwaitingHeroInput())
        {
            return false;
        }
        foreach (Actor a in World.Actors)
        {
            if (a.Team == Team.Hero && a.IsBenched && a.IsAlive)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>Upcoming actors for the turn-order strip (next `count`, active
    /// first).</summary>
    public List<string> Upcoming(int count)
    {
        var outp = new List<string>();
        if (World.Phase == Phase.Ended)
        {
            return outp;
        }
        int n = World.Actors.Count;
        if (n == 0)
        {
            return outp;
        }
        int cursor = World.TurnCursor;
        for (int i = 0; i < n * 3 && outp.Count < count; i++)
        {
            Actor a = World.Actors[((cursor % n) + n) % n];
            if (a.IsAlive && !a.IsBenched) // a benched hero never gets a turn until swapped in
            {
                if (outp.Count > 0 || a.Id == World.ActiveActorId || World.ActiveActorId is null)
                {
                    outp.Add(a.Id);
                }
            }
            cursor += 1;
        }
        return outp;
    }

    // --- input --------------------------------------------------------------

    public void BeginAim(Vec2 simPos)
    {
        if (!IsAwaitingHeroInput())
        {
            return;
        }
        _aiming = true;
        _aimStartX = simPos.X;
        _aimStartY = simPos.Y;
        _aimState = ComputeAim(simPos.X, simPos.Y, simPos.X, simPos.Y);
    }

    public void UpdateAim(Vec2 simPos)
    {
        if (!_aiming)
        {
            return;
        }
        _aimState = ComputeAim(_aimStartX, _aimStartY, simPos.X, simPos.Y);
    }

    public void CancelAim()
    {
        _aiming = false;
        _aimState = null;
    }

    /// <summary>Release the sling. Returns true if a launch actually happened — a
    /// sub-threshold draw is a cancel, not a weak shot.</summary>
    public bool Release(Vec2 simPos)
    {
        if (!_aiming)
        {
            return false;
        }
        _aiming = false;
        _aimState = null;
        double dx = _aimStartX - simPos.X;
        double dy = _aimStartY - simPos.Y;
        double drawRatio = Math.Min(Math.Sqrt(dx * dx + dy * dy) / _maxDrag, 1);
        return BattleSim.TryLaunch(World, _cfg, new LaunchInput { DirX = dx, DirY = dy, DrawRatio = drawRatio });
    }

    /// <summary>Prompt 4 — swap the active hero for the benched teammate instead
    /// of launching. Cancels any in-progress aim (a swap is a distinct choice,
    /// not a way to also fire). Returns true if the swap actually happened.</summary>
    public bool TrySwap()
    {
        CancelAim();
        return BattleSim.TrySwap(World, _cfg);
    }

    public AimState? GetAim() => _aimState;

    private AimState ComputeAim(double startX, double startY, double curX, double curY)
    {
        Actor self = World.ActiveActor();
        double dx = startX - curX;
        double dy = startY - curY;
        double dist = Math.Sqrt(dx * dx + dy * dy);
        double len = dist == 0 ? 1 : dist;
        double drawRatio = Math.Min(dist / _maxDrag, 1);
        bool legal = drawRatio >= _cfg.MinDrawRatio;
        Prediction? prediction = legal
            ? Predict.Trajectory(World, _cfg, new LaunchInput { DirX = dx, DirY = dy, DrawRatio = drawRatio },
                maxTicks: PreviewMaxTicks(self))
            : null;
        return new AimState
        {
            Self = self.Pos,
            DirX = dx / len,
            DirY = dy / len,
            DrawRatio = drawRatio,
            Legal = legal,
            Prediction = prediction,
        };
    }

    /// <summary>The aim preview always runs out to a flat fraction of the
    /// hero's own move-duration budget, never the full predicted travel,
    /// regardless of archetype — previously Focus (Balanced) extended this to
    /// 100%, but the preview is meant to show a taste of the shot, not its
    /// full resolution. Purely a UI truncation: Predict.Trajectory always runs
    /// the real sim, so this never changes what the eventual shot actually
    /// does, only how much of it the player gets to see in advance.</summary>
    private int PreviewMaxTicks(Actor self)
        => Math.Max(1, (int)(self.MoveDurationTicks * _cfg.PreviewFractionBase));
}

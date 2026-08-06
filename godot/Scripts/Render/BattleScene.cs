using System.Collections.Generic;
using Godot;
using Slingtt.Game;
using Slingtt.Sim;

namespace Slingtt.Render;

// The one node allowed to touch both Godot types and the sim/game layers. It
// converts sim Vec2 (double) to engine Vector3 (float) only here, at the render
// boundary, and it never makes a gameplay decision ג€” BattleController owns the
// loop and the simulation owns the rules.
public sealed partial class BattleScene : Node3D
{
    private const float ActorHeight = 0.5f; // the plane the sling is dragged on

    private GameRoot _game = null!;
    private BattleController _controller = null!;
    private Camera3D _camera = null!;
    private CameraFit _fit;
    private AimView _aim = null!;
    private VfxView _vfx = null!;
    private BattleHud _hud = null!;

    private readonly Dictionary<string, ActorView> _actorViews = new();
    private readonly Dictionary<string, ActorVisual> _visuals = new();

    private float _arenaW;
    private float _arenaH;
    private float _clock;
    private int _dragTouchIndex = -1;
    private Vec2 _dragSim;
    private bool _resolved; // battle end handled exactly once
    private Vector2I _lastViewport;
    private float _autoAimHoldElapsed = -1f; // -1 = not currently holding a `--holdaim` shot

    public override void _Ready()
    {
        _game = GameRoot.Instance;
        if (_game is null || _game.FatalError is not null)
        {
            GD.PushError("[Slingtt] battle entered without loaded content; returning to menu");
            GameRoot.Instance?.GoToMenu();
            return;
        }

        _arenaW = (float)_game.Content.Balance.Arena.W;
        _arenaH = (float)_game.Content.Balance.Arena.H;

        BuildEnvironment();
        BuildBattle();
    }

    // --- construction -------------------------------------------------------

    private void BuildEnvironment()
    {
        // An explicit WorldEnvironment. Without one, a 3D scene falls back to the
        // engine's default clear colour, which is the flat grey a player reads as
        // "the game didn't load".
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = Palette.ArenaFog,
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.42f, 0.40f, 0.55f),
            AmbientLightEnergy = 0.85f,
            FogEnabled = false,
        };
        AddChild(new WorldEnvironment { Environment = env, Name = "WorldEnvironment" });

        var key = new DirectionalLight3D
        {
            Name = "KeyLight",
            LightEnergy = 1.15f,
            LightColor = new Color(1f, 0.96f, 0.9f),
            ShadowEnabled = true,
        };
        key.RotationDegrees = new Vector3(-58, -34, 0);
        AddChild(key);

        var rim = new DirectionalLight3D
        {
            Name = "RimLight",
            LightEnergy = 0.35f,
            LightColor = Palette.TeamHero,
            ShadowEnabled = false,
        };
        rim.RotationDegrees = new Vector3(-20, 150, 0);
        AddChild(rim);

        _camera = new Camera3D { Name = "Camera3D", Current = true, Fov = CameraFitter.Fov, Far = 200f };
        AddChild(_camera);
        RefitCamera();
    }

    private void RefitCamera()
    {
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        float aspect = vp.Y > 0 ? vp.X / vp.Y : 0.5f;
        _fit = CameraFitter.Compute(_arenaW, _arenaH, aspect);
        _camera.Fov = _fit.Fov;
        _camera.GlobalPosition = _fit.Position;
        _camera.LookAt(_fit.Target, Vector3.Up);
        _lastViewport = (Vector2I)vp;
    }

    private void BuildBattle()
    {
        int floor = _game.Run.CurrentFloor;
        List<LoadoutSlot> team = _game.Run.Team;

        WorldSetup setup = BattleSetup.Build(_game.Content, floor, team, _game.Run.SeedForFloor(floor));
        _controller = new BattleController(setup, _game.SimConfig);

        AddChild(ArenaView.Build(_arenaW, _arenaH));

        _visuals.Clear();
        foreach (ActorVisual v in VisualRoster.Build(_game.Content, floor, team))
        {
            _visuals[v.Id] = v;
            ActorView view = ActorView.Create(v);
            _actorViews[v.Id] = view;
            AddChild(view);
        }

        _aim = AimView.Create(_arenaW, _arenaH);
        AddChild(_aim);

        _vfx = VfxView.Create(_arenaW, _arenaH);
        AddChild(_vfx);

        _hud = BattleHud.Create(_game.Content);
        _hud.QuitPressed += OnQuit;
        _hud.RestartPressed += OnRestart;
        _hud.ContinuePressed += OnContinue;
        _hud.SwapPressed += OnSwap;
        AddChild(_hud);

        FloorClassification cls = Rewards.Classify(floor, _game.Content.Balance.Progression);
        _hud.SetFloor(floor, cls.IsBoss);
        _resolved = false;

        SyncActors();
    }

    // --- loop ---------------------------------------------------------------

    public override void _Process(double delta)
    {
        if (_controller is null)
        {
            return;
        }
        _clock += (float)delta;

        if ((Vector2I)GetViewport().GetVisibleRect().Size != _lastViewport)
        {
            RefitCamera(); // rotation, split-screen, or a resized desktop window
        }

        if (DevCapture.AutoPlay && _controller.IsAwaitingHeroInput())
        {
            AutoFire((float)delta);
        }

        _controller.Advance(delta);
        DrainEvents();
        SyncActors();
        _vfx.SyncHazards(_controller.Hazards); // Prompt 10 — "trail visuals": no spawn event of its own, synced every frame

        CameraFitter.ApplyRig(_camera, _fit, (float)_controller.ShakeTrauma, (float)_controller.CameraPunch, _clock);
        _hud.UpdateBattle(_controller, _visuals);

        if (_controller.Phase == Phase.Ended && !_resolved)
        {
            ResolveBattleEnd();
        }
    }

    private void SyncActors()
    {
        foreach (RenderActor ra in _controller.GetRenderActors())
        {
            if (!_actorViews.TryGetValue(ra.Id, out ActorView? view))
            {
                continue;
            }
            view.SetAlive(ra.Alive);
            if (!ra.Alive)
            {
                continue;
            }
            view.SetPose(
                (float)Coords.SimToWorldX(ra.X, _arenaW),
                (float)Coords.SimToWorldZ(ra.Y, _arenaH),
                ra.Active,
                _clock);
            view.SetHealth((float)(ra.MaxHp > 0 ? ra.Hp / ra.MaxHp : 0), ra.ShieldActive);
            view.SetMarked(ra.Marked);
        }
    }

    private void DrainEvents()
    {
        List<SimEvent> events = _controller.DrainEvents();

        // Prompt 10 — a WeaponUltimate's own Hit events resolved in the same
        // sim tick, in the same order as its ResolutionTimeline's "hit" beats
        // (Ultimates.FireShapeQuery adds both together per target). Consuming
        // exactly that many of them here — wherever they fall among whatever
        // else is interspersed (a kill's Death, Prompt 6 Siphon's Heal, an
        // armor ultimate trigger) — and staggering them through VfxView.Defer
        // is what turns "telegraph before every strike" and "staggered damage
        // numbers" from data (Prompt 1's timeline) into something the player
        // actually sees, instead of the whole ultimate flashing at once.
        // Everything NOT captured this way (deaths, heals, shields, ...)
        // still fires immediately below, exactly as before.
        var consumedHitIndices = new HashSet<int>();
        for (int i = 0; i < events.Count; i++)
        {
            if (events[i].Kind == SimEventKind.WeaponUltimate && events[i].WeaponUlt is { } ultSpec)
            {
                ScheduleWeaponUltimateTimeline(events[i], ultSpec, events, i, consumedHitIndices);
            }
        }

        for (int i = 0; i < events.Count; i++)
        {
            if (consumedHitIndices.Contains(i))
            {
                continue;
            }
            SimEvent ev = events[i];
            switch (ev.Kind)
            {
                case SimEventKind.Launch:
                    _game.Sfx.Play(SfxId.Launch);
                    break;

                case SimEventKind.WallBounce:
                    _vfx.OnWallBounce(ev.Pos);
                    _game.Sfx.Play(SfxId.WallBounce, -14f);
                    break;

                case SimEventKind.EnemyBounce:
                    _vfx.OnEnemyBounce(ev.Pos);
                    _game.Sfx.Play(SfxId.WallBounce, -10f);
                    break;

                case SimEventKind.Hit:
                    PlayHit(ev);
                    break;

                case SimEventKind.Death:
                    _vfx.OnDeath(ev.Pos);
                    _game.Sfx.Play(SfxId.Death, -6f);
                    break;

                case SimEventKind.WeaponUltimate:
                    // Fully handled by ScheduleWeaponUltimateTimeline above —
                    // its own SFX plays there, immediately, matching the
                    // always-offset-0 first shape beat.
                    break;

                case SimEventKind.Heal:
                    _vfx.OnHeal(ev.Pos, ev.Amount);
                    _game.Sfx.Play(SfxId.Heal, -8f);
                    break;

                case SimEventKind.Shield:
                    _vfx.OnShield(ev.Pos, ev.Amount);
                    _game.Sfx.Play(SfxId.Heal, -10f);
                    break;

                case SimEventKind.ComboContact:
                    _vfx.OnComboContact(ev.Pos, (int)ev.Amount);
                    _game.Sfx.Play(SfxId.Heal, -6f);
                    _hud.PulseCombo();
                    break;

                case SimEventKind.MarkApplied:
                    _vfx.OnMarkApplied(ev.Pos);
                    _game.Sfx.Play(SfxId.UiTap, -4f);
                    break;

                case SimEventKind.EnemySplit:
                    _vfx.OnEnemySplit(ev.Pos);
                    _game.Sfx.Play(SfxId.Death, -8f);
                    break;

                case SimEventKind.Swap:
                    _vfx.OnSwap(ev.Pos);
                    _game.Sfx.Play(SfxId.UiTap, -4f);
                    break;
            }
        }
    }

    private void PlayHit(SimEvent ev)
    {
        bool targetIsHero = _visuals.TryGetValue(ev.TargetId, out ActorVisual? tv) && tv.Team == Team.Hero;
        _vfx.OnHit(ev.Pos, ev.Amount, ev.HitKind, targetIsHero);
        if (_actorViews.TryGetValue(ev.TargetId, out ActorView? tview))
        {
            tview.Flash(_clock);
        }
        _game.Sfx.Play(ev.Amount >= 250 ? SfxId.HeavyHit : SfxId.Hit, -8f);
    }

    /// <summary>Schedules one WeaponUltimate's whole ResolutionTimeline through
    /// VfxView.Defer and marks the Hit event indices it consumed so the main
    /// DrainEvents pass skips their immediate handling.</summary>
    private void ScheduleWeaponUltimateTimeline(
        SimEvent ultEvent, WeaponUltimateSpec spec, List<SimEvent> events, int ultIndex, HashSet<int> consumedHitIndices)
    {
        _game.Sfx.Play(SfxId.Ultimate, -4f);

        if (ultEvent.Timeline is not { } timeline)
        {
            _vfx.OnWeaponUltimate(ultEvent.Pos, ultEvent.Dir, spec); // no timeline data: fall back to firing everything at once
            return;
        }

        int hitBeatsNeeded = 0;
        foreach (TimelineBeat b in timeline.Beats)
        {
            if (b.Kind == "hit")
            {
                hitBeatsNeeded += 1;
            }
        }

        var hitEventsInOrder = new List<SimEvent>();
        for (int j = ultIndex + 1; j < events.Count && hitEventsInOrder.Count < hitBeatsNeeded; j++)
        {
            if (events[j].Kind == SimEventKind.Hit)
            {
                hitEventsInOrder.Add(events[j]);
                consumedHitIndices.Add(j);
            }
        }

        Vec2 pos = ultEvent.Pos;
        Vec2 dir = ultEvent.Dir;
        int hitIndex = 0;
        foreach (TimelineBeat beat in timeline.Beats)
        {
            if (beat.Kind == "shape")
            {
                _vfx.Defer(beat.OffsetSeconds, () => _vfx.OnWeaponUltimate(pos, dir, spec));
            }
            else if (beat.Kind == "hit" && hitIndex < hitEventsInOrder.Count)
            {
                SimEvent hitEv = hitEventsInOrder[hitIndex++];
                _vfx.Defer(beat.OffsetSeconds, () => PlayHit(hitEv));
            }
        }
    }

    // --- input --------------------------------------------------------------

    public override void _UnhandledInput(InputEvent @event)
    {
        if (_controller is null || _hud.OverlayVisible)
        {
            return;
        }

        // The project enables emulate_touch_from_mouse, so this single path serves
        // both a phone and the editor ג€” there is no mouse-only code to go untested.
        switch (@event)
        {
            case InputEventScreenTouch touch:
                if (touch.Pressed)
                {
                    if (_dragTouchIndex == -1)
                    {
                        BeginDrag(touch.Index, touch.Position);
                    }
                }
                else if (touch.Index == _dragTouchIndex)
                {
                    EndDrag(touch.Position);
                }
                break;

            case InputEventScreenDrag drag when drag.Index == _dragTouchIndex:
                UpdateDrag(drag.Position);
                break;
        }
    }

    private void BeginDrag(int index, Vector2 screenPos)
    {
        if (!_controller.IsAwaitingHeroInput() || !TryProjectToArena(screenPos, out Vec2 sim))
        {
            return;
        }
        _dragTouchIndex = index;
        _dragSim = sim;
        _controller.BeginAim(sim);
        RefreshAimView();
    }

    private void UpdateDrag(Vector2 screenPos)
    {
        if (!TryProjectToArena(screenPos, out Vec2 sim))
        {
            return;
        }
        _dragSim = sim;
        _controller.UpdateAim(sim);
        RefreshAimView();
    }

    private void EndDrag(Vector2 screenPos)
    {
        _dragTouchIndex = -1;
        _aim.HidePreview();
        if (TryProjectToArena(screenPos, out Vec2 sim))
        {
            _dragSim = sim;
        }
        _controller.Release(_dragSim);
    }

    private void RefreshAimView()
    {
        AimState? aim = _controller.GetAim();
        if (aim is null)
        {
            _aim.HidePreview();
            return;
        }
        _aim.Show(aim, _dragSim);
    }

    /// <summary>Screen point to a sim-space position on the actor plane.</summary>
    private bool TryProjectToArena(Vector2 screenPos, out Vec2 result)
    {
        result = Vec2.Zero;
        Vector3 from = _camera.ProjectRayOrigin(screenPos);
        Vector3 dir = _camera.ProjectRayNormal(screenPos);

        if (Mathf.Abs(dir.Y) < 0.0001f)
        {
            return false;
        }
        float t = (ActorHeight - from.Y) / dir.Y;
        if (t < 0)
        {
            return false;
        }

        Vector3 hit = from + dir * t;
        result = new Vec2(
            Coords.WorldToSimX(hit.X, _arenaW),
            Coords.WorldToSimZ(hit.Z, _arenaH));
        return true;
    }

    // --- end of battle ------------------------------------------------------

    private void ResolveBattleEnd()
    {
        _resolved = true;
        _aim.HidePreview();

        if (_controller.Winner == Team.Hero)
        {
            var heroes = new List<PostBattleHero>();
            foreach (Actor a in _controller.World.Actors)
            {
                if (a.Team != Team.Hero)
                {
                    continue;
                }
                heroes.Add(new PostBattleHero
                {
                    HeroId = a.Id,
                    Name = _visuals.TryGetValue(a.Id, out ActorVisual? v) ? v.DisplayName : a.Id,
                    Hp = a.Hp,
                    MaxHp = a.MaxHp,
                });
            }

            FloorResult result = _game.Run.ResolveFloorClear(heroes, _controller.Round);
            _game.Sfx.Play(SfxId.Victory, -4f);
            _hud.ShowVictory(result, runComplete: !result.HasNextFloor);
        }
        else
        {
            bool turnLimit = _controller.Round > _game.Content.Balance.Sim.TurnLimit;
            _game.Sfx.Play(SfxId.Defeat, -4f);
            _hud.ShowDefeat(_game.Run.CurrentFloor, turnLimit);
        }
    }

    private void OnContinue()
    {
        _game.Run.ContinueToNextFloor();
        _game.GoToBattle(); // reload the scene for the next floor
    }

    private void OnRestart()
    {
        _game.Run.RestartFloor();
        _game.GoToBattle();
    }

    private void OnQuit() => _game.GoToMenu();

    private void OnSwap()
    {
        _dragTouchIndex = -1;
        _aim.HidePreview();
        _controller.TrySwap();
    }

    /// <summary>Dev-only (`--autoplay`): aim the active hero at the nearest living
    /// enemy and fire at full draw. Used by the build script to play a whole floor
    /// without a human, so gameplay can be verified headlessly. With `--holdaim
    /// &lt;sec&gt;`, holds the draw (and refreshes the aim preview) for that long
    /// before releasing, so a `--shot` capture can land mid-aim.</summary>
    private void AutoFire(float delta)
    {
        if (DevCapture.AimHoldSeconds > 0)
        {
            if (_autoAimHoldElapsed < 0f)
            {
                if (!BeginAutoAim())
                {
                    return;
                }
                _autoAimHoldElapsed = 0f;
                return;
            }
            _autoAimHoldElapsed += delta;
            if (_autoAimHoldElapsed < DevCapture.AimHoldSeconds)
            {
                return;
            }
            _autoAimHoldElapsed = -1f;
        }

        if (!BeginAutoAim())
        {
            return;
        }
        _controller.Release(_dragSim);
    }

    /// <summary>Aims (without releasing) the active hero at the nearest living
    /// enemy, at full draw. Shared by the immediate-fire and `--holdaim` paths.
    /// Returns false if there is no living target.</summary>
    private bool BeginAutoAim()
    {
        Actor self = _controller.World.ActiveActor();
        Actor? target = null;
        double best = double.PositiveInfinity;
        foreach (Actor a in _controller.World.Actors)
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
        if (target is null)
        {
            return false;
        }

        Vec2 toward = (target.Pos - self.Pos).Normalized();
        _controller.BeginAim(self.Pos);
        // Aim opposite the target at full draw: drag = -direction * maxDrag.
        _dragSim = self.Pos - toward * _controller.MaxDrag;
        _controller.UpdateAim(_dragSim);
        RefreshAimView();
        return true;
    }
}


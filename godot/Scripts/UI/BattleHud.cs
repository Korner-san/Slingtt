using System;
using System.Collections.Generic;
using Godot;
using Slingtt.Game;
using Slingtt.Sim;

namespace Slingtt.Render;

// Battle HUD and the end-of-floor overlay. Lives on a CanvasLayer above the 3D
// viewport so it scales with the UI stretch, not with the camera.
public sealed partial class BattleHud : CanvasLayer
{
    public event Action? QuitPressed;
    public event Action? RestartPressed;
    public event Action? ContinuePressed;

    private Label _floorLabel = null!;
    private Label _roundLabel = null!;
    private Label _activeLabel = null!;
    private Label _hintLabel = null!;
    private HBoxContainer _turnStrip = null!;
    private Control _overlay = null!;
    private VBoxContainer _overlayBody = null!;

    private Content _content = null!;

    public static BattleHud Create(Content content)
    {
        var hud = new BattleHud { Name = "Hud", _content = content };
        hud.Build();
        return hud;
    }

    private void Build()
    {
        Layer = 10;

        var root = new Control();
        root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        root.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(root);

        MarginContainer safe = UiKit.SafeArea(14);
        safe.MouseFilter = Control.MouseFilterEnum.Ignore;
        root.AddChild(safe);

        VBoxContainer col = UiKit.VBox(8);
        col.MouseFilter = Control.MouseFilterEnum.Ignore;
        safe.AddChild(col);

        // --- top bar --------------------------------------------------------
        PanelContainer top = UiKit.MakePanel(Palette.UiSurface with { A = 0.85f });
        HBoxContainer topRow = UiKit.HBox(10);
        top.AddChild(topRow);

        _floorLabel = UiKit.MakeLabel("FLOOR 1", 22, Palette.UiText);
        _floorLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        topRow.AddChild(_floorLabel);

        _roundLabel = UiKit.MakeLabel("ROUND 1", 18, Palette.UiTextDim, HorizontalAlignment.Center);
        _roundLabel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        topRow.AddChild(_roundLabel);

        Button menuBtn = UiKit.MakeButton("MENU", Palette.UiSurfaceRaised, primary: false, fontSize: 18);
        menuBtn.CustomMinimumSize = new Vector2(96, UiKit.TouchMin);
        menuBtn.Pressed += () => QuitPressed?.Invoke();
        topRow.AddChild(menuBtn);

        col.AddChild(top);

        // --- turn order strip -----------------------------------------------
        PanelContainer stripPanel = UiKit.MakePanel(Palette.UiSurface with { A = 0.7f });
        _turnStrip = UiKit.HBox(6);
        stripPanel.AddChild(_turnStrip);
        col.AddChild(stripPanel);

        col.AddChild(UiKit.Spacer());

        // --- bottom: active actor + hint ------------------------------------
        PanelContainer bottom = UiKit.MakePanel(Palette.UiSurface with { A = 0.85f });
        VBoxContainer bottomCol = UiKit.VBox(2);
        bottom.AddChild(bottomCol);

        _activeLabel = UiKit.MakeLabel("", 24, Palette.TeamHero, HorizontalAlignment.Center);
        bottomCol.AddChild(_activeLabel);

        _hintLabel = UiKit.MakeLabel("Drag back from your hero, then release", 16, Palette.UiTextDim, HorizontalAlignment.Center);
        bottomCol.AddChild(_hintLabel);

        col.AddChild(bottom);

        // --- end-of-floor overlay -------------------------------------------
        _overlay = BuildOverlay();
        root.AddChild(_overlay);
    }

    private Control BuildOverlay()
    {
        // The backdrop IS the overlay root rather than a sibling ColorRect: a
        // sibling laid out while the overlay was still hidden ended up short,
        // leaving the arena visible under the results panel.
        var wrap = new ColorRect
        {
            Visible = false,
            // Near-opaque: at 0.9 the arena's bright unshaded HP bars still read
            // through at roughly the same value as the results panels, which makes
            // the screen look unfinished rather than modal.
            Color = Palette.UiBg with { A = 0.975f },
            MouseFilter = Control.MouseFilterEnum.Stop, // swallow taps meant for the arena
        };
        wrap.SetAnchorsPreset(Control.LayoutPreset.FullRect);

        MarginContainer safe = UiKit.SafeArea(22);
        wrap.AddChild(safe);

        var scroll = new ScrollContainer();
        scroll.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
        safe.AddChild(scroll);

        _overlayBody = UiKit.VBox(12);
        _overlayBody.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        scroll.AddChild(_overlayBody);

        return wrap;
    }

    // --- per-frame updates --------------------------------------------------

    public void SetFloor(int floor, bool isBoss)
    {
        _floorLabel.Text = isBoss ? $"FLOOR {floor}  ·  BOSS" : $"FLOOR {floor}";
        _floorLabel.AddThemeColorOverride("font_color", isBoss ? Palette.TeamEnemy : Palette.UiText);
    }

    public void UpdateBattle(BattleController controller, IReadOnlyDictionary<string, ActorVisual> visuals)
    {
        _roundLabel.Text = $"ROUND {Mathf.Max(controller.Round, 1)} / {_content.Balance.Sim.TurnLimit}";

        string activeId = controller.ActiveActorId ?? "";
        if (visuals.TryGetValue(activeId, out ActorVisual? v))
        {
            bool hero = v.Team == Team.Hero;
            _activeLabel.Text = hero ? $"{v.DisplayName}  —  YOUR TURN" : $"{v.DisplayName} is attacking";
            _activeLabel.AddThemeColorOverride("font_color", hero ? Palette.TeamHero : Palette.TeamEnemy);
            _hintLabel.Text = hero
                ? "Drag back from your hero, then release"
                : "Enemy turn…";
        }
        else
        {
            _activeLabel.Text = "";
            _hintLabel.Text = controller.Phase == Phase.Ended ? "" : "Resolving…";
        }

        RebuildTurnStrip(controller, visuals);
    }

    private void RebuildTurnStrip(BattleController controller, IReadOnlyDictionary<string, ActorVisual> visuals)
    {
        List<string> upcoming = controller.Upcoming(5);

        // Reuse existing children where possible; only rebuild when the shape changes.
        while (_turnStrip.GetChildCount() > upcoming.Count)
        {
            Node last = _turnStrip.GetChild(_turnStrip.GetChildCount() - 1);
            _turnStrip.RemoveChild(last);
            last.QueueFree();
        }
        while (_turnStrip.GetChildCount() < upcoming.Count)
        {
            Label l = UiKit.MakeLabel("", 15, Palette.UiTextDim, HorizontalAlignment.Center);
            l.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            _turnStrip.AddChild(l);
        }

        for (int i = 0; i < upcoming.Count; i++)
        {
            if (_turnStrip.GetChild(i) is not Label l)
            {
                continue;
            }
            string id = upcoming[i];
            string name = visuals.TryGetValue(id, out ActorVisual? v) ? v.DisplayName : id;
            bool isNow = i == 0;
            l.Text = isNow ? $"▶ {name}" : name;
            Color c = !visuals.TryGetValue(id, out ActorVisual? vv)
                ? Palette.UiTextDim
                : vv.Team == Team.Hero ? Palette.TeamHero : Palette.TeamEnemy;
            l.AddThemeColorOverride("font_color", isNow ? c : c.Darkened(0.35f));
        }
    }

    // --- overlay ------------------------------------------------------------

    public void ShowVictory(FloorResult result, bool runComplete)
    {
        ClearOverlay();

        _overlayBody.AddChild(UiKit.MakeLabel(
            runComplete ? "RUN COMPLETE" : "FLOOR CLEARED",
            42, Palette.UiSuccess, HorizontalAlignment.Center));
        string rounds = result.TurnsUsed == 1 ? "1 round" : $"{result.TurnsUsed} rounds";
        _overlayBody.AddChild(UiKit.MakeLabel(
            $"Floor {result.FloorNumber}  ·  {rounds}  ·  {result.SurvivingHeroes}/{result.TotalHeroes} heroes standing",
            17, Palette.UiTextDim, HorizontalAlignment.Center));

        if (result.Rewards.Count > 0)
        {
            PanelContainer rewards = UiKit.MakePanel();
            VBoxContainer rv = UiKit.VBox(6);
            rewards.AddChild(rv);
            rv.AddChild(UiKit.MakeLabel("REWARDS", 13, Palette.UiTextDim));
            foreach (RewardLine line in result.Rewards)
            {
                HBoxContainer row = UiKit.HBox(8);
                Label name = UiKit.MakeLabel(Rewards.Label(line.Resource), 19, Palette.Resource(line.Resource));
                name.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
                row.AddChild(name);
                row.AddChild(UiKit.MakeLabel($"+{UiKit.FormatNumber(line.Amount)}", 19, Palette.UiText, HorizontalAlignment.Right));
                row.AddChild(UiKit.MakeLabel($"({UiKit.FormatNumber(line.NewBalance)})", 15, Palette.UiTextDim, HorizontalAlignment.Right));
                rv.AddChild(row);
            }
            if (result.BossFirstClearBonus > 0)
            {
                rv.AddChild(UiKit.MakeLabel(
                    $"First-clear bonus: +{UiKit.FormatNumber(result.BossFirstClearBonus)} Sling Cores",
                    15, Palette.RaritySSR));
            }
            _overlayBody.AddChild(rewards);
        }

        PanelContainer team = UiKit.MakePanel();
        VBoxContainer tv = UiKit.VBox(5);
        team.AddChild(tv);
        tv.AddChild(UiKit.MakeLabel("TEAM", 13, Palette.UiTextDim));
        foreach (HeroResult h in result.Heroes)
        {
            HBoxContainer row = UiKit.HBox(8);
            Label n = UiKit.MakeLabel(h.Name, 18, h.Survived ? Palette.UiText : Palette.UiTextDim);
            n.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            row.AddChild(n);
            if (h.Survived)
            {
                if (h.HealingReceived > 0)
                {
                    row.AddChild(UiKit.MakeLabel($"+{UiKit.FormatNumber(h.HealingReceived)}", 15, Palette.VfxHeal, HorizontalAlignment.Right));
                }
                row.AddChild(UiKit.MakeLabel(
                    $"{UiKit.FormatNumber(h.CurrentHp)} / {UiKit.FormatNumber(h.MaxHp)}", 17, Palette.UiSuccess, HorizontalAlignment.Right));
            }
            else
            {
                row.AddChild(UiKit.MakeLabel("DOWN", 17, Palette.TeamEnemy, HorizontalAlignment.Right));
            }
            tv.AddChild(row);
        }
        _overlayBody.AddChild(team);

        if (result.Milestones.Count > 0)
        {
            PanelContainer ms = UiKit.MakePanel();
            VBoxContainer mv = UiKit.VBox(3);
            ms.AddChild(mv);
            mv.AddChild(UiKit.MakeLabel("PROGRESS", 13, Palette.UiTextDim));
            int shown = 0;
            foreach (Milestone m in result.Milestones)
            {
                if (shown++ >= 4)
                {
                    break;
                }
                mv.AddChild(UiKit.MakeLabel("• " + Slingtt.Game.Rewards.Text(m), 16, Palette.UiTextDim));
            }
            _overlayBody.AddChild(ms);
        }

        if (runComplete)
        {
            Button toMenu = UiKit.MakeButton("BACK TO MENU", Palette.TeamHero);
            toMenu.Pressed += () => QuitPressed?.Invoke();
            _overlayBody.AddChild(toMenu);
        }
        else
        {
            Button next = UiKit.MakeButton($"NEXT FLOOR  ({result.NextFloorNumber})", Palette.UiSuccess);
            next.Pressed += () => ContinuePressed?.Invoke();
            _overlayBody.AddChild(next);

            Button toMenu = UiKit.MakeButton("MENU", Palette.UiSurfaceRaised, primary: false, fontSize: 20);
            toMenu.Pressed += () => QuitPressed?.Invoke();
            _overlayBody.AddChild(toMenu);
        }

        ShowOverlay();
    }

    public void ShowDefeat(int floor, bool turnLimit)
    {
        ClearOverlay();

        _overlayBody.AddChild(UiKit.Spacer(40));
        _overlayBody.AddChild(UiKit.MakeLabel("DEFEAT", 44, Palette.TeamEnemy, HorizontalAlignment.Center));
        _overlayBody.AddChild(UiKit.MakeLabel(
            turnLimit ? $"Floor {floor} — ran out of rounds" : $"Floor {floor} — your team fell",
            18, Palette.UiTextDim, HorizontalAlignment.Center));
        _overlayBody.AddChild(UiKit.Spacer(20));

        Button retry = UiKit.MakeButton("RETRY FLOOR", Palette.TeamHero);
        retry.Pressed += () => RestartPressed?.Invoke();
        _overlayBody.AddChild(retry);

        Button toMenu = UiKit.MakeButton("MENU", Palette.UiSurfaceRaised, primary: false, fontSize: 20);
        toMenu.Pressed += () => QuitPressed?.Invoke();
        _overlayBody.AddChild(toMenu);

        ShowOverlay();
    }

    /// <summary>Force the overlay rect on show. A Control that was hidden when its
    /// parent last resized keeps a stale rect, and re-applying an anchor preset it
    /// already has is a no-op that won't re-sort it — so the size is set outright.</summary>
    private void ShowOverlay()
    {
        _overlay.Visible = true;
        Vector2 vp = GetViewport().GetVisibleRect().Size;
        _overlay.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _overlay.Position = Vector2.Zero;
        _overlay.Size = vp;
    }

    public void HideOverlay() => _overlay.Visible = false;

    public bool OverlayVisible => _overlay.Visible;

    private void ClearOverlay()
    {
        foreach (Node c in _overlayBody.GetChildren())
        {
            _overlayBody.RemoveChild(c);
            c.QueueFree();
        }
    }
}

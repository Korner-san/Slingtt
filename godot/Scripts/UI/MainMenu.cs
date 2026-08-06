using Godot;
using Slingtt.Game;

namespace Slingtt.Render;

// The first thing the app shows. It is a Control tree on an opaque background, so
// the very first rendered frame is unambiguously the game — never an empty 3D
// viewport clear colour.
public sealed partial class MainMenu : Control
{
    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(UiKit.Backdrop(Palette.UiBg));

        GameRoot? game = GameRoot.Instance;
        if (game is null)
        {
            BuildErrorScreen("GameRoot autoload is missing.");
            return;
        }
        if (game.FatalError is { } err)
        {
            BuildErrorScreen(err);
            return;
        }

        MarginContainer safe = UiKit.SafeArea(20);
        AddChild(safe);

        VBoxContainer root = UiKit.VBox(14);
        safe.AddChild(root);

        // --- header ---------------------------------------------------------
        root.AddChild(UiKit.MakeLabel("SLING TT", 54, Palette.UiText, HorizontalAlignment.Center));
        Label sub = UiKit.MakeLabel("E V O L U T I O N", 24, Palette.TeamHero, HorizontalAlignment.Center);
        root.AddChild(sub);

        root.AddChild(UiKit.Spacer(8));
        root.AddChild(BuildCurrencyBar(game.Run));
        root.AddChild(UiKit.Spacer(6));
        root.AddChild(BuildProgressPanel(game));
        root.AddChild(UiKit.Spacer(6));
        root.AddChild(BuildTeamPanel(game));

        root.AddChild(UiKit.Spacer());

        // --- actions --------------------------------------------------------
        bool resuming = game.Run.CurrentFloor > 1 || game.Run.HighestFloorCleared > 0;
        Button play = UiKit.MakeButton(
            resuming ? $"CONTINUE  ·  FLOOR {game.Run.CurrentFloor}" : "START RUN",
            Palette.TeamHero, primary: true, fontSize: 28);
        play.Pressed += () =>
        {
            game.Sfx.Play(SfxId.UiTap);
            game.GoToBattle();
        };
        root.AddChild(play);

        if (resuming)
        {
            Button reset = UiKit.MakeButton("NEW RUN  (keeps rewards)", Palette.UiSurfaceRaised, primary: false, fontSize: 22);
            reset.Pressed += () =>
            {
                game.Sfx.Play(SfxId.UiTap);
                game.Run.ResetRun(keepMeta: true);
                game.GoToMenu();
            };
            root.AddChild(reset);
        }

        root.AddChild(UiKit.MakeLabel(
            "Drag back from your hero and release to sling. Bounce off walls to hit enemies.",
            18, Palette.UiTextDim, HorizontalAlignment.Center, wrap: true));
    }

    private void BuildErrorScreen(string err)
    {
        MarginContainer safe = UiKit.SafeArea(24);
        AddChild(safe);
        VBoxContainer v = UiKit.VBox(16);
        safe.AddChild(v);
        v.AddChild(UiKit.Spacer());
        v.AddChild(UiKit.MakeLabel("Content failed to load", 34, Palette.TeamEnemy, HorizontalAlignment.Center));
        v.AddChild(UiKit.MakeLabel(err, 18, Palette.UiTextDim, HorizontalAlignment.Center, wrap: true));
        v.AddChild(UiKit.Spacer());
    }

    private static Control BuildCurrencyBar(RunState run)
    {
        PanelContainer panel = UiKit.MakePanel();
        HBoxContainer row = UiKit.HBox(6);
        panel.AddChild(row);

        foreach (ResourceKind kind in Rewards.Order)
        {
            VBoxContainer cell = UiKit.VBox(2);
            cell.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            cell.AddChild(UiKit.MakeLabel(
                UiKit.FormatNumber(run.BalanceOf(kind)), 22, Palette.Resource(kind), HorizontalAlignment.Center));
            cell.AddChild(UiKit.MakeLabel(
                Rewards.Label(kind), 13, Palette.UiTextDim, HorizontalAlignment.Center));
            row.AddChild(cell);
        }
        return panel;
    }

    private static Control BuildProgressPanel(GameRoot game)
    {
        PanelContainer panel = UiKit.MakePanel();
        HBoxContainer row = UiKit.HBox(10);
        panel.AddChild(row);

        row.AddChild(Stat("FLOOR", $"{game.Run.CurrentFloor} / {game.Run.MaxFloor}", Palette.UiText));
        row.AddChild(Stat("BEST", game.Run.HighestFloorCleared.ToString(), Palette.RaritySSR));

        FloorDef? floor = game.Content.Floor(game.Run.CurrentFloor);
        int enemyCount = floor?.Enemies.Count ?? 0;
        FloorClassification cls = Rewards.Classify(game.Run.CurrentFloor, game.Content.Balance.Progression);
        row.AddChild(Stat(cls.IsBoss ? "BOSS" : "ENEMIES", enemyCount.ToString(),
            cls.IsBoss ? Palette.TeamEnemy : Palette.UiText));

        return panel;
    }

    private static Control Stat(string label, string value, Color color)
    {
        VBoxContainer cell = UiKit.VBox(2);
        cell.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        cell.AddChild(UiKit.MakeLabel(value, 26, color, HorizontalAlignment.Center));
        cell.AddChild(UiKit.MakeLabel(label, 13, Palette.UiTextDim, HorizontalAlignment.Center));
        return cell;
    }

    private static Control BuildTeamPanel(GameRoot game)
    {
        PanelContainer panel = UiKit.MakePanel();
        VBoxContainer v = UiKit.VBox(8);
        panel.AddChild(v);
        v.AddChild(UiKit.MakeLabel("YOUR TEAM", 14, Palette.UiTextDim));

        foreach (LoadoutSlot slot in game.Run.Team)
        {
            if (!game.Content.Heroes.TryGetValue(slot.HeroId, out HeroDef? hero))
            {
                continue;
            }
            game.Content.Weapons.TryGetValue(slot.WeaponId, out WeaponDef? weapon);
            game.Content.Armor.TryGetValue(slot.ArmorId, out ArmorDef? armor);

            HBoxContainer row = UiKit.HBox(8);

            Color rarity = Palette.Rarity(weapon?.Rarity);
            var dot = new ColorRect
            {
                Color = rarity,
                CustomMinimumSize = new Vector2(6, 0),
                SizeFlagsVertical = SizeFlags.Fill,
            };
            row.AddChild(dot);

            VBoxContainer text = UiKit.VBox(1);
            text.SizeFlagsHorizontal = SizeFlags.ExpandFill;
            text.AddChild(UiKit.MakeLabel(game.Content.Name(hero.NameKey), 20, Palette.UiText));
            string gear = $"{game.Content.Name(weapon?.NameKey ?? "")}  ·  {game.Content.Name(armor?.NameKey ?? "")}";
            text.AddChild(UiKit.MakeLabel(gear, 14, Palette.UiTextDim));
            row.AddChild(text);

            string hp = slot.CurrentHp is { } cur ? UiKit.FormatNumber(cur) : "FULL";
            row.AddChild(UiKit.MakeLabel(hp, 18,
                slot.CurrentHp is null ? Palette.UiSuccess : Palette.UiTextDim, HorizontalAlignment.Right));

            v.AddChild(row);
        }
        return panel;
    }
}

using Godot;
using Slingtt.Game;

namespace Slingtt.Render;

// The actual first thing the app shows now (run/main_scene), ahead of
// MainMenu — a brief branded moment with the build version visible, so "what
// version is this APK" is answerable just by opening the app, not by digging
// through Android's App Info. Version text is never hardcoded here: it reads
// ProjectSettings' application/config/version, the same single source of
// truth the export tooling names the APK from (see scripts/export-apk.sh),
// so the two can never drift apart.
public sealed partial class SplashScreen : Control
{
    private const double HoldSeconds = 1.2;

    // GetTree().CreateTimer() is owned by the SceneTree, not by this node —
    // its Timeout keeps firing on schedule even after ChangeSceneToFile has
    // already freed this node and moved on (e.g. DevCapture's --autobattle,
    // which jumps straight to Battle well inside the 1.2s hold). Without
    // this guard, the orphaned timer would still fire GoToMenu() at the
    // 1.2s mark and yank the player out of whatever scene is active by
    // then, regardless of what it is.
    private bool _stillOnSplash = true;

    public override void _ExitTree() => _stillOnSplash = false;

    public override void _Ready()
    {
        SetAnchorsPreset(LayoutPreset.FullRect);
        AddChild(UiKit.Backdrop(Palette.UiBg));

        MarginContainer safe = UiKit.SafeArea(20);
        AddChild(safe);

        // Same "safe area -> one VBox -> expanding spacers" shape MainMenu
        // already uses to push its own action buttons to the bottom — here
        // two spacers centre the title block and pin the version label to
        // the very bottom of the safe area.
        VBoxContainer root = UiKit.VBox(6);
        safe.AddChild(root);

        root.AddChild(UiKit.Spacer());
        root.AddChild(UiKit.MakeLabel("SLING TT", 54, Palette.UiText, HorizontalAlignment.Center));
        root.AddChild(UiKit.MakeLabel("E V O L U T I O N", 24, Palette.TeamHero, HorizontalAlignment.Center));
        root.AddChild(UiKit.Spacer());

        string version = (string)ProjectSettings.GetSetting("application/config/version", "");
        root.AddChild(UiKit.MakeLabel(
            string.IsNullOrEmpty(version) ? "" : $"v{version}", 15, Palette.UiTextDim, HorizontalAlignment.Center));

        GetTree().CreateTimer(HoldSeconds).Timeout += () =>
        {
            if (_stillOnSplash)
            {
                GameRoot.Instance?.GoToMenu();
            }
        };
    }
}

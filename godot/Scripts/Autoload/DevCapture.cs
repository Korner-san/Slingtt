using Godot;

namespace Slingtt.Render;

// Headless-ish verification aid. Lets a build script prove what the game actually
// draws instead of trusting that "the export succeeded" means "the app works" —
// which is precisely the assumption that let a grey screen ship last time.
//
// Activated only by command-line flags after `--`, so it is inert in a real build:
//   --shot <path>        write a PNG of the framebuffer, then quit
//   --shot-delay <sec>   wait this long first (default 1.5)
//   --autobattle         jump straight into the battle scene on boot
//   --autoplay           fire the active hero automatically each turn
//   --autoitems          jump straight into the items (armory) screen on boot
//   --itemspage <name>   gacha|upgrade|evolution — which ItemsScreen page to
//                        start on (implies --autoitems); default gacha
//   --holdaim <sec>      with --autoplay, hold each auto-fired shot at full draw
//                        for this many seconds (aim computed and RefreshAimView()
//                        called) before releasing, so a --shot capture can land
//                        mid-aim and prove out the aim-preview render (dots,
//                        enemy-bounce prediction, ultimate footprint) instead of
//                        only ever seeing post-release battle state
public sealed partial class DevCapture : Node
{
    public static bool AutoPlay { get; private set; }
    public static string? ItemsPage { get; private set; }
    public static double AimHoldSeconds { get; private set; }

    private string? _shotPath;
    private double _delay = 1.5;
    private double _elapsed;
    private bool _done;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        string[] args = OS.GetCmdlineUserArgs();
        bool autoBattle = false;
        bool autoItems = false;

        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--shot" when i + 1 < args.Length:
                    _shotPath = args[++i];
                    break;
                case "--shot-delay" when i + 1 < args.Length:
                    _delay = args[++i].ToFloat();
                    break;
                case "--autobattle":
                    autoBattle = true;
                    break;
                case "--autoplay":
                    AutoPlay = true;
                    break;
                case "--autoitems":
                    autoItems = true;
                    break;
                case "--itemspage" when i + 1 < args.Length:
                    ItemsPage = args[++i];
                    autoItems = true;
                    break;
                case "--holdaim" when i + 1 < args.Length:
                    AimHoldSeconds = args[++i].ToFloat();
                    break;
            }
        }

        if (autoBattle)
        {
            GD.Print("[DevCapture] auto-entering battle");
            GameRoot.Instance?.GoToBattle();
        }
        else if (autoItems)
        {
            GD.Print("[DevCapture] auto-entering items screen");
            GameRoot.Instance?.GoToItems();
        }
        if (_shotPath is null)
        {
            SetProcess(false);
        }
    }

    public override void _Process(double delta)
    {
        if (_done || _shotPath is null)
        {
            return;
        }
        _elapsed += delta;
        if (_elapsed < _delay)
        {
            return;
        }
        _done = true;

        Image img = GetViewport().GetTexture().GetImage();
        Error err = img.SavePng(_shotPath);
        GD.Print($"[DevCapture] screenshot {_shotPath} -> {err} ({img.GetWidth()}x{img.GetHeight()})");
        GetTree().Quit();
    }
}

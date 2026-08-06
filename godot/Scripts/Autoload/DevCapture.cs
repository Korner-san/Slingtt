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
public sealed partial class DevCapture : Node
{
    public static bool AutoPlay { get; private set; }

    private string? _shotPath;
    private double _delay = 1.5;
    private double _elapsed;
    private bool _done;

    public override void _Ready()
    {
        ProcessMode = ProcessModeEnum.Always;

        string[] args = OS.GetCmdlineUserArgs();
        bool autoBattle = false;

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
            }
        }

        if (autoBattle)
        {
            GD.Print("[DevCapture] auto-entering battle");
            GameRoot.Instance?.GoToBattle();
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

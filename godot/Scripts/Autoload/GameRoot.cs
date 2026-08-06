using Godot;
using Slingtt.Game;
using Slingtt.Sim;

namespace Slingtt.Render;

/// <summary>Persistence backed by Godot's user:// filesystem, which is the one
/// writable location that exists on both desktop and Android.</summary>
public sealed class GodotSaveStore : ISaveStore
{
    private const string Path = "user://slingtt-run-v1.json";

    public string? Load()
    {
        if (!Godot.FileAccess.FileExists(Path))
        {
            return null;
        }
        using Godot.FileAccess f = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Read);
        return f?.GetAsText();
    }

    public void Save(string json)
    {
        using Godot.FileAccess f = Godot.FileAccess.Open(Path, Godot.FileAccess.ModeFlags.Write);
        f?.StoreString(json);
    }
}

// Autoloaded singleton. Owns the content catalog, the run state, and the shared
// SimConfig, so scenes stay cheap to enter and leave. Everything here is
// engine-side glue: no gameplay rules live in this file.
public sealed partial class GameRoot : Node
{
    public static GameRoot Instance { get; private set; } = null!;

    public Content Content { get; private set; } = null!;
    public RunState Run { get; private set; } = null!;
    public SimConfig SimConfig { get; private set; } = null!;
    public Sfx Sfx { get; private set; } = null!;
    public IAdProvider AdProvider { get; private set; } = null!;

    /// <summary>Set when content fails to load. The menu surfaces this instead of
    /// showing an empty screen — a visible error beats a grey rectangle.</summary>
    public string? FatalError { get; private set; }

    public const string MenuScene = "res://scenes/MainMenu.tscn";
    public const string BattleScene = "res://scenes/Battle.tscn";
    public const string ItemsScene = "res://scenes/ItemsScreen.tscn";

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        Sfx = new Sfx { Name = "Sfx" };
        AddChild(Sfx);

        var adProvider = new SimulatedAdProvider { Name = "AdProvider" };
        AddChild(adProvider);
        AdProvider = adProvider;

        try
        {
            // Fully qualified: the `Content` property shadows the type name here.
            Content = Slingtt.Game.Content.Load();
            SimConfig = SimConfigBuilder.Build(Content.Balance);
            Run = new RunState(Content, new GodotSaveStore());
            GD.Print($"[Slingtt] content loaded: {Content.Heroes.Count} heroes, " +
                     $"{Content.Enemies.Count} enemies, {Content.Floors.Count} floors, " +
                     $"arena {Content.Balance.Arena.W}x{Content.Balance.Arena.H}");
        }
        catch (System.Exception e)
        {
            FatalError = e.Message;
            GD.PushError($"[Slingtt] content failed to load: {e}");
        }
    }

    public void GoToMenu() => GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, MenuScene);

    public void GoToBattle() => GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, BattleScene);

    public void GoToItems() => GetTree().CallDeferred(SceneTree.MethodName.ChangeSceneToFile, ItemsScene);
}

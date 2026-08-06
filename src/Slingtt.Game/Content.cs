using System.Reflection;
using System.Text.Json;

namespace Slingtt.Game;

/// <summary>The validated content catalog, loaded once from JSON embedded in this
/// assembly. Nothing below this layer imports content — Slingtt.Sim receives plain
/// data as arguments.</summary>
public sealed class Content
{
    public Dictionary<string, HeroDef> Heroes { get; } = new();
    public Dictionary<string, WeaponDef> Weapons { get; } = new();
    public Dictionary<string, ArmorDef> Armor { get; } = new();
    public Dictionary<string, EnemyDef> Enemies { get; } = new();
    public Dictionary<string, UltimateDef> Ultimates { get; } = new();
    public List<FloorDef> Floors { get; } = new();
    public Balance Balance { get; private set; } = new();
    public Dictionary<string, string> Names { get; } = new();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static Content? _instance;

    /// <summary>Process-wide catalog. Loading is pure and idempotent.</summary>
    public static Content Load()
    {
        return _instance ??= LoadFresh();
    }

    private static Content LoadFresh()
    {
        var c = new Content();

        foreach (HeroDef h in ReadList<HeroDef>("heroes.json"))
        {
            c.Heroes[h.Id] = h;
        }
        foreach (WeaponDef w in ReadList<WeaponDef>("weapons.json"))
        {
            c.Weapons[w.Id] = w;
        }
        foreach (ArmorDef a in ReadList<ArmorDef>("armor.json"))
        {
            c.Armor[a.Id] = a;
        }
        foreach (EnemyDef e in ReadList<EnemyDef>("enemies.json"))
        {
            c.Enemies[e.Id] = e;
        }
        foreach (UltimateDef u in ReadList<UltimateDef>("ultimates.json"))
        {
            c.Ultimates[u.Id] = u;
        }
        c.Floors.AddRange(ReadList<FloorDef>("floors.json"));
        c.Floors.Sort((a, b) => a.Floor.CompareTo(b.Floor));

        c.Balance = JsonSerializer.Deserialize<Balance>(ReadText("balance.json"), JsonOpts)
                    ?? throw new InvalidOperationException("content: balance.json failed to parse");

        Dictionary<string, string>? names =
            JsonSerializer.Deserialize<Dictionary<string, string>>(ReadText("en.json"), JsonOpts);
        if (names is not null)
        {
            foreach (KeyValuePair<string, string> kv in names)
            {
                c.Names[kv.Key] = kv.Value;
            }
        }

        return c;
    }

    /// <summary>Display name for a content nameKey, falling back to the key so a
    /// missing string is visible rather than blank.</summary>
    public string Name(string nameKey)
        => Names.TryGetValue(nameKey, out string? v) ? v : nameKey;

    public int MaxFloor => Floors.Count == 0 ? 1 : Floors[^1].Floor;

    public FloorDef? Floor(int floorNumber)
    {
        foreach (FloorDef f in Floors)
        {
            if (f.Floor == floorNumber)
            {
                return f;
            }
        }
        return null;
    }

    private static List<T> ReadList<T>(string fileName)
        => JsonSerializer.Deserialize<List<T>>(ReadText(fileName), JsonOpts) ?? new List<T>();

    private static string ReadText(string fileName)
    {
        Assembly asm = typeof(Content).Assembly;
        // Embedded resource names are "<RootNamespace>.content.<file>"; matching on
        // the suffix keeps this working if the namespace or folder ever moves.
        string? match = null;
        foreach (string n in asm.GetManifestResourceNames())
        {
            if (n.EndsWith("." + fileName, StringComparison.Ordinal) || n == fileName)
            {
                match = n;
                break;
            }
        }
        if (match is null)
        {
            throw new InvalidOperationException(
                $"content: embedded resource '{fileName}' not found. Present: {string.Join(", ", asm.GetManifestResourceNames())}");
        }

        using Stream s = asm.GetManifestResourceStream(match)
                         ?? throw new InvalidOperationException($"content: cannot open '{match}'");
        using var reader = new StreamReader(s);
        return reader.ReadToEnd();
    }
}

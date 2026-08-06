using System.Text.Json;
using System.Text.Json.Serialization;

namespace Slingtt.Game;

/// <summary>Persistence seam. Slingtt.Game stays engine-free: the render layer
/// supplies an implementation backed by Godot's user:// filesystem.</summary>
public interface ISaveStore
{
    string? Load();
    void Save(string json);
}

public sealed class HeroResult
{
    public string HeroId { get; set; } = "";
    public string Name { get; set; } = "";
    public double CurrentHp { get; set; }
    public double MaxHp { get; set; }
    public double HpBeforeHealing { get; set; }
    public double HealingReceived { get; set; }
    public bool Survived { get; set; }
}

public sealed class RewardLine
{
    public ResourceKind Resource { get; set; }
    public int Amount { get; set; }
    public int PreviousBalance { get; set; }
    public int NewBalance { get; set; }
}

public sealed class FloorResult
{
    public string GrantId { get; set; } = "";
    public int FloorNumber { get; set; }
    public bool IsBossFloor { get; set; }
    public bool IsCheckpoint { get; set; }
    public int TurnsUsed { get; set; }
    public int TurnLimit { get; set; }
    public int SurvivingHeroes { get; set; }
    public int TotalHeroes { get; set; }
    public List<HeroResult> Heroes { get; set; } = new();
    public List<RewardLine> Rewards { get; set; } = new();
    public int BossFirstClearBonus { get; set; }
    public bool IsFirstClear { get; set; }
    public bool IsNewHighestFloor { get; set; }
    public int NextFloorNumber { get; set; }
    public bool HasNextFloor { get; set; }
    public List<Milestone> Milestones { get; set; } = new();
}

public sealed class PostBattleHero
{
    public string HeroId { get; set; } = "";
    public string Name { get; set; } = "";
    public double Hp { get; set; } // post-battle HP, before floor-clear healing
    public double MaxHp { get; set; }
}

/// <summary>Everything that survives an app restart.</summary>
public sealed class RunSave
{
    public string RunId { get; set; } = "";
    public int CurrentFloor { get; set; } = 1;
    public int HighestFloorCleared { get; set; }
    public List<int> FirstClears { get; set; } = new();
    public List<LoadoutSlot> Team { get; set; } = new();
    public uint SeedBase { get; set; }
    public Dictionary<string, int> Balances { get; set; } = new();
    /// <summary>Grant ids already paid out, so a crash mid-results can't double-grant.</summary>
    public List<string> GrantedIds { get; set; } = new();

    /// <summary>Prompt 8 — gacha meta-progression (pity, pieces, tokens,
    /// inventory, essence). Carried forward across a "new run" the same way
    /// currency balances are (see RunState.ResetRun).</summary>
    public GachaSave Gacha { get; set; } = new();
}

// Local run + meta-progression state. Persists so a resumed run keeps its place,
// its HP, and — crucially — its already-granted floor rewards, which is how
// reward duplication is prevented across restarts.
public sealed class RunState
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Content _content;
    private readonly ISaveStore _store;
    private RunSave _save = new();

    public RunState(Content content, ISaveStore store)
    {
        _content = content;
        _store = store;
        LoadOrCreate();
    }

    public int CurrentFloor => _save.CurrentFloor;
    public int HighestFloorCleared => _save.HighestFloorCleared;
    public List<LoadoutSlot> Team => _save.Team;
    public int MaxFloor => _content.MaxFloor;
    public FloorResult? PendingResult { get; private set; }

    public int BalanceOf(ResourceKind kind)
        => _save.Balances.GetValueOrDefault(kind.ToString());

    public Dictionary<ResourceKind, int> AllBalances()
    {
        var d = new Dictionary<ResourceKind, int>();
        foreach (ResourceKind k in Rewards.Order)
        {
            d[k] = BalanceOf(k);
        }
        return d;
    }

    /// <summary>Deterministic per-floor seed, so "restart floor" replays the same
    /// battle setup.</summary>
    public uint SeedForFloor(int floor)
        => unchecked(_save.SeedBase ^ (uint)(floor * 0x9E3779B9));

    // --- Prompt 8: gacha economy ---------------------------------------------
    // Thin wrappers over GachaEconomy (pure, content+GachaSave only): this is
    // the one place that also touches the SlingCores balance (Tier 1's pull
    // cost lives in the same ResourceKind system floor rewards use) and
    // persistence. Tier 2/3 pulls are gated entirely by their own tokens, so
    // they never touch SlingCores at all.

    public int EssenceBalanceOf(GachaTab tab) => GachaEconomy.EssenceBalanceOf(_save.Gacha, tab);

    public GachaTabState GachaTabStateOf(GachaTab tab) => GachaEconomy.TabOf(_save.Gacha, tab);

    public PullResult Pull(GachaTab tab, GachaTier tier)
    {
        if (tier == GachaTier.Tier1)
        {
            int cost = _content.Balance.Gacha.Tier1PullCost;
            int have = BalanceOf(ResourceKind.SlingCores);
            if (have < cost)
            {
                return new PullResult { Success = false, FailureReason = "insufficient-sling-cores" };
            }
            _save.Balances[ResourceKind.SlingCores.ToString()] = have - cost;
        }

        PullResult result = GachaEconomy.Pull(_content, _save.Gacha, tab, tier);
        if (result.Success)
        {
            Persist();
        }
        return result;
    }

    public SacrificeResult Sacrifice(GachaTab tab, string instanceId, GachaTab receiveInvestedAs)
    {
        SacrificeResult result = GachaEconomy.Sacrifice(_content, _save.Gacha, tab, instanceId, receiveInvestedAs);
        if (result.Success)
        {
            Persist();
        }
        return result;
    }

    public EnhanceResult Enhance(GachaTab tab, string instanceId)
    {
        EnhanceResult result = GachaEconomy.Enhance(_content, _save.Gacha, tab, instanceId, _content.Balance.Progression.MaxLevel);
        if (result.Success)
        {
            Persist();
        }
        return result;
    }

    private void LoadOrCreate()
    {
        string? raw = _store.Load();
        if (!string.IsNullOrWhiteSpace(raw))
        {
            try
            {
                RunSave? loaded = JsonSerializer.Deserialize<RunSave>(raw, JsonOpts);
                if (loaded is not null && loaded.Team.Count > 0)
                {
                    _save = loaded;
                    return;
                }
            }
            catch (JsonException)
            {
                // A corrupt save must not brick the app — start a fresh run instead.
            }
        }
        ResetRun(keepMeta: false);
    }

    private void Persist()
    {
        _store.Save(JsonSerializer.Serialize(_save, JsonOpts));
    }

    /// <summary>Start a brand-new run. All-time meta (highest floor, first clears,
    /// currency) survives unless keepMeta is false.</summary>
    public void ResetRun(bool keepMeta = true)
    {
        var next = new RunSave
        {
            RunId = "run_" + DateTime.UtcNow.Ticks.ToString("x"),
            CurrentFloor = 1,
            SeedBase = unchecked((uint)DateTime.UtcNow.Ticks),
            Team = BattleSetup.DefaultTeam(),
            Gacha = new GachaSave { RngState = unchecked((uint)(DateTime.UtcNow.Ticks >> 3)) },
        };
        if (keepMeta)
        {
            next.HighestFloorCleared = _save.HighestFloorCleared;
            next.FirstClears = _save.FirstClears;
            next.Balances = _save.Balances;
            next.GrantedIds = _save.GrantedIds;
            next.Gacha = _save.Gacha;
        }
        _save = next;
        PendingResult = null;
        Persist();
    }

    /// <summary>Replay the current floor at full HP. Does not re-grant an
    /// already-cleared floor.</summary>
    public void RestartFloor()
    {
        foreach (LoadoutSlot slot in _save.Team)
        {
            slot.CurrentHp = null;
        }
        PendingResult = null;
        Persist();
    }

    /// <summary>Resolve a floor clear: grant rewards at most once, compute the
    /// FloorResult, and record meta progress. Safe to call repeatedly for the same
    /// floor — the grant id gates the payout.</summary>
    public FloorResult ResolveFloorClear(List<PostBattleHero> heroes, int turnsUsed)
    {
        int floor = _save.CurrentFloor;
        string grantId = $"{_save.RunId}:floor:{floor}";

        FloorClassification cls = Rewards.Classify(floor, _content.Balance.Progression);
        bool alreadyGranted = _save.GrantedIds.Contains(grantId);
        bool isFirstClear = !_save.FirstClears.Contains(floor);
        bool isNewHighestFloor = floor > _save.HighestFloorCleared;

        RewardAmounts rewards = Rewards.CalculateFloorRewards(
            floor, cls.IsBoss, isFirstClear, _content.Balance.Rewards);

        var lines = new List<RewardLine>();
        foreach (ResourceKind kind in Rewards.Order)
        {
            int amount = Rewards.AmountOf(rewards, kind);
            if (amount <= 0)
            {
                continue;
            }
            int prev = BalanceOf(kind);
            int next = alreadyGranted ? prev : prev + amount;
            if (!alreadyGranted)
            {
                _save.Balances[kind.ToString()] = next;
            }
            lines.Add(new RewardLine
            {
                Resource = kind,
                Amount = amount,
                PreviousBalance = prev,
                NewBalance = next,
            });
        }

        var heroResults = new List<HeroResult>();
        foreach (PostBattleHero h in heroes)
        {
            bool survived = h.Hp > 0;
            double heal = survived ? Slingtt.Sim.SimMath.RoundJs(h.MaxHp * rewards.HealPctMaxHp) : 0;
            double finalHp = survived ? Math.Min(h.MaxHp, h.Hp + heal) : 0;
            heroResults.Add(new HeroResult
            {
                HeroId = h.HeroId,
                Name = h.Name,
                CurrentHp = finalHp,
                MaxHp = h.MaxHp,
                HpBeforeHealing = h.Hp,
                HealingReceived = finalHp - h.Hp,
                Survived = survived,
            });
        }

        int nextFloorNumber = floor + 1;
        bool hasNextFloor = nextFloorNumber <= _content.MaxFloor;

        var result = new FloorResult
        {
            GrantId = grantId,
            FloorNumber = floor,
            IsBossFloor = cls.IsBoss,
            IsCheckpoint = cls.IsCheckpoint,
            TurnsUsed = turnsUsed,
            TurnLimit = _content.Balance.Sim.TurnLimit,
            SurvivingHeroes = heroResults.Count(h => h.Survived),
            TotalHeroes = heroResults.Count,
            Heroes = heroResults,
            Rewards = lines,
            BossFirstClearBonus = rewards.BossFirstClearBonus,
            IsFirstClear = isFirstClear,
            IsNewHighestFloor = isNewHighestFloor,
            NextFloorNumber = nextFloorNumber,
            HasNextFloor = hasNextFloor,
            Milestones = Rewards.ComputeMilestones(
                AllBalances(), rewards, cls, isNewHighestFloor, nextFloorNumber, hasNextFloor, _content.Balance),
        };

        if (!alreadyGranted)
        {
            _save.GrantedIds.Add(grantId);
        }
        if (isFirstClear)
        {
            _save.FirstClears.Add(floor);
        }
        _save.HighestFloorCleared = Math.Max(_save.HighestFloorCleared, floor);

        PendingResult = result;
        Persist();
        return result;
    }

    /// <summary>Bank surviving HP (post-heal) and advance to the next floor.</summary>
    public void ContinueToNextFloor()
    {
        if (PendingResult is not { } result)
        {
            return;
        }
        foreach (LoadoutSlot slot in _save.Team)
        {
            HeroResult? h = result.Heroes.FirstOrDefault(x => x.HeroId == slot.HeroId);
            if (h is not null)
            {
                slot.CurrentHp = h.CurrentHp;
            }
        }
        if (result.HasNextFloor)
        {
            _save.CurrentFloor = result.NextFloorNumber;
        }
        PendingResult = null;
        Persist();
    }
}

using System.Collections.Generic;
using System.Linq;

namespace Slingtt.Sim;

public sealed class ActorInit
{
    public string Id { get; init; } = "";
    public Team Team { get; init; }
    public Vec2 Pos { get; init; }
    public double Radius { get; init; } = 0.5;
    public double Hp { get; init; }
    public double Def { get; init; }
    public WeaponStats Weapon { get; init; } = null!;
    public ArmorStats? Armor { get; init; }
    public int MoveDurationTicks { get; init; }
}

public sealed class WorldSetup
{
    public uint Seed { get; init; }
    public double BoundsW { get; init; }
    public double BoundsH { get; init; }
    public List<ActorInit> Actors { get; init; } = new();
}

/// <summary>The sim's arena is axis-aligned with its origin at a corner: x in
/// [0, W], y in [0, H]. Heroes spawn at high y, enemies at low y. The render
/// layer re-centres it — see Slingtt.Game.Coords.</summary>
public sealed class World
{
    public int Tick;
    public int Round;
    public RngState Rng;
    public double BoundsW;
    public double BoundsH;

    /// <summary>Stable order, never re-sorted in place — turn order indexes into it.</summary>
    public List<Actor> Actors = new();

    public string? ActiveActorId;
    public int TurnCursor = -1; // first AdvanceTurn moves to index 0
    public Phase Phase = Phase.Settling; // first step advances into round 1 / first turn
    public Team? Winner;

    public List<SimEvent> Events = new();

    /// <summary>"sourceId:targetId" -> tick of last damage, for the contact cooldown.</summary>
    public Dictionary<string, int> ContactLog = new();

    public bool DamageEnabled = true; // false in aim-prediction clones

    public static World Create(WorldSetup setup)
    {
        var w = new World
        {
            Tick = 0,
            Round = 0,
            Rng = new RngState(setup.Seed),
            BoundsW = setup.BoundsW,
            BoundsH = setup.BoundsH,
            ActiveActorId = null,
            TurnCursor = -1,
            Phase = Phase.Settling,
            Winner = null,
        };
        foreach (ActorInit init in setup.Actors)
        {
            w.Actors.Add(new Actor
            {
                Id = init.Id,
                Team = init.Team,
                Pos = init.Pos,
                Vel = Vec2.Zero,
                Radius = init.Radius,
                Hp = init.Hp,
                MaxHp = init.Hp,
                Def = init.Def,
                Weapon = init.Weapon,
                Armor = init.Armor,
                MoveDurationTicks = init.MoveDurationTicks,
            });
        }
        return w;
    }

    public Actor GetActor(string id)
    {
        foreach (Actor a in Actors)
        {
            if (a.Id == id)
            {
                return a;
            }
        }
        throw new InvalidOperationException($"sim: no actor {id}");
    }

    public Actor ActiveActor()
    {
        if (ActiveActorId is null)
        {
            throw new InvalidOperationException("sim: no active actor");
        }
        return GetActor(ActiveActorId);
    }

    public List<Actor> LivingTeam(Team team)
    {
        var outp = new List<Actor>();
        foreach (Actor a in Actors)
        {
            if (a.Team == team && a.IsAlive)
            {
                outp.Add(a);
            }
        }
        return outp;
    }

    public int LivingCount(Team team)
    {
        int n = 0;
        foreach (Actor a in Actors)
        {
            if (a.Team == team && a.IsAlive)
            {
                n++;
            }
        }
        return n;
    }

    /// <summary>Flat structural clone — cheap enough for per-drag aim prediction.
    /// Weapon/armor stats are never mutated by the sim, so sharing them is safe.</summary>
    public World Clone()
    {
        var c = new World
        {
            Tick = Tick,
            Round = Round,
            Rng = Rng,
            BoundsW = BoundsW,
            BoundsH = BoundsH,
            ActiveActorId = ActiveActorId,
            TurnCursor = TurnCursor,
            Phase = Phase,
            Winner = Winner,
            DamageEnabled = DamageEnabled,
            ContactLog = new Dictionary<string, int>(ContactLog),
        };
        foreach (Actor a in Actors)
        {
            c.Actors.Add(a.Clone());
        }
        return c;
    }
}

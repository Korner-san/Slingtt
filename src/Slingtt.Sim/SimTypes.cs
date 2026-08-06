namespace Slingtt.Sim;

// Core simulation types, ported from the web original's src/sim/types.ts.
// Everything here is plain serializable data — if a value influencing the outcome
// can't survive a round-trip, replay breaks. sim/ never imports content;
// Slingtt.Game maps validated content into these shapes.

public enum Team
{
    Hero,
    Enemy,
}

public enum WeaponType
{
    Sword,
    Lance,
    Hammer,
}

public enum Phase
{
    Aiming,
    Travelling,
    Ultimate,
    Settling,
    Ended,
}

public enum HitKind
{
    Contact,
    Aoe,
    Pierce,
    Ultimate,
    ArmorReflect,
}

public enum WeaponUltKind
{
    Cross,
    Beam,
    Aftershock,
}

public enum ArmorUltKind
{
    Bulwark,
    Swift,
    Vital,
}

public enum EndReason
{
    Elimination,
    TurnLimit,
}

/// <summary>Discriminated-union stand-in for the TS WeaponUltimateSpec. Only the
/// fields belonging to <see cref="Kind"/> are meaningful.</summary>
public readonly struct WeaponUltimateSpec
{
    public WeaponUltKind Kind { get; init; }
    public double Width { get; init; }
    public double DmgMult { get; init; }
    public bool DoubleCross { get; init; }
    public bool SecondaryBeam { get; init; }
    public double Radius { get; init; }
    public int StunTurns { get; init; }
}

/// <summary>Discriminated-union stand-in for the TS ArmorUltimateSpec.</summary>
public readonly struct ArmorUltimateSpec
{
    public ArmorUltKind Kind { get; init; }
    public double ShieldRatio { get; init; }
    public int Rounds { get; init; }
    public double MoveMult { get; init; }
    public double HealRatio { get; init; }
}

/// <summary>Never mutated by the sim, so clones share the reference (matches the
/// web original's cloneWorld).</summary>
public sealed class WeaponStats
{
    public WeaponType Type { get; init; }
    public double Atk { get; init; }
    public int Tier { get; init; } // evolution tier 0-3; scales damage and gates the ultimate
    public double BounceDecay { get; init; } = 0.1; // sword
    public int PierceCount { get; init; } = 1; // lance
    public double AoeRadius { get; init; } = 1.0; // hammer
    public WeaponUltimateSpec? Ultimate { get; init; } // null below tier 1 (item level < 10)
}

public sealed class ArmorStats
{
    public int MoveDurationTicks { get; init; }
    public int Tier { get; init; }
    public ArmorUltimateSpec? Ultimate { get; init; }
}

public sealed class Actor
{
    public string Id { get; init; } = "";
    public Team Team { get; init; }
    public Vec2 Pos;
    public Vec2 Vel;
    public double Radius { get; init; }
    public double Hp;
    public double MaxHp;
    public double Def { get; init; }
    public WeaponStats Weapon { get; init; } = null!;
    public ArmorStats? Armor { get; init; }

    public int MoveDurationTicks { get; init; }
    public int TravelTicksRemaining;
    public int TravelTickCount;
    public int HitsThisTravel;
    public List<string> PiercedIds = new();
    public int PierceBudgetUsed;
    public bool ArmorUltFired;
    public double ShieldHp;
    public int ShieldExpiresRound;
    public double MoveMultNextTurn = 1.0;
    public int StunnedTurns;
    public Vec2 LastTravelDir = new(0, 1);

    public bool IsAlive => Hp > 0;

    public Actor Clone()
    {
        var c = (Actor)MemberwiseClone();
        c.PiercedIds = new List<string>(PiercedIds);
        return c;
    }
}

public readonly struct LaunchInput
{
    public double DirX { get; init; }
    public double DirY { get; init; }
    public double DrawRatio { get; init; }
}

public enum SimEventKind
{
    RoundStart,
    TurnStart,
    Launch,
    WallBounce,
    Hit,
    Death,
    Stopped,
    WeaponUltimate,
    ArmorUltimate,
    Heal,
    Shield,
    BattleEnd,
}

/// <summary>One flat struct instead of a class hierarchy: the sim appends events
/// every tick, and a struct in a List keeps that allocation-free.</summary>
public struct SimEvent
{
    public SimEventKind Kind;
    public string ActorId;   // acting actor, or damage source for Hit
    public string TargetId;  // Hit only
    public Vec2 Pos;
    public Vec2 Dir;         // Launch/WeaponUltimate direction, WallBounce normal
    public double Amount;    // damage / heal / shield / bounce speed / drawRatio
    public double Absorbed;  // Hit only: portion eaten by a shield
    public HitKind HitKind;
    public int Round;
    public Team Winner;
    public EndReason Reason;
    public WeaponUltimateSpec? WeaponUlt;
    public ArmorUltimateSpec? ArmorUlt;
}

public readonly struct ContactInfo
{
    public Vec2 Pos { get; init; }    // contact point
    public Vec2 Normal { get; init; } // from target toward self
}

/// <summary>What the integrator does after a weapon behavior resolves a contact.</summary>
public readonly struct ContactResult
{
    public bool Deflect { get; init; }
    public bool Stop { get; init; }
}

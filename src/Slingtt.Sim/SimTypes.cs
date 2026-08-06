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

    /// <summary>Prompt 7 — enemy-only. Passes through every contact undamaged
    /// and undeflected (Weapons.TrailBehavior); the real threat is the toxic
    /// hazard it drops along its path (Hazards.MaybeDropTrail).</summary>
    Trail,

    /// <summary>Prompt 7 — enemy-only. Elastic-bounces like Sword but halts
    /// outright after its second hero contact (Weapons.ChainBehavior) — the
    /// "aims for one hero, then the other" AI lives in EnemyAi.</summary>
    Chain,
}

public enum Phase
{
    Aiming,
    Travelling,
    Ultimate,

    /// <summary>Live-iteration rework — weapon ultimates now spawn real,
    /// slow-traveling projectiles (Projectiles.cs) instead of resolving
    /// instantly. The sim sits in this phase, ticking Projectiles.Advance,
    /// until every spawned projectile has hit or expired.</summary>
    UltimateTravel,

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

    /// <summary>Prompt 7 — a toxic trail hazard hit. Uses the standard contact
    /// cooldown like Contact (not exempt like Aoe/Ultimate), but each hazard
    /// point can only ever hit a given victim once regardless — see Hazard.</summary>
    Hazard,
}

public enum WeaponUltKind
{
    /// <summary>Sword. A volley of bullets fired in the hero's landing
    /// direction — bullet count scales with rarity, direction count with
    /// evolution tier. Each bullet hits one enemy only, but for more damage
    /// than a single Grenade/Boomerang hit, since it can never multi-hit.</summary>
    Striker,

    /// <summary>Lance. Flies out in the landing direction; on reaching its
    /// range, delivers one fan-shaped hit (multi-target, no return pass).
    /// Range scales with rarity, fan angle with evolution tier.</summary>
    Boomerang,

    /// <summary>Hammer. Flies at the current furthest living enemy; explodes
    /// on arrival, dealing AoE damage (multi-target) to everyone in range.
    /// AoE radius scales with rarity.</summary>
    Grenade,
}

public enum ArmorUltKind
{
    Bulwark,
    Swift,
    Vital,
}

/// <summary>Prompt 6 — the archetype IS the trait selector: Heavy grants
/// Siphon (Damage.Apply), Light grants Carry (Ultimates.FireWeaponUltimate).
/// Balanced has no trait of its own — a later live-iteration request removed
/// its former Focus aim-preview bonus, so it's now the neutral middle choice
/// by design, not just by default. Balanced still sorts first so an unset/
/// default ArmorStats reads as that neutral archetype.</summary>
public enum ArmorArchetype
{
    Balanced,
    Heavy,
    Light,
}

public enum EndReason
{
    Elimination,
    TurnLimit,
}

/// <summary>Discriminated-union stand-in for the TS WeaponUltimateSpec. All
/// fields below are pre-resolved once by BattleSetup from the wielding item's
/// level and rarity — the sim itself never computes rarity/level math, it
/// just reads what it's told, same determinism posture as everything else
/// here. Only the fields relevant to Kind are meaningful; the rest sit at
/// their default, same convention ArmorUltimateSpec already uses.
///
/// Striker: BulletCount (rarity-scaled) fired in each of DirectionCount
/// (evolution-tier-scaled) directions, direction 0 always the exact landing
/// direction.
/// Grenade: AoeRadius (rarity-scaled) around wherever it lands, on the
/// current furthest living enemy.
/// Boomerang: FanRange (rarity-scaled) and FanHalfAngleRadians
/// (evolution-tier-scaled, ramps 1..30 level, capped) define the one fan hit
/// it delivers on arrival.
/// DualActivation — true only when the wielding item's rarity is Legendary:
/// the whole thing fires again, delayed, after the first wave is spawned.</summary>
public readonly struct WeaponUltimateSpec
{
    public WeaponUltKind Kind { get; init; }
    public double DmgMult { get; init; }
    public int StunTurns { get; init; }
    public bool DualActivation { get; init; }

    // Striker
    public int BulletCount { get; init; }
    public int DirectionCount { get; init; }

    // Grenade
    public double AoeRadius { get; init; }

    // Boomerang
    public double FanRange { get; init; }
    public double FanHalfAngleRadians { get; init; }
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

    /// <summary>Prompt 7 — enemy-only "marker" behaviour: after this actor's
    /// travel/contact resolves, it spits at the farthest active hero and marks
    /// them (MarkerSpit.FireIfApplicable, called from the Ultimate phase
    /// alongside Ultimates.FireWeaponUltimate — heroes never set this).</summary>
    public bool HasMarkerSpit { get; init; }
}

public sealed class ArmorStats
{
    public int MoveDurationTicks { get; init; }
    public int Tier { get; init; }
    public ArmorUltimateSpec? Ultimate { get; init; }

    /// <summary>Prompt 6 — pre-resolved by BattleSetup from the item's authored
    /// archetype; the sim never looks at content, it just reads which trait this
    /// wearer has.</summary>
    public ArmorArchetype Archetype { get; init; }
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

    /// <summary>Prompt 4 — party of 2 active / 1 benched. A benched hero takes no
    /// turns (TurnOrder skips it), can't be hit (Collision/Ultimates skip it as a
    /// target, EnemyAi never aims at it), and regenerates HP each turn instead.
    /// Never true for enemies.</summary>
    public bool IsBenched;

    /// <summary>Prompt 5 — a single sling shot can only proc the contact combo
    /// once even if its flight path re-crosses the teammate (wall bounces,
    /// lingering overlap across several ticks). Reset on every launch.</summary>
    public bool ComboFiredThisTravel;

    /// <summary>Prompt 7 — turns left under a Marker enemy's mark. Ticks down
    /// once every turn-grant (TurnOrder), any actor's turn, same cadence as
    /// bench regen/combo decay/Siphon's reset — not tied to the marked hero's
    /// own turns, since the mark's actual effect (blocking Prompt 5's contact
    /// combo) is checked on the OTHER hero's turns, whenever they're the one
    /// travelling and might touch this marked one.</summary>
    public int MarkedTurns;

    /// <summary>A marked hero can never be the target of a Prompt 5 contact
    /// combo — Combo.CheckContact checks this on the stationary teammate.</summary>
    public bool IsMarked => MarkedTurns > 0;

    /// <summary>Prompt 7 — Trail weapon type: ticks left before the next hazard
    /// point drops. 0 on a fresh launch so the first travel tick always drops
    /// one immediately.</summary>
    public int TrailDropCooldownTicks;

    /// <summary>Prompt 7 — boss "splits on death": pre-resolved spawn blueprints
    /// for the actors this one becomes when it dies (Damage.Apply). Null for
    /// everything except split-capable bosses.</summary>
    public List<ActorInit>? SplitOnDeath;

    /// <summary>Prompt 6 — Carry (Light archetype): accumulated path length
    /// (not straight-line displacement — every sub-tick delta, so wall bounces
    /// count) travelled this launch. Reset on every launch, read when the
    /// wielder's ultimate fires, whatever triggered it.</summary>
    public double DistanceTravelledThisTravel;

    /// <summary>Prompt 6 — Siphon (Heavy archetype): cumulative lifesteal
    /// already granted this turn-window, capped by SimConfig.SiphonCapPctMaxHp.
    /// Reset every turn (TurnOrder), not every launch — a combo-triggered
    /// arrival ultimate on a hero mid-bench-wait still shares the same cap.</summary>
    public double SiphonHealedThisTurn;

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
    Swap, // Prompt 4: ActorId = outgoing (benched) hero, TargetId = incoming (activated) hero
    ComboContact, // Prompt 5: ActorId = toucher, TargetId = touched hero, Amount = new stack count
    MarkApplied, // Prompt 7: ActorId = marker enemy, TargetId = marked hero
    EnemySplit, // Prompt 7: ActorId = the boss that died, TargetId = id of the spawned split, Pos = spawn position

    /// <summary>Prompt 10 — emitted unconditionally (unlike Hit, never gated by
    /// World.DamageEnabled) whenever a Sword-type contact deflects off an
    /// opposing actor, exactly like WallBounce's existing unconditional
    /// emission. That's what lets Predict.Trajectory's aim-prediction clone
    /// see enemy bounces even with damage disabled.</summary>
    EnemyBounce,
}

/// <summary>One flat struct instead of a class hierarchy: the sim appends events
/// every tick, and a struct in a List keeps that allocation-free.</summary>
public struct SimEvent
{
    public SimEventKind Kind;
    public string ActorId;   // acting actor, or damage source for Hit
    public string TargetId;  // Hit, Swap, ComboContact, MarkApplied, EnemySplit
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

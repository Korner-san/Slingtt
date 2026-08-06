namespace Slingtt.Sim;

// Weapon ultimates fire in the Ultimate phase, after travel fully resolves, from
// the actor's final position. Prompt 3 layers escalation on top of Prompt 1's
// base shape query, all pre-resolved onto WeaponUltimateSpec by BattleSetup (the
// sim never computes rarity/level math itself — it just fires the numbers it's
// handed, same determinism posture as everything else):
//
//  - a SWEEP pass: the shape rotated clockwise by SweepDegrees (and
//    counter-clockwise too, if SweepBidirectional — evolution-gate bonus), at
//    SweepDamageMult damage, hitting only targets the base activation missed,
//    as its own timeline beat(s) at least SweepMinBeatOffsetSeconds after the
//    base activation's;
//  - for Aftershock specifically, "sweep" is an additional larger concentric
//    ring instead of a rotation — a ring has no meaningful angle to rotate —
//    with the same damage/exclusion/beat-timing rules, and a second still-
//    larger ring at the evolution-gate bonus instead of a second direction;
//  - DUAL ACTIVATION (Legendary rarity only): a second full-strength,
//    unfiltered firing of the base shape after the sweep resolves — a genuinely
//    independent second activation, not a supplementary pass.
//
// Targets are always visited in stable actor order so variance draws stay
// deterministic.
public static class Ultimates
{
    private const double HitBeatStaggerSeconds = 0.05;
    private const double AftershockBonusRingScale = 1.3; // matches balance.json's ultimateEscalation.aftershockBonusRingScale

    /// <summary>scaleMultiplier composes with the shape's already-resolved
    /// rarity scale (Prompt 3) — 1.0 for a normal firing, 1.25 for Prompt 5's
    /// contact combo. It's a pure geometry multiplier: damage, sweep degrees,
    /// and dual activation are untouched by it.</summary>
    public static void FireWeaponUltimate(World world, SimConfig cfg, Actor self, double scaleMultiplier = 1.0)
    {
        if (self.Weapon.Ultimate is not { } spec)
        {
            return;
        }

        // Matches the pre-Prompt-1 fallback exactly: a zero LastTravelDir
        // (should never happen — Actor defaults it to (0,1) and launch always
        // normalizes it) divides by 1 rather than 0, leaving it (0,0).
        double dirLen = self.LastTravelDir.Length();
        Vec2 baseDir = self.LastTravelDir * (1.0 / (dirLen == 0 ? 1 : dirLen));

        // Prompt 6 — Carry (Light archetype): the wielder's own last-recorded
        // travel distance adds further scale, on top of whatever scaleMultiplier
        // the caller already applied (rarity's own scale is baked into
        // spec.Shape well before this point). Whatever fired this ultimate —
        // the wielder's own turn, a swap arrival, a Prompt 5 contact-combo
        // proc — reads whatever DistanceTravelledThisTravel currently holds.
        double carryBonus = self.Armor?.Archetype == ArmorArchetype.Light
            ? Math.Min(cfg.CarryMaxBonus, self.DistanceTravelledThisTravel * cfg.CarryScalePerDistanceUnit)
            : 0;
        double totalScale = scaleMultiplier * (1 + carryBonus);

        ShapeDef shape = totalScale == 1.0
            ? spec.Shape
            : spec.Shape with { Width = spec.Shape.Width * totalScale, Radius = spec.Shape.Radius * totalScale };

        var timeline = new ResolutionTimeline();
        world.Events.Add(new SimEvent
        {
            Kind = SimEventKind.WeaponUltimate,
            ActorId = self.Id,
            WeaponUlt = spec with { Shape = shape },
            Pos = self.Pos,
            Dir = baseDir,
            Timeline = timeline,
        });

        double timeCursor = 0;

        // Base activation — full shape, full damage, everything in range.
        var baseHitIds = new HashSet<string>();
        FireShapeQuery(world, cfg, self, spec, shape, baseDir, spec.DmgMult,
            exclude: null, applyStun: true, timeline, ref timeCursor, baseHitIds);

        // Sweep / concentric-ring escalation — reduced damage, never re-hits a
        // target the base activation already caught.
        if (spec.SweepDegrees > 0 || spec.Kind == WeaponUltKind.Aftershock)
        {
            timeCursor = Math.Max(timeCursor, cfg.SweepMinBeatOffsetSeconds);
            var excluded = new HashSet<string>(baseHitIds);
            double sweepDamage = spec.DmgMult * cfg.SweepDamageMult;

            if (spec.Kind == WeaponUltKind.Aftershock)
            {
                ShapeDef ring1 = shape with { Radius = shape.Radius * AftershockBonusRingScale };
                FireShapeQuery(world, cfg, self, spec, ring1, baseDir, sweepDamage,
                    excluded, applyStun: false, timeline, ref timeCursor, excluded);

                if (spec.SweepBidirectional)
                {
                    ShapeDef ring2 = shape with { Radius = shape.Radius * AftershockBonusRingScale * AftershockBonusRingScale };
                    FireShapeQuery(world, cfg, self, spec, ring2, baseDir, sweepDamage,
                        excluded, applyStun: false, timeline, ref timeCursor, excluded);
                }
            }
            else
            {
                double sweepRad = spec.SweepDegrees * Math.PI / 180.0;
                ShapeDef clockwise = shape with { RotationOffset = shape.RotationOffset - sweepRad };
                FireShapeQuery(world, cfg, self, spec, clockwise, baseDir, sweepDamage,
                    excluded, applyStun: false, timeline, ref timeCursor, excluded);

                if (spec.SweepBidirectional)
                {
                    ShapeDef counterClockwise = shape with { RotationOffset = shape.RotationOffset + sweepRad };
                    FireShapeQuery(world, cfg, self, spec, counterClockwise, baseDir, sweepDamage,
                        excluded, applyStun: false, timeline, ref timeCursor, excluded);
                }
            }
        }

        // Dual activation — Legendary only.
        if (spec.DualActivation)
        {
            timeCursor = Math.Max(timeCursor, cfg.SweepMinBeatOffsetSeconds * 2);
            FireShapeQuery(world, cfg, self, spec, shape, baseDir, spec.DmgMult,
                exclude: null, applyStun: true, timeline, ref timeCursor, new HashSet<string>());
        }

        ClampToBudget(timeline, cfg.TimelineBudgetSeconds);
    }

    /// <summary>Prompt 11 — rescales every beat's OffsetSeconds down together so
    /// the whole timeline (base + sweep/rings + dual activation, all of it — one
    /// shared budget, not one per wave) never exceeds cfg.TimelineBudgetSeconds.
    /// A no-op when the raw stagger already fits. Beats stay in the same relative
    /// order and proportion, just compressed, so more targets naturally means a
    /// tighter per-hit gap instead of the whole sequence running long.</summary>
    private static void ClampToBudget(ResolutionTimeline timeline, double budgetSeconds)
    {
        if (timeline.Beats.Count == 0)
        {
            return;
        }
        double lastOffset = timeline.Beats[^1].OffsetSeconds;
        if (lastOffset <= budgetSeconds || lastOffset <= 0)
        {
            return;
        }
        double scale = budgetSeconds / lastOffset;
        for (int i = 0; i < timeline.Beats.Count; i++)
        {
            timeline.Beats[i] = timeline.Beats[i] with { OffsetSeconds = timeline.Beats[i].OffsetSeconds * scale };
        }
    }

    /// <summary>One shape query: a "shape" telegraph beat at timeCursor, then a
    /// "hit" beat (staggered by HitBeatStaggerSeconds) per target actually
    /// damaged. Advances timeCursor past the last beat it added. hitIds
    /// accumulates who got hit — callers reuse the same set as both `exclude`
    /// and `hitIds` across a wave's multiple calls (e.g. Aftershock's two
    /// rings) so later calls in the same wave don't re-hit earlier ones.</summary>
    private static void FireShapeQuery(
        World world, SimConfig cfg, Actor self, WeaponUltimateSpec spec, ShapeDef shape, Vec2 baseDir,
        double dmgMult, HashSet<string>? exclude, bool applyStun,
        ResolutionTimeline timeline, ref double timeCursor, HashSet<string> hitIds)
    {
        timeline.Beats.Add(new TimelineBeat { OffsetSeconds = timeCursor, Kind = "shape" });
        double hitCursor = timeCursor + HitBeatStaggerSeconds;

        // Prompt 7 — snapshot before iterating: a hit here can kill a
        // split-capable boss, and Damage.Apply spawns its children straight
        // into world.Actors. Enumerating the live list while that happens
        // throws; a snapshot also correctly keeps the newly spawned children
        // out of this same shape query's damage pass.
        foreach (Actor target in world.Actors.ToArray())
        {
            if (target.Team == self.Team || !target.IsAlive || target.IsBenched)
            {
                continue;
            }
            if (exclude != null && exclude.Contains(target.Id))
            {
                continue;
            }
            if (!ShapeResolver.Overlaps(shape, self.Pos, baseDir, target))
            {
                continue;
            }

            double? dealt = Damage.Apply(world, cfg, self, target, dmgMult, HitKind.Ultimate, target.Pos);
            if (dealt is null)
            {
                continue;
            }

            hitIds.Add(target.Id);
            timeline.Beats.Add(new TimelineBeat { OffsetSeconds = hitCursor, Kind = "hit", TargetId = target.Id });
            hitCursor += HitBeatStaggerSeconds;

            if (applyStun && spec.Kind == WeaponUltKind.Aftershock && spec.StunTurns > 0 && target.IsAlive)
            {
                target.StunnedTurns = Math.Max(target.StunnedTurns, spec.StunTurns);
            }
        }

        timeCursor = hitCursor;
    }
}

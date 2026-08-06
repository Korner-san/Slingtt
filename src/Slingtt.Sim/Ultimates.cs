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

    public static void FireWeaponUltimate(World world, SimConfig cfg, Actor self)
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

        var timeline = new ResolutionTimeline();
        world.Events.Add(new SimEvent
        {
            Kind = SimEventKind.WeaponUltimate,
            ActorId = self.Id,
            WeaponUlt = spec,
            Pos = self.Pos,
            Dir = baseDir,
            Timeline = timeline,
        });

        double timeCursor = 0;

        // Base activation — full shape, full damage, everything in range.
        var baseHitIds = new HashSet<string>();
        FireShapeQuery(world, cfg, self, spec, spec.Shape, baseDir, spec.DmgMult,
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
                ShapeDef ring1 = spec.Shape with { Radius = spec.Shape.Radius * AftershockBonusRingScale };
                FireShapeQuery(world, cfg, self, spec, ring1, baseDir, sweepDamage,
                    excluded, applyStun: false, timeline, ref timeCursor, excluded);

                if (spec.SweepBidirectional)
                {
                    ShapeDef ring2 = spec.Shape with { Radius = spec.Shape.Radius * AftershockBonusRingScale * AftershockBonusRingScale };
                    FireShapeQuery(world, cfg, self, spec, ring2, baseDir, sweepDamage,
                        excluded, applyStun: false, timeline, ref timeCursor, excluded);
                }
            }
            else
            {
                double sweepRad = spec.SweepDegrees * Math.PI / 180.0;
                ShapeDef clockwise = spec.Shape with { RotationOffset = spec.Shape.RotationOffset - sweepRad };
                FireShapeQuery(world, cfg, self, spec, clockwise, baseDir, sweepDamage,
                    excluded, applyStun: false, timeline, ref timeCursor, excluded);

                if (spec.SweepBidirectional)
                {
                    ShapeDef counterClockwise = spec.Shape with { RotationOffset = spec.Shape.RotationOffset + sweepRad };
                    FireShapeQuery(world, cfg, self, spec, counterClockwise, baseDir, sweepDamage,
                        excluded, applyStun: false, timeline, ref timeCursor, excluded);
                }
            }
        }

        // Dual activation — Legendary only.
        if (spec.DualActivation)
        {
            timeCursor = Math.Max(timeCursor, cfg.SweepMinBeatOffsetSeconds * 2);
            FireShapeQuery(world, cfg, self, spec, spec.Shape, baseDir, spec.DmgMult,
                exclude: null, applyStun: true, timeline, ref timeCursor, new HashSet<string>());
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

        foreach (Actor target in world.Actors)
        {
            if (target.Team == self.Team || !target.IsAlive)
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

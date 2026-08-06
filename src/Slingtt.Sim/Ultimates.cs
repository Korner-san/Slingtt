namespace Slingtt.Sim;

// Weapon ultimates fire in the Ultimate phase, after travel fully resolves, from
// the actor's final position. They are instantaneous shape queries, not moving
// projectiles. Geometry lives in WeaponUltimateSpec.Shape and is evaluated by
// the shared ShapeResolver (Shapes.cs) — this file is now just: resolve the
// shape, apply damage to what it caught, handle Aftershock's stun, and record a
// ResolutionTimeline for the render layer. Targets are visited in stable actor
// order so variance draws stay deterministic.
public static class Ultimates
{
    // Stagger between per-target "hit" beats in the emitted timeline. Nothing
    // consumes this yet (see Timeline.cs) — established for later prompts.
    private const double HitBeatStaggerSeconds = 0.05;

    public static void FireWeaponUltimate(World world, SimConfig cfg, Actor self)
    {
        if (self.Weapon.Ultimate is not { } spec)
        {
            return;
        }

        // Matches the pre-refactor fallback exactly: a zero LastTravelDir (should
        // never happen in practice — Actor defaults it to (0,1) and launch always
        // normalizes it) divides by 1 rather than 0, which leaves it (0,0) rather
        // than substituting some other direction.
        double dirLen = self.LastTravelDir.Length();
        Vec2 baseDir = self.LastTravelDir * (1.0 / (dirLen == 0 ? 1 : dirLen));

        var timeline = new ResolutionTimeline();
        timeline.Beats.Add(new TimelineBeat { OffsetSeconds = 0, Kind = "shape" });

        world.Events.Add(new SimEvent
        {
            Kind = SimEventKind.WeaponUltimate,
            ActorId = self.Id,
            WeaponUlt = spec,
            Pos = self.Pos,
            Dir = baseDir,
            Timeline = timeline,
        });

        foreach (Actor target in world.Actors)
        {
            if (target.Team == self.Team || !target.IsAlive)
            {
                continue;
            }
            if (!ShapeResolver.Overlaps(spec.Shape, self.Pos, baseDir, target))
            {
                continue;
            }

            double? dealt = Damage.Apply(world, cfg, self, target, spec.DmgMult, HitKind.Ultimate, target.Pos);
            if (dealt is null)
            {
                continue;
            }

            timeline.Beats.Add(new TimelineBeat
            {
                OffsetSeconds = HitBeatStaggerSeconds * (timeline.Beats.Count),
                Kind = "hit",
                TargetId = target.Id,
            });

            if (spec.Kind == WeaponUltKind.Aftershock && spec.StunTurns > 0 && target.IsAlive)
            {
                target.StunnedTurns = Math.Max(target.StunnedTurns, spec.StunTurns);
            }
        }
    }
}

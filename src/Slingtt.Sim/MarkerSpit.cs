namespace Slingtt.Sim;

/// <summary>Prompt 7 — Marker enemies: after their travel/contact resolves
/// (same Ultimate phase as Ultimates.FireWeaponUltimate — enemies never have
/// a weapon ultimate of their own, so the two never actually fire for the
/// same actor), they spit at the farthest active hero and mark them, blocking
/// that hero from ever being the target of a Prompt 5 contact combo for
/// SimConfig.MarkDurationTurns.</summary>
public static class MarkerSpit
{
    public static void FireIfApplicable(World world, SimConfig cfg, Actor self)
    {
        if (!self.Weapon.HasMarkerSpit)
        {
            return;
        }

        Actor? farthest = null;
        double best = -1;
        foreach (Actor h in world.Actors)
        {
            if (h.Team != Team.Hero || !h.IsAlive || h.IsBenched)
            {
                continue;
            }
            double d = self.Pos.DistanceTo(h.Pos);
            if (d > best)
            {
                best = d;
                farthest = h;
            }
        }
        if (farthest is null)
        {
            return;
        }

        farthest.MarkedTurns = cfg.MarkDurationTurns;
        world.Events.Add(new SimEvent
        {
            Kind = SimEventKind.MarkApplied,
            ActorId = self.Id,
            TargetId = farthest.Id,
            Pos = farthest.Pos,
        });
    }
}

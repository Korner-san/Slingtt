namespace Slingtt.Sim;

// Prompt 5 — touching your other active hero mid-travel procs a "contact
// combo": the touched hero fires their own weapon ultimate from where they
// stand at 1.25x scale (Ultimates.FireWeaponUltimate's scaleMultiplier), and a
// global stack counter ticks up. The counter buffs every active hero's
// outgoing damage (+6%/stack, capped at 10 — see Damage.Apply) and decays by
// one stack every turn (TurnOrder.AdvanceRoundRobin) that ends without a
// proc. It lives on World, not on any Actor, so it survives Prompt 4 swaps
// untouched. Blocked entirely if the touched hero is marked (Prompt 7).
public static class Combo
{
    public const int MaxStacks = 10;
    public const double DamagePerStack = 0.06;
    private const double ScaleMultiplier = 1.25;

    public static double DamageMultiplier(World world) => 1 + world.ComboStacks * DamagePerStack;

    /// <summary>Checked once per tick while a hero travels. No-ops once this
    /// travel has already procced (ComboFiredThisTravel), if there's no living
    /// unbenched teammate to touch, if that teammate is marked, or if the two
    /// aren't actually overlapping yet.</summary>
    public static void CheckContact(World world, SimConfig cfg, Actor self)
    {
        if (self.ComboFiredThisTravel)
        {
            return;
        }

        Actor? teammate = FindOtherActiveHero(world, self);
        if (teammate is null || teammate.IsMarked)
        {
            return;
        }

        double dx = self.Pos.X - teammate.Pos.X;
        double dy = self.Pos.Y - teammate.Pos.Y;
        double radiusSum = self.Radius + teammate.Radius;
        if (dx * dx + dy * dy > radiusSum * radiusSum)
        {
            return; // not touching
        }

        self.ComboFiredThisTravel = true;
        world.ComboContactThisTurn = true;
        world.ComboStacks = Math.Min(MaxStacks, world.ComboStacks + 1);

        world.Events.Add(new SimEvent
        {
            Kind = SimEventKind.ComboContact,
            ActorId = self.Id,
            TargetId = teammate.Id,
            Pos = teammate.Pos,
            Amount = world.ComboStacks,
        });

        Ultimates.FireWeaponUltimate(world, cfg, teammate, ScaleMultiplier);
    }

    private static Actor? FindOtherActiveHero(World world, Actor self)
    {
        foreach (Actor a in world.Actors)
        {
            if (a.Team == Team.Hero && a.Id != self.Id && a.IsAlive && !a.IsBenched)
            {
                return a;
            }
        }
        return null;
    }
}

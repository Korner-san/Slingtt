namespace Slingtt.Sim;

/// <summary>One implementation per weapon type. Adding a fourth weapon means
/// adding a class here, not editing the integrator. The behavior applies damage;
/// the integrator applies the physics the returned ContactResult asks for.</summary>
public interface IWeaponBehavior
{
    ContactResult OnEnemyContact(World world, SimConfig cfg, Actor self, Actor target, ContactInfo hit);
}

/// <summary>Sword: elastic bounce off the enemy; damage decays multiplicatively
/// per contact this travel, floored — the decay is what stops a lucky pinball
/// between two enemies from one-shotting a floor.</summary>
public sealed class SwordBehavior : IWeaponBehavior
{
    public ContactResult OnEnemyContact(World world, SimConfig cfg, Actor self, Actor target, ContactInfo hit)
    {
        double mult = Math.Max(
            Math.Pow(1 - self.Weapon.BounceDecay, self.HitsThisTravel),
            cfg.SwordFloorMult);
        double? dealt = Damage.Apply(world, cfg, self, target, mult, HitKind.Contact, hit.Pos);
        if (dealt is not null)
        {
            self.HitsThisTravel += 1;
        }
        return new ContactResult { Deflect = true, Stop = false };
    }
}

/// <summary>Lance: passes through without deflection. A target can't be pierced
/// again until the lance fully leaves its contact radius (PiercedIds, maintained
/// by the integrator). After PierceCount pierces, further contacts deal no damage
/// but still don't deflect. Walls bounce normally.</summary>
public sealed class LanceBehavior : IWeaponBehavior
{
    public ContactResult OnEnemyContact(World world, SimConfig cfg, Actor self, Actor target, ContactInfo hit)
    {
        if (!self.PiercedIds.Contains(target.Id))
        {
            self.PiercedIds.Add(target.Id);
            if (self.PierceBudgetUsed < self.Weapon.PierceCount)
            {
                double? dealt = Damage.Apply(world, cfg, self, target, cfg.LancePierceMult, HitKind.Pierce, hit.Pos);
                if (dealt is not null)
                {
                    self.PierceBudgetUsed += 1;
                    self.HitsThisTravel += 1;
                }
            }
        }
        return new ContactResult { Deflect = false, Stop = false };
    }
}

/// <summary>Hammer: stop dead on first contact, then detonate. Direct hit and AOE
/// are separate events so VFX/audio can layer them. AOE falls off linearly from
/// center to rim and bypasses the contact cooldown.</summary>
public sealed class HammerBehavior : IWeaponBehavior
{
    public ContactResult OnEnemyContact(World world, SimConfig cfg, Actor self, Actor target, ContactInfo hit)
    {
        double? dealt = Damage.Apply(world, cfg, self, target, cfg.HammerDirectMult, HitKind.Contact, hit.Pos);
        if (dealt is not null)
        {
            self.HitsThisTravel += 1;
        }

        double radius = self.Weapon.AoeRadius;
        foreach (Actor enemy in world.Actors)
        {
            if (enemy.Team == self.Team || !enemy.IsAlive)
            {
                continue;
            }
            double d = hit.Pos.DistanceTo(enemy.Pos);
            if (d > radius)
            {
                continue;
            }
            double falloff = d / radius;
            double mult = cfg.HammerAoeCenterMult
                          + (cfg.HammerAoeRimMult - cfg.HammerAoeCenterMult) * falloff;
            Damage.Apply(world, cfg, self, enemy, mult, HitKind.Aoe, enemy.Pos);
        }
        return new ContactResult { Deflect = false, Stop = true };
    }
}

public static class Weapons
{
    private static readonly SwordBehavior Sword = new();
    private static readonly LanceBehavior Lance = new();
    private static readonly HammerBehavior Hammer = new();

    public static IWeaponBehavior Behavior(WeaponType type) => type switch
    {
        WeaponType.Lance => Lance,
        WeaponType.Hammer => Hammer,
        _ => Sword,
    };
}

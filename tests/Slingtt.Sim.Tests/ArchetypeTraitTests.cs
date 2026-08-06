using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 6's sim-layer payload: Siphon (Heavy) and Carry (Light). Damage
// numbers are hand-derived, not RNG-captured: variance pinned to 1.0, so
// raw = Atk*hitMult, mitigated = raw*(1-Def/(Def+DefK)). Atk=100, Def=30,
// DefK=300 -> factor 0.909090... -> 90.909 -> RoundJs -> 91.
public class ArchetypeTraitTests
{
    private static SimConfig Cfg() => new()
    {
        VarianceMin = 1.0,
        VarianceMax = 1.0,
        SiphonRatio = 0.25,
        SiphonCapPctMaxHp = 0.15,
        CarryScalePerDistanceUnit = 0.05,
        CarryMaxBonus = 0.5,
    };

    private static Actor Hero(string id, Vec2 pos, ArmorArchetype archetype, WeaponUltimateSpec? ult = null) => new()
    {
        Id = id,
        Team = Team.Hero,
        Pos = pos,
        Radius = 0.5,
        Hp = 500,
        MaxHp = 1000,
        Def = 0,
        Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 100, Tier = 0, Ultimate = ult },
        Armor = new ArmorStats { Archetype = archetype },
    };

    private static Actor Enemy(string id, Vec2 pos) => new()
    {
        Id = id,
        Team = Team.Enemy,
        Pos = pos,
        Radius = 0.45,
        Hp = 1000,
        MaxHp = 1000,
        Def = 30,
        Weapon = new WeaponStats { Type = WeaponType.Sword, Atk = 50, Tier = 0 },
    };

    private static World BuildWorld(params Actor[] actors)
    {
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20 };
        w.Actors.AddRange(actors);
        return w;
    }

    // --- Siphon --------------------------------------------------------------

    [Fact]
    public void Siphon_HealsAHeavySource_ForARatioOfUltimateDamageDealt()
    {
        Actor source = Hero("source", new Vec2(0, 0), ArmorArchetype.Heavy);
        Actor target = Enemy("target", new Vec2(1, 0));
        World w = BuildWorld(source, target);

        double? dealt = Damage.Apply(w, Cfg(), source, target, hitMult: 1.0, HitKind.Ultimate, target.Pos);

        Assert.Equal(91, dealt);
        Assert.Equal(500 + 22.75, source.Hp); // 91 * 0.25
        Assert.Equal(22.75, source.SiphonHealedThisTurn);
        Assert.Contains(w.Events, e => e.Kind == SimEventKind.Heal && e.ActorId == "source");
    }

    [Fact]
    public void Siphon_NeverTriggers_OnNonUltimateDamage()
    {
        Actor source = Hero("source", new Vec2(0, 0), ArmorArchetype.Heavy);
        Actor target = Enemy("target", new Vec2(1, 0));
        World w = BuildWorld(source, target);

        Damage.Apply(w, Cfg(), source, target, hitMult: 1.0, HitKind.Contact, target.Pos);

        Assert.Equal(500, source.Hp);
        Assert.Equal(0, source.SiphonHealedThisTurn);
    }

    [Theory]
    [InlineData(ArmorArchetype.Balanced)]
    [InlineData(ArmorArchetype.Light)]
    public void Siphon_NeverTriggers_ForNonHeavyArchetypes(ArmorArchetype archetype)
    {
        Actor source = Hero("source", new Vec2(0, 0), archetype);
        Actor target = Enemy("target", new Vec2(1, 0));
        World w = BuildWorld(source, target);

        Damage.Apply(w, Cfg(), source, target, hitMult: 1.0, HitKind.Ultimate, target.Pos);

        Assert.Equal(500, source.Hp);
    }

    [Fact]
    public void Siphon_IsCappedByWhatsLeftOfThePerTurnBudget()
    {
        Actor source = Hero("source", new Vec2(0, 0), ArmorArchetype.Heavy);
        Actor target = Enemy("target", new Vec2(1, 0));
        World w = BuildWorld(source, target);
        SimConfig cfg = Cfg(); // cap = 1000 * 0.15 = 150
        source.SiphonHealedThisTurn = 145; // only 5 left in the budget

        Damage.Apply(w, cfg, source, target, hitMult: 1.0, HitKind.Ultimate, target.Pos); // would normally heal 22.75

        Assert.Equal(500 + 5, source.Hp);
        Assert.Equal(150, source.SiphonHealedThisTurn);
    }

    [Fact]
    public void Siphon_GrantsNoHeal_WhenTheBudgetIsAlreadyExhausted()
    {
        Actor source = Hero("source", new Vec2(0, 0), ArmorArchetype.Heavy);
        Actor target = Enemy("target", new Vec2(1, 0));
        World w = BuildWorld(source, target);
        source.SiphonHealedThisTurn = 150; // cap already fully spent

        Damage.Apply(w, Cfg(), source, target, hitMult: 1.0, HitKind.Ultimate, target.Pos);

        Assert.Equal(500, source.Hp);
    }

    // --- Carry -----------------------------------------------------------------

    private static WeaponUltimateSpec Grenade(double aoeRadius) => new()
    {
        Kind = WeaponUltKind.Grenade,
        DmgMult = 1.0,
        AoeRadius = aoeRadius,
    };

    [Fact]
    public void Carry_ScalesTheUltimateShape_ByDistanceTravelled()
    {
        Actor self = Hero("self", new Vec2(5, 5), ArmorArchetype.Light, Grenade(2.0));
        self.DistanceTravelledThisTravel = 4; // carryBonus = min(0.5, 4*0.05) = 0.2
        World w = BuildWorld(self);

        Ultimates.FireWeaponUltimate(w, Cfg(), self);

        SimEvent ult = w.Events.Single(e => e.Kind == SimEventKind.WeaponUltimate);
        Assert.Equal(2.4, ult.WeaponUlt!.Value.AoeRadius, precision: 6); // 2.0 * 1.2
    }

    [Fact]
    public void Carry_CapsAtCarryMaxBonus()
    {
        Actor self = Hero("self", new Vec2(5, 5), ArmorArchetype.Light, Grenade(2.0));
        self.DistanceTravelledThisTravel = 20; // carryBonus = min(0.5, 20*0.05=1.0) = 0.5, capped
        World w = BuildWorld(self);

        Ultimates.FireWeaponUltimate(w, Cfg(), self);

        SimEvent ult = w.Events.Single(e => e.Kind == SimEventKind.WeaponUltimate);
        Assert.Equal(3.0, ult.WeaponUlt!.Value.AoeRadius, precision: 6); // 2.0 * 1.5
    }

    [Fact]
    public void Carry_ComposesMultiplicatively_WithThePromptFiveComboScale()
    {
        Actor self = Hero("self", new Vec2(5, 5), ArmorArchetype.Light, Grenade(2.0));
        self.DistanceTravelledThisTravel = 4; // carryBonus = 0.2
        World w = BuildWorld(self);

        Ultimates.FireWeaponUltimate(w, Cfg(), self, scaleMultiplier: 1.25);

        SimEvent ult = w.Events.Single(e => e.Kind == SimEventKind.WeaponUltimate);
        Assert.Equal(3.0, ult.WeaponUlt!.Value.AoeRadius, precision: 6); // 2.0 * 1.25 * 1.2
    }

    [Theory]
    [InlineData(ArmorArchetype.Balanced)]
    [InlineData(ArmorArchetype.Heavy)]
    public void Carry_NeverApplies_ForNonLightArchetypes(ArmorArchetype archetype)
    {
        Actor self = Hero("self", new Vec2(5, 5), archetype, Grenade(2.0));
        self.DistanceTravelledThisTravel = 20; // would be a big bonus if this were Light
        World w = BuildWorld(self);

        Ultimates.FireWeaponUltimate(w, Cfg(), self);

        SimEvent ult = w.Events.Single(e => e.Kind == SimEventKind.WeaponUltimate);
        Assert.Equal(2.0, ult.WeaponUlt!.Value.AoeRadius, precision: 6); // unscaled
    }

    // --- TurnOrder housekeeping -------------------------------------------------

    [Fact]
    public void TurnOrder_ResetsSiphonBudget_EveryTurn()
    {
        Actor active1 = Hero("active1", new Vec2(2, 2), ArmorArchetype.Heavy);
        active1.SiphonHealedThisTurn = 100;
        Actor active2 = Hero("active2", new Vec2(4, 2), ArmorArchetype.Balanced);
        Actor enemy = Enemy("enemy", new Vec2(2, 8));
        var w = new World { Rng = new RngState(1), BoundsW = 20, BoundsH = 20 };
        w.Actors.Add(active1);
        w.Actors.Add(active2);
        w.Actors.Add(enemy);

        TurnOrder.AdvanceRoundRobin(w, Cfg());

        Assert.Equal(0, active1.SiphonHealedThisTurn);
    }
}

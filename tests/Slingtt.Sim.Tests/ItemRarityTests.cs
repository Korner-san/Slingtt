using Slingtt.Game;
using Slingtt.Sim;
using Xunit;

namespace Slingtt.Sim.Tests;

// Prompt 2 — five-rarity vocabulary (Common/Uncommon/Rare/Epic/Legendary)
// replacing R/SR/SSR, item pool expanded to the designed shape, rarity now
// actually multiplies stats (it didn't before — Rarity was purely a display
// string). No escalation behaviour here; that's later prompts.
public class ItemRarityTests
{
    private static readonly string[] ValidRarities = { "Common", "Uncommon", "Rare", "Epic", "Legendary" };

    [Fact]
    public void ItemPool_MatchesTheDesignedShape()
    {
        Content c = Content.Load();

        Assert.Equal(24, c.Weapons.Count);
        Assert.Equal(30, c.Armor.Count);
    }

    [Fact]
    public void EveryWeaponAndArmor_UsesAValidFiveTierRarity()
    {
        Content c = Content.Load();

        foreach (WeaponDef w in c.Weapons.Values)
        {
            Assert.Contains(w.Rarity, ValidRarities);
        }
        foreach (ArmorDef a in c.Armor.Values)
        {
            Assert.Contains(a.Rarity, ValidRarities);
        }
    }

    [Fact]
    public void EachWeaponType_HasEightItemsAcrossAllFiveRarities()
    {
        Content c = Content.Load();

        foreach (string type in new[] { "sword", "lance", "hammer" })
        {
            List<WeaponDef> ofType = c.Weapons.Values.Where(w => w.Type == type).ToList();
            Assert.Equal(8, ofType.Count);
            foreach (string rarity in ValidRarities)
            {
                Assert.True(ofType.Any(w => w.Rarity == rarity), $"{type} has no {rarity} weapon");
            }
        }
    }

    [Fact]
    public void ItemRarityBalance_ExposesAllFiveMultipliers()
    {
        Content c = Content.Load();
        ItemRarityBalance rarity = c.Balance.ItemRarity;

        Assert.Equal(5, rarity.Order.Count);
        double last = 0;
        foreach (string tier in rarity.Order)
        {
            double mult = rarity.MultiplierFor(tier);
            Assert.True(mult > last, $"{tier}'s multiplier ({mult}) should exceed the previous tier's ({last})");
            last = mult;
        }
        Assert.Equal(1.0, rarity.MultiplierFor("Common"));
    }

    [Fact]
    public void HigherRarityWeapon_HasMoreAtkThanLowerRarityAtTheSameLevel()
    {
        Content c = Content.Load();
        var team = new List<LoadoutSlot>
        {
            new() { HeroId = "hero_bram", WeaponId = "wpn_squire_blade", ArmorId = "arm_squire_mail", WeaponLevel = 1, ArmorLevel = 1 },
        };
        var legendaryTeam = new List<LoadoutSlot>
        {
            new() { HeroId = "hero_bram", WeaponId = "wpn_aurelions_requiem", ArmorId = "arm_squire_mail", WeaponLevel = 1, ArmorLevel = 1 },
        };

        WorldSetup commonSetup = BattleSetup.Build(c, 1, team, seed: 1);
        WorldSetup legendarySetup = BattleSetup.Build(c, 1, legendaryTeam, seed: 1);

        double commonAtk = commonSetup.Actors.First(a => a.Team == Team.Hero).Weapon.Atk;
        double legendaryAtk = legendarySetup.Actors.First(a => a.Team == Team.Hero).Weapon.Atk;

        Assert.True(legendaryAtk > commonAtk,
            $"Legendary Atk ({legendaryAtk}) should exceed Common Atk ({commonAtk}) at the same weapon level");
        // Both Common (x1.0) and Legendary (x2.1) sword base Atk is 100, so this
        // should be exactly the rarity ratio -- confirms the multiplier is wired
        // in, not just "some" difference from unrelated stat noise.
        Assert.Equal(2.1, legendaryAtk / commonAtk, precision: 3);
    }

    [Fact]
    public void HigherRarityArmor_GrantsMoreHpAndDefThanLowerRarityAtTheSameLevel()
    {
        Content c = Content.Load();
        var commonTeam = new List<LoadoutSlot>
        {
            new() { HeroId = "hero_bram", WeaponId = "wpn_squire_blade", ArmorId = "arm_squire_mail", WeaponLevel = 1, ArmorLevel = 1 }, // Common
        };
        var legendaryTeam = new List<LoadoutSlot>
        {
            new() { HeroId = "hero_bram", WeaponId = "wpn_squire_blade", ArmorId = "arm_aegis_of_dawn", WeaponLevel = 1, ArmorLevel = 1 }, // Legendary, same weight class as Squire's Mail
        };

        WorldSetup commonSetup = BattleSetup.Build(c, 1, commonTeam, seed: 1);
        WorldSetup legendarySetup = BattleSetup.Build(c, 1, legendaryTeam, seed: 1);

        ActorInit commonHero = commonSetup.Actors.First(a => a.Team == Team.Hero);
        ActorInit legendaryHero = legendarySetup.Actors.First(a => a.Team == Team.Hero);

        Assert.True(legendaryHero.Hp > commonHero.Hp);
        Assert.True(legendaryHero.Def > commonHero.Def);
    }
}

using Slingtt.Sim;

namespace Slingtt.Game;

/// <summary>Per-actor VISUAL spec, as plain primitives (no engine types) — the
/// render layer turns these into meshes and palette colours. Ids match
/// BattleSetup.Build exactly so the render layer can join on id.</summary>
public sealed class ActorVisual
{
    public string Id { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public Team Team { get; init; }
    public string Generator { get; init; } = "hero"; // hero | enemy | boss
    public string ModelId { get; init; } = "";
    public double Radius { get; init; } = 0.5;
    public int Tier { get; init; }
    public string? Rarity { get; init; } // Common | Uncommon | Rare | Epic | Legendary, null for enemies
    public WeaponType WeaponType { get; init; }
    public string WeaponModelId { get; init; } = "";
}

public static class VisualRoster
{
    public static List<ActorVisual> Build(Content content, int floor, List<LoadoutSlot> team)
    {
        var outp = new List<ActorVisual>();

        foreach (LoadoutSlot slot in team)
        {
            if (!content.Heroes.TryGetValue(slot.HeroId, out HeroDef? hero)
                || !content.Weapons.TryGetValue(slot.WeaponId, out WeaponDef? weapon))
            {
                continue;
            }
            outp.Add(new ActorVisual
            {
                Id = hero.Id,
                DisplayName = content.Name(hero.NameKey),
                Team = Team.Hero,
                Generator = "hero",
                ModelId = hero.ModelId,
                Radius = 0.5,
                Tier = Formulas.EvolutionTier(slot.WeaponLevel),
                Rarity = weapon.Rarity,
                WeaponType = BattleSetup.ParseWeaponType(weapon.Type),
                WeaponModelId = weapon.ModelId,
            });
        }

        FloorDef? floorDef = content.Floor(floor);
        if (floorDef is not null)
        {
            for (int i = 0; i < floorDef.Enemies.Count; i++)
            {
                if (!content.Enemies.TryGetValue(floorDef.Enemies[i].EnemyId, out EnemyDef? def))
                {
                    continue;
                }
                outp.Add(new ActorVisual
                {
                    Id = $"{def.Id}#{i}",
                    DisplayName = content.Name(def.NameKey),
                    Team = Team.Enemy,
                    Generator = def.Kind == "boss" ? "boss" : "enemy",
                    ModelId = def.ModelId,
                    Radius = def.Radius,
                    Tier = 0,
                    Rarity = null,
                    WeaponType = BattleSetup.ParseWeaponType(def.WeaponType),
                    WeaponModelId = def.ModelId,
                });
            }
        }

        return outp;
    }
}

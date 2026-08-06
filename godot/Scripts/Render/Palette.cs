using Godot;

namespace Slingtt.Render;

// The ONLY source of colour in the game — the palette is closed. No mesh factory,
// material, or VFX invents a hue; they pick from these sets by role. Values are
// ported verbatim from the web original's src/art/palette.ts and src/ui/tokens.ts
// so the mobile build and the web build cannot drift into two palettes.
public static class Palette
{
    private static Color Hex(uint rgb) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f);

    // Arena ramp — deep and desaturated, so saturated actors and VFX pop.
    public static readonly Color ArenaFloor = Hex(0x1B1826);
    public static readonly Color ArenaFloorEdge = Hex(0x241F33);
    public static readonly Color ArenaWall = Hex(0x2E2842);
    public static readonly Color ArenaWallTop = Hex(0x3C3654);
    public static readonly Color ArenaGrid = Hex(0x322C48);
    public static readonly Color ArenaFog = Hex(0x14121A);

    // Teams.
    public static readonly Color TeamHero = Hex(0x4A90FF);
    public static readonly Color TeamHeroDark = Hex(0x2B5BBF);
    public static readonly Color TeamEnemy = Hex(0xFF4D6A);
    public static readonly Color TeamEnemyDark = Hex(0xB62F47);

    // Rarity hues — load-bearing: the same hue appears on card, model emissive,
    // pull animation, and results flash.
    public static readonly Color RarityR = Hex(0x8A94B0);
    public static readonly Color RaritySR = Hex(0xB46BFF);
    public static readonly Color RaritySSR = Hex(0xFFC24D);

    // Weapon-type accent, used on the held prop and the actor's trim.
    public static readonly Color WeaponSword = Hex(0xE6ECFF);
    public static readonly Color WeaponLance = Hex(0x66E0FF);
    public static readonly Color WeaponHammer = Hex(0xFFB35C);

    // VFX.
    public static readonly Color VfxImpact = Hex(0xFFFFFF);
    public static readonly Color VfxAoe = Hex(0xFF9D5C);
    public static readonly Color VfxPierce = Hex(0x7FD7FF);
    public static readonly Color VfxUltimate = Hex(0xFFD34D);
    public static readonly Color VfxHeal = Hex(0x7DFF9A);
    public static readonly Color VfxShield = Hex(0x9FB7FF);
    public static readonly Color VfxSpark = Hex(0xFFE9A8);

    /// <summary>Evolution emissive intensity by tier (0..3). Emissive means
    /// progression, so tier 0 is dark and brightness only ever increases.</summary>
    public static readonly float[] EmissiveByTier = { 0.0f, 0.35f, 0.7f, 1.15f };

    // UI tokens, derived from the same closed palette.
    public static readonly Color UiBg = Hex(0x14121A);
    public static readonly Color UiSurface = Hex(0x211C30);
    public static readonly Color UiSurfaceRaised = Hex(0x2B2540);
    public static readonly Color UiBorder = Hex(0x3C3654);
    public static readonly Color UiBorderStrong = Hex(0x4C4568);
    public static readonly Color UiText = Hex(0xF2EEFC);
    public static readonly Color UiTextDim = Hex(0xA79FC4);
    public static readonly Color UiSuccess = Hex(0x54E08A);
    public static readonly Color UiHpHero = Hex(0x54E08A);
    public static readonly Color UiHpEnemy = Hex(0xFF7A5C);

    public static Color Rarity(string? rarity) => rarity switch
    {
        "SSR" => RaritySSR,
        "SR" => RaritySR,
        "R" => RarityR,
        _ => RarityR,
    };

    public static Color Weapon(Slingtt.Sim.WeaponType type) => type switch
    {
        Slingtt.Sim.WeaponType.Lance => WeaponLance,
        Slingtt.Sim.WeaponType.Hammer => WeaponHammer,
        _ => WeaponSword,
    };

    public static Color Resource(Slingtt.Game.ResourceKind kind) => kind switch
    {
        Slingtt.Game.ResourceKind.SlingCores => TeamHero,
        Slingtt.Game.ResourceKind.EvoOre => RaritySR,
        Slingtt.Game.ResourceKind.EvolutionCores => RaritySSR,
        _ => Hex(0xFF9D4D),
    };

    /// <summary>Godot's emission adds more aggressively than three.js's
    /// emissiveIntensity, so the web values are scaled to keep the team colour
    /// readable — an SSR holder should glow, not turn white.</summary>
    private const float EmissiveGain = 0.55f;

    public static float EmissiveFor(int tier)
        => EmissiveByTier[Mathf.Clamp(tier, 0, EmissiveByTier.Length - 1)] * EmissiveGain;
}

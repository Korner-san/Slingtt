using Godot;
using Slingtt.Sim;

namespace Slingtt.Render;

// Procedural placeholder geometry, ported from the web original's
// src/art/generators.ts. Every generator is a PURE function of (seed, params):
// the same input produces the same silhouette on every device and session, so an
// item looks identical everywhere without shipping a single model file.
//
// Godot primitives stand in for the web build's hand-tessellated parts: a low
// RadialSegments count on CapsuleMesh/SphereMesh gives the same faceted low-poly
// read, and a CylinderMesh with TopRadius 0 is a cone. Keeping the art procedural
// is also what lets this build have zero binary asset dependencies — nothing can
// fail to package.
public static class MeshFactory
{
    private static double Jitter(ref RngState rng, double baseValue, double spread)
        => baseValue + (Rng.NextDouble(ref rng) * 2 - 1) * spread;

    /// <summary>Proportions grow ~8% at tier 2+. Tier 0 and 1 stay identical in size.</summary>
    private static float TierScale(int tier) => tier >= 2 ? 1.08f : 1.0f;

    private static uint SeedOf(string modelId)
    {
        // FNV-1a: a stable hash so a model id always yields the same silhouette.
        unchecked
        {
            uint h = 2166136261u;
            foreach (char c in modelId)
            {
                h ^= c;
                h *= 16777619u;
            }
            return h;
        }
    }

    public static StandardMaterial3D BodyMaterial(Color albedo, Color emission, float emissiveEnergy)
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = albedo,
            Roughness = 0.65f,
            Metallic = 0.15f,
            EmissionEnabled = emissiveEnergy > 0.001f,
            Emission = emission,
            EmissionEnergyMultiplier = emissiveEnergy,
        };
        return m;
    }

    public static StandardMaterial3D UnshadedMaterial(Color color, bool transparent = false, bool billboard = false)
    {
        var m = new StandardMaterial3D
        {
            AlbedoColor = color,
            ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
            Transparency = transparent
                ? BaseMaterial3D.TransparencyEnum.Alpha
                : BaseMaterial3D.TransparencyEnum.Disabled,
            BillboardMode = billboard
                ? BaseMaterial3D.BillboardModeEnum.Enabled
                : BaseMaterial3D.BillboardModeEnum.Disabled,
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        return m;
    }

    private static MeshInstance3D Part(Mesh mesh, Material mat, Vector3 pos, Vector3? rotDeg = null)
    {
        var mi = new MeshInstance3D { Mesh = mesh, MaterialOverride = mat, Position = pos };
        if (rotDeg is { } r)
        {
            mi.RotationDegrees = r;
        }
        mi.CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
        return mi;
    }

    private static SphereMesh Gem(float radius) => new()
    {
        Radius = radius,
        Height = radius * 2f,
        RadialSegments = 5,
        Rings = 3,
    };

    private static CapsuleMesh FacetedCapsule(float radius, float height, int segments) => new()
    {
        Radius = radius,
        Height = height + radius * 2f,
        RadialSegments = segments,
        Rings = 2,
    };

    private static CylinderMesh Cone(float radius, float height) => new()
    {
        TopRadius = 0f,
        BottomRadius = radius,
        Height = height,
        RadialSegments = 4,
        Rings = 1,
    };

    private static CylinderMesh Cyl(float rTop, float rBottom, float height) => new()
    {
        TopRadius = rTop,
        BottomRadius = rBottom,
        Height = height,
        RadialSegments = 5,
        Rings = 1,
    };

    /// <summary>A hero: faceted capsule torso, gem head, shoulder pauldrons, and a
    /// planted foot block; tier 3 adds a back crest — a real silhouette change at
    /// final evolution. Base sits at y=0, ~1.6 tall.</summary>
    public static Node3D BuildHero(string modelId, int tier, Material mat)
    {
        var rng = new RngState(SeedOf(modelId));
        var root = new Node3D { Name = "Body" };
        float s = TierScale(tier);
        float bodyH = (float)Jitter(ref rng, 0.95, 0.08) * s;
        float bodyR = (float)Jitter(ref rng, 0.34, 0.03) * s;
        float headR = (float)Jitter(ref rng, 0.3, 0.02) * s;
        float shoulderR = bodyR * 0.55f;

        root.AddChild(Part(FacetedCapsule(bodyR, bodyH, 6), mat, new Vector3(0, bodyR + bodyH / 2f, 0)));

        float headY = bodyR + bodyH + headR * 0.7f;
        root.AddChild(Part(Gem(headR), mat, new Vector3(0, headY, 0)));

        float shoulderY = bodyR + bodyH * 0.9f;
        root.AddChild(Part(Gem(shoulderR), mat, new Vector3(bodyR * 0.9f, shoulderY, 0)));
        root.AddChild(Part(Gem(shoulderR), mat, new Vector3(-bodyR * 0.9f, shoulderY, 0)));

        root.AddChild(Part(
            new BoxMesh { Size = new Vector3(bodyR * 1.6f, 0.18f, bodyR * 1.2f) },
            mat, new Vector3(0, 0.09f, 0)));

        if (tier >= 3)
        {
            root.AddChild(Part(Cone(0.16f * s, 0.5f * s), mat,
                new Vector3(0, headY + 0.15f, -bodyR * 0.6f), new Vector3(180, 0, 0)));
        }
        return root;
    }

    /// <summary>A standard enemy: same silhouette family, hostile spikes. ~1.3 tall.</summary>
    public static Node3D BuildEnemy(string modelId, float size, Material mat)
    {
        var rng = new RngState(SeedOf(modelId));
        var root = new Node3D { Name = "Body" };
        float bodyH = (float)Jitter(ref rng, 0.6, 0.08) * size;
        float bodyR = (float)Jitter(ref rng, 0.4, 0.04) * size;
        float headR = (float)Jitter(ref rng, 0.24, 0.02) * size;

        root.AddChild(Part(FacetedCapsule(bodyR, bodyH, 5), mat, new Vector3(0, bodyR + bodyH / 2f, 0)));
        root.AddChild(Part(Gem(headR), mat, new Vector3(0, bodyR + bodyH + headR * 0.6f, 0)));

        int spikes = 3 + (int)(Rng.NextDouble(ref rng) * 3);
        for (int i = 0; i < spikes; i++)
        {
            double a = (double)i / spikes * Math.Tau + Rng.NextDouble(ref rng) * 0.4;
            float r = bodyR * 0.95f;
            root.AddChild(Part(
                Cone(0.1f * size, (float)Jitter(ref rng, 0.3, 0.08) * size), mat,
                new Vector3(Mathf.Cos((float)a) * r, bodyR + bodyH * 0.7f, Mathf.Sin((float)a) * r)));
        }
        return root;
    }

    /// <summary>A boss: larger enemy body with a heavy crown of spikes and a core gem.</summary>
    public static Node3D BuildBoss(string modelId, float size, Material mat)
    {
        var rng = new RngState(SeedOf(modelId));
        var root = new Node3D { Name = "Body" };
        float s = size * 1.7f;
        float bodyR = 0.7f * s * 0.6f;
        float bodyH = 0.7f * s * 0.6f;

        root.AddChild(Part(FacetedCapsule(bodyR, bodyH, 6), mat, new Vector3(0, bodyR + bodyH / 2f, 0)));
        root.AddChild(Part(Gem(0.34f * s * 0.6f), mat, new Vector3(0, bodyR + bodyH + 0.2f * s * 0.6f, 0)));

        const int crown = 6;
        for (int i = 0; i < crown; i++)
        {
            double a = (double)i / crown * Math.Tau;
            float r = bodyR * 1.05f;
            root.AddChild(Part(
                Cone(0.14f * s * 0.6f, (float)Jitter(ref rng, 0.5, 0.1) * s * 0.6f), mat,
                new Vector3(Mathf.Cos((float)a) * r, bodyR + bodyH, Mathf.Sin((float)a) * r)));
        }
        return root;
    }

    /// <summary>Held prop indicating weapon type and evolution. Grip at origin,
    /// blade/head extending +Y. A readable emblem, not a hero-sized object.</summary>
    public static Node3D BuildWeapon(string modelId, WeaponType type, int tier, Material mat)
    {
        var rng = new RngState(SeedOf(modelId));
        var root = new Node3D { Name = "Weapon" };
        const float grip = 0.28f;
        root.AddChild(Part(Cyl(0.05f, 0.06f, grip), mat, new Vector3(0, grip / 2f, 0)));

        switch (type)
        {
            case WeaponType.Sword:
            {
                root.AddChild(Part(new BoxMesh { Size = new Vector3(0.22f, 0.05f, 0.06f) }, mat,
                    new Vector3(0, grip + 0.02f, 0)));
                float bladeLen = (float)Jitter(ref rng, 0.55, 0.05);
                root.AddChild(Part(new BoxMesh { Size = new Vector3(0.1f, bladeLen, 0.04f) }, mat,
                    new Vector3(0, grip + 0.04f + bladeLen / 2f, 0)));
                break;
            }
            case WeaponType.Lance:
            {
                float shaft = (float)Jitter(ref rng, 0.7, 0.05);
                root.AddChild(Part(Cyl(0.03f, 0.035f, shaft), mat, new Vector3(0, grip + shaft / 2f, 0)));
                root.AddChild(Part(Cone(0.06f, 0.18f), mat, new Vector3(0, grip + shaft + 0.09f, 0)));
                break;
            }
            default:
            {
                const float shaft = 0.4f;
                root.AddChild(Part(Cyl(0.035f, 0.04f, shaft), mat, new Vector3(0, shaft / 2f, 0)));
                root.AddChild(Part(new BoxMesh { Size = new Vector3(0.34f, 0.26f, 0.3f) }, mat,
                    new Vector3(0, shaft + 0.05f, 0)));
                break;
            }
        }

        if (tier >= 1)
        {
            // A small accent gem appears once the ultimate is unlocked (Lv10+).
            root.AddChild(Part(Gem(0.05f), mat, new Vector3(0, grip - 0.02f, 0.02f)));
        }
        return root;
    }
}

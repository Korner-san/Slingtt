using System.Collections.Generic;
using Godot;
using Slingtt.Game;
using Slingtt.Sim;

namespace Slingtt.Render;

// Impact feedback: floating damage numbers, hit sparks, and ultimate shapes.
// Everything is pooled and recycled — a battle can emit dozens of events per
// second and allocating a node per hit would stutter on a mid-range phone.
public sealed partial class VfxView : Node3D
{
    private const int PoolSize = 24;

    private sealed class Floater
    {
        public Label3D Label = null!;
        public float Life;
        public Vector3 Origin;
    }

    private sealed class Burst
    {
        public MeshInstance3D Mesh = null!;
        public StandardMaterial3D Mat = null!;
        public float Life;
        public float MaxScale;
    }

    private readonly List<Floater> _floaters = new();
    private readonly List<Burst> _bursts = new();
    private int _floaterCursor;
    private int _burstCursor;

    private float _arenaW;
    private float _arenaH;

    public static VfxView Create(float arenaW, float arenaH)
    {
        var v = new VfxView { Name = "Vfx", _arenaW = arenaW, _arenaH = arenaH };
        v.Build();
        return v;
    }

    private void Build()
    {
        for (int i = 0; i < PoolSize; i++)
        {
            var label = new Label3D
            {
                Text = "",
                FontSize = 96,
                PixelSize = 0.0032f,
                Billboard = BaseMaterial3D.BillboardModeEnum.Enabled,
                NoDepthTest = true,
                Visible = false,
                OutlineSize = 20,
                OutlineModulate = new Color(0, 0, 0, 0.85f),
            };
            AddChild(label);
            _floaters.Add(new Floater { Label = label });
        }

        var burstMesh = new SphereMesh { Radius = 0.5f, Height = 1.0f, RadialSegments = 8, Rings = 4 };
        for (int i = 0; i < PoolSize; i++)
        {
            StandardMaterial3D mat = MeshFactory.UnshadedMaterial(Palette.VfxImpact, transparent: true);
            var mi = new MeshInstance3D
            {
                Mesh = burstMesh,
                MaterialOverride = mat,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(mi);
            _bursts.Add(new Burst { Mesh = mi, Mat = mat });
        }
    }

    private Vector3 ToWorld(Vec2 simPos, float y = 0.6f) => new(
        (float)Coords.SimToWorldX(simPos.X, _arenaW),
        y,
        (float)Coords.SimToWorldZ(simPos.Y, _arenaH));

    public void OnHit(Vec2 pos, double damage, HitKind kind, bool targetIsHero)
    {
        Color color = kind switch
        {
            HitKind.Aoe => Palette.VfxAoe,
            HitKind.Pierce => Palette.VfxPierce,
            HitKind.Ultimate => Palette.VfxUltimate,
            _ => targetIsHero ? Palette.TeamEnemy : Palette.VfxImpact,
        };
        SpawnFloater(ToWorld(pos, 0.9f), Mathf.Round((float)damage).ToString(), color,
            kind == HitKind.Ultimate ? 1.35f : 1.0f);
        SpawnBurst(ToWorld(pos), color, kind == HitKind.Aoe ? 1.8f : 0.9f, 0.28f);
    }

    public void OnHeal(Vec2 pos, double amount)
    {
        SpawnFloater(ToWorld(pos, 1.1f), "+" + Mathf.Round((float)amount), Palette.VfxHeal, 1.0f);
    }

    public void OnShield(Vec2 pos, double amount)
    {
        SpawnFloater(ToWorld(pos, 1.1f), "SHIELD " + Mathf.Round((float)amount), Palette.VfxShield, 0.9f);
        SpawnBurst(ToWorld(pos), Palette.VfxShield, 1.6f, 0.4f);
    }

    public void OnWallBounce(Vec2 pos)
    {
        SpawnBurst(ToWorld(pos, 0.4f), Palette.VfxSpark, 0.5f, 0.16f);
    }

    public void OnDeath(Vec2 pos)
    {
        SpawnBurst(ToWorld(pos), Palette.TeamEnemy, 2.2f, 0.45f);
    }

    /// <summary>The ultimate's shape, drawn once at full extent so the player can
    /// read what the ability actually covered. Geometry comes from the same
    /// ShapeResolver.Directions the sim used to decide who got hit, rather than
    /// a second hardcoded per-Kind switch here — one source of truth for what
    /// the shape actually is (Prompt 1).</summary>
    public void OnWeaponUltimate(Vec2 pos, Vec2 dir, WeaponUltimateSpec spec)
    {
        Vector3 origin = ToWorld(pos, 0.5f);
        switch (spec.Kind)
        {
            case WeaponUltKind.Aftershock:
                SpawnBurst(origin, Palette.VfxUltimate, (float)spec.Shape.Radius * 2f, 0.5f);
                break;
            case WeaponUltKind.Cross:
            case WeaponUltKind.Beam:
            {
                Vec2 baseDir = dir.LengthSquared() < 0.0001 ? new Vec2(0, 1) : dir;
                float length = spec.Kind == WeaponUltKind.Beam ? 24f : 20f;
                foreach ((double ux, double uy) in ShapeResolver.Directions(spec.Shape, baseDir))
                {
                    SpawnBeam(origin, new Vector3((float)ux, 0, (float)uy), (float)spec.Shape.Width, length);
                }
                break;
            }
        }
    }

    private void SpawnFloater(Vector3 pos, string text, Color color, float scale)
    {
        Floater f = _floaters[_floaterCursor];
        _floaterCursor = (_floaterCursor + 1) % _floaters.Count;
        f.Label.Text = text;
        f.Label.Modulate = color;
        f.Label.Scale = Vector3.One * scale;
        f.Label.Visible = true;
        f.Origin = pos;
        f.Label.Position = pos;
        f.Life = 0.85f;
    }

    private void SpawnBurst(Vector3 pos, Color color, float maxScale, float life)
    {
        Burst b = _bursts[_burstCursor];
        _burstCursor = (_burstCursor + 1) % _bursts.Count;
        b.Mesh.Mesh = new SphereMesh { Radius = 0.5f, Height = 1.0f, RadialSegments = 8, Rings = 4 };
        b.Mesh.Position = pos;
        b.Mesh.Rotation = Vector3.Zero;
        b.Mesh.Scale = Vector3.One * 0.1f;
        b.Mesh.Visible = true;
        b.Mat.AlbedoColor = color;
        b.MaxScale = maxScale;
        b.Life = life;
    }

    private void SpawnBeam(Vector3 origin, Vector3 dir, float width, float length)
    {
        Burst b = _bursts[_burstCursor];
        _burstCursor = (_burstCursor + 1) % _bursts.Count;
        b.Mesh.Mesh = new BoxMesh { Size = new Vector3(Mathf.Max(width, 0.3f), 0.12f, length) };
        b.Mesh.Position = origin;
        b.Mesh.Scale = Vector3.One;
        b.Mesh.Visible = true;
        b.Mesh.LookAt(origin + dir, Vector3.Up);
        b.Mat.AlbedoColor = Palette.VfxUltimate;
        b.MaxScale = 1f;
        b.Life = 0.35f;
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta;

        foreach (Floater f in _floaters)
        {
            if (f.Life <= 0)
            {
                continue;
            }
            f.Life -= dt;
            if (f.Life <= 0)
            {
                f.Label.Visible = false;
                continue;
            }
            float k = 1f - f.Life / 0.85f;
            f.Label.Position = f.Origin + new Vector3(0, k * 1.1f, 0);
            f.Label.Modulate = f.Label.Modulate with { A = Mathf.Clamp(f.Life / 0.4f, 0f, 1f) };
        }

        foreach (Burst b in _bursts)
        {
            if (b.Life <= 0)
            {
                continue;
            }
            b.Life -= dt;
            if (b.Life <= 0)
            {
                b.Mesh.Visible = false;
                continue;
            }
            if (b.MaxScale > 1f || b.Mesh.Mesh is SphereMesh)
            {
                float k = 1f - Mathf.Max(b.Life, 0f) / 0.5f;
                b.Mesh.Scale = Vector3.One * Mathf.Lerp(0.1f, b.MaxScale, Mathf.Clamp(k * 2f, 0f, 1f));
            }
            b.Mat.AlbedoColor = b.Mat.AlbedoColor with { A = Mathf.Clamp(b.Life * 3f, 0f, 0.85f) };
        }
    }
}

using System.Collections.Generic;
using Godot;
using Slingtt.Game;
using Slingtt.Sim;

namespace Slingtt.Render;

// Impact feedback: floating damage numbers, hit sparks, and the live weapon-
// ultimate projectile pool. Everything is pooled and recycled — a battle can
// emit dozens of events per second and allocating a node per hit would
// stutter on a mid-range phone.
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

    private const int HazardPoolSize = 40;

    // Live-iteration rework — worst case for simultaneous bullets:
    // BulletCount(Legendary=4) x DirectionCount(tier3=4) x 2 (DualActivation)
    // = 32. Grenade/Boomerang only ever have up to 2 in flight at once
    // (DualActivation), never more.
    private const int BulletPoolSize = 32;
    private const int GrenadePoolSize = 2;
    private const int BoomerangPoolSize = 2;

    private readonly List<Floater> _floaters = new();
    private readonly List<Burst> _bursts = new();
    private int _floaterCursor;
    private int _burstCursor;

    private readonly MeshInstance3D[] _hazardMarkers = new MeshInstance3D[HazardPoolSize];
    private readonly StandardMaterial3D[] _hazardMats = new StandardMaterial3D[HazardPoolSize];

    private readonly MeshInstance3D[] _bulletPool = new MeshInstance3D[BulletPoolSize];
    private readonly MeshInstance3D[] _grenadePool = new MeshInstance3D[GrenadePoolSize];
    private readonly MeshInstance3D[] _boomerangPool = new MeshInstance3D[BoomerangPoolSize];

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

        // Prompt 10 — "trail visuals": one flat disc per pool slot, synced
        // every frame against the controller's live hazard list (SyncHazards)
        // rather than reacting to a one-shot event — a hazard has none.
        var hazardMesh = new CylinderMesh { TopRadius = 0.6f, BottomRadius = 0.6f, Height = 0.02f, RadialSegments = 12, Rings = 1 };
        for (int i = 0; i < HazardPoolSize; i++)
        {
            var mat = MeshFactory.UnshadedMaterial(Palette.VfxTrail with { A = 0.4f }, transparent: true);
            var mi = new MeshInstance3D
            {
                Mesh = hazardMesh,
                MaterialOverride = mat,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(mi);
            _hazardMarkers[i] = mi;
            _hazardMats[i] = mat;
        }

        // Live-iteration rework — the weapon-ultimate projectile pools. Each
        // kind gets its own persistent mesh/material (not swapped per-use
        // like Burst) since, unlike a one-shot flash, a projectile is synced
        // continuously frame over frame while it's actually in flight.
        var bulletMesh = new CapsuleMesh { Radius = 0.09f, Height = 0.42f, RadialSegments = 8, Rings = 2 };
        StandardMaterial3D bulletMat = MeshFactory.UnshadedMaterial(Palette.VfxSpark, transparent: false);
        for (int i = 0; i < BulletPoolSize; i++)
        {
            var mi = new MeshInstance3D
            {
                Mesh = bulletMesh,
                MaterialOverride = bulletMat,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(mi);
            _bulletPool[i] = mi;
        }

        var grenadeMesh = new SphereMesh { Radius = 0.22f, Height = 0.44f, RadialSegments = 8, Rings = 4 };
        StandardMaterial3D grenadeMat = MeshFactory.UnshadedMaterial(Palette.VfxAoe, transparent: false);
        for (int i = 0; i < GrenadePoolSize; i++)
        {
            var mi = new MeshInstance3D
            {
                Mesh = grenadeMesh,
                MaterialOverride = grenadeMat,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(mi);
            _grenadePool[i] = mi;
        }

        var boomerangMesh = new BoxMesh { Size = new Vector3(0.14f, 0.06f, 0.5f) };
        StandardMaterial3D boomerangMat = MeshFactory.UnshadedMaterial(Palette.VfxPierce, transparent: false);
        for (int i = 0; i < BoomerangPoolSize; i++)
        {
            var mi = new MeshInstance3D
            {
                Mesh = boomerangMesh,
                MaterialOverride = boomerangMat,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(mi);
            _boomerangPool[i] = mi;
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

    /// <summary>Prompt 10 — the live-battle counterpart to Predict's new
    /// enemy-bounce prediction: a distinct spark from OnWallBounce's, at the
    /// actual contact point, whenever a Sword-type deflects off an opponent.</summary>
    public void OnEnemyBounce(Vec2 pos)
    {
        SpawnBurst(ToWorld(pos, 0.5f), Palette.VfxImpact, 0.7f, 0.2f);
    }

    /// <summary>Prompt 10 — "contact pulse": a world-space ring at the touch
    /// point when Prompt 5's contact combo procs. The HUD's own combo-counter
    /// punch (BattleHud.PulseCombo) is the other half of "combo counter."</summary>
    public void OnComboContact(Vec2 pos, int stacks)
    {
        SpawnFloater(ToWorld(pos, 1.2f), $"COMBO x{stacks}", Palette.RarityLegendary, 1.1f);
        SpawnBurst(ToWorld(pos, 0.5f), Palette.RarityLegendary, 1.3f, 0.35f);
    }

    /// <summary>Prompt 10 — "mark visual" one-shot: the persistent per-turn
    /// indicator is ActorView.SetMarked, synced every frame; this is just the
    /// moment-of-application flash.</summary>
    public void OnMarkApplied(Vec2 pos)
    {
        SpawnFloater(ToWorld(pos, 1.2f), "MARKED", Palette.VfxMark, 1.0f);
        SpawnBurst(ToWorld(pos, 0.6f), Palette.VfxMark, 1.1f, 0.4f);
    }

    public void OnEnemySplit(Vec2 pos)
    {
        SpawnBurst(ToWorld(pos, 0.5f), Palette.TeamEnemy, 1.4f, 0.4f);
    }

    public void OnSwap(Vec2 pos)
    {
        SpawnBurst(ToWorld(pos, 0.5f), Palette.TeamHero, 1.2f, 0.3f);
    }

    public void OnDeath(Vec2 pos)
    {
        SpawnBurst(ToWorld(pos), Palette.TeamEnemy, 2.2f, 0.45f);
    }

    /// <summary>Live-iteration rework — the "muzzle flash" at the cast
    /// position the instant a weapon ultimate fires. The travel and the
    /// actual damage are handled entirely elsewhere now: SyncProjectiles
    /// renders the real flight, and each impact's OnHit already fires
    /// naturally (via the normal Hit event) at whatever tick the projectile
    /// actually connects — nothing to schedule or stagger here anymore.</summary>
    public void OnWeaponUltimate(Vec2 pos)
    {
        SpawnBurst(ToWorld(pos, 0.5f), Palette.VfxUltimate, 1.4f, 0.3f);
    }

    /// <summary>Live-iteration rework — called every frame from
    /// BattleScene._Process with the controller's live projectile list, same
    /// pattern SyncHazards already uses. Bucketed into three pools by kind
    /// (each visually distinct), index-matched within each — a projectile can
    /// "jump" pool slots on the rare frame one resolves from the middle of
    /// the list, the same acceptable trade SyncHazards already makes.</summary>
    public void SyncProjectiles(IReadOnlyList<RenderProjectile> projectiles)
    {
        int bullets = 0, grenades = 0, boomerangs = 0;
        foreach (RenderProjectile p in projectiles)
        {
            Vector3 pos = ToWorld(new Vec2(p.X, p.Y), 0.5f);
            var lookDir = new Vector3((float)p.DirX, 0, (float)p.DirY);
            switch (p.Kind)
            {
                case ProjectileKind.Bullet when bullets < BulletPoolSize:
                    PlaceProjectile(_bulletPool[bullets++], pos, lookDir);
                    break;
                case ProjectileKind.Grenade when grenades < GrenadePoolSize:
                    PlaceProjectile(_grenadePool[grenades++], pos, lookDir);
                    break;
                case ProjectileKind.Boomerang when boomerangs < BoomerangPoolSize:
                    PlaceProjectile(_boomerangPool[boomerangs++], pos, lookDir);
                    break;
            }
        }
        for (int i = bullets; i < BulletPoolSize; i++)
        {
            _bulletPool[i].Visible = false;
        }
        for (int i = grenades; i < GrenadePoolSize; i++)
        {
            _grenadePool[i].Visible = false;
        }
        for (int i = boomerangs; i < BoomerangPoolSize; i++)
        {
            _boomerangPool[i].Visible = false;
        }
    }

    private static void PlaceProjectile(MeshInstance3D mi, Vector3 pos, Vector3 lookDir)
    {
        mi.Visible = true;
        mi.Position = pos;
        if (lookDir.LengthSquared() > 0.0001f)
        {
            mi.LookAt(pos + lookDir, Vector3.Up);
        }
    }

    /// <summary>Prompt 10 — "trail visuals": called every frame from
    /// BattleScene.SyncActors' sibling sync pass with the controller's live
    /// hazard list. Index-matched against the pool, not id-stable — a hazard
    /// disc can visually "jump" pool slots on the rare frame one expires from
    /// the middle of the list, an acceptable trade for not tracking ids.</summary>
    public void SyncHazards(IReadOnlyList<Hazard> hazards)
    {
        int shown = Math.Min(hazards.Count, HazardPoolSize);
        for (int i = 0; i < shown; i++)
        {
            _hazardMarkers[i].Position = ToWorld(hazards[i].Pos, 0.02f);
            _hazardMarkers[i].Visible = true;
        }
        for (int i = shown; i < HazardPoolSize; i++)
        {
            _hazardMarkers[i].Visible = false;
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

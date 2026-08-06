using Godot;
using Slingtt.Game;
using Slingtt.Sim;

namespace Slingtt.Render;

// The scene graph for one actor: body + orbiting weapon prop + blob shadow +
// billboarded HP bar + active-turn ring. Built once, then mutated every frame —
// nothing is re-instantiated in the loop.
public sealed partial class ActorView : Node3D
{
    private const float HpBarWidth = 1.1f;

    private ActorVisual _visual = null!;
    private Node3D _body = null!;
    private Node3D _weapon = null!;
    private MeshInstance3D _ring = null!;
    private MeshInstance3D _hpFill = null!;
    private MeshInstance3D _hpBack = null!;
    private MeshInstance3D _shieldBar = null!;
    private MeshInstance3D _markIcon = null!;
    private StandardMaterial3D _bodyMat = null!;
    private StandardMaterial3D _ringMat = null!;

    private float _baseEmissive;
    private float _flashUntil;

    public string ActorId => _visual.Id;

    public static ActorView Create(ActorVisual v)
    {
        var view = new ActorView { Name = "Actor_" + v.Id.Replace('#', '_') };
        view.Build(v);
        return view;
    }

    private void Build(ActorVisual v)
    {
        _visual = v;

        Color teamColor = v.Team == Team.Hero ? Palette.TeamHero : Palette.TeamEnemy;
        Color emissive = v.Rarity is not null
            ? Palette.Rarity(v.Rarity)
            : v.Team == Team.Hero ? Palette.TeamHero : Palette.TeamEnemyDark;

        _baseEmissive = Palette.EmissiveFor(v.Tier);
        _bodyMat = MeshFactory.BodyMaterial(teamColor, emissive, _baseEmissive);

        float size = (float)(v.Radius / 0.5);
        _body = v.Generator switch
        {
            "boss" => MeshFactory.BuildBoss(v.ModelId, size, _bodyMat),
            "enemy" => MeshFactory.BuildEnemy(v.ModelId, size, _bodyMat),
            _ => MeshFactory.BuildHero(v.ModelId, v.Tier, _bodyMat),
        };
        AddChild(_body);

        Color weaponHue = Palette.Weapon(v.WeaponType);
        var weaponMat = MeshFactory.BodyMaterial(weaponHue, weaponHue, 0.25f + 0.3f * v.Tier);
        _weapon = MeshFactory.BuildWeapon(v.WeaponModelId, v.WeaponType, v.Tier, weaponMat);
        _weapon.Scale = Vector3.One * 0.9f;
        AddChild(_weapon);

        // Active-turn ring: a flat torus on the ground, unshaded so it reads as UI.
        _ringMat = MeshFactory.UnshadedMaterial(Palette.VfxUltimate, transparent: true);
        _ring = new MeshInstance3D
        {
            // Rings is the subdivision AROUND the ring; a low value here makes the
            // turn marker read as a diamond instead of a circle.
            Mesh = new TorusMesh
            {
                InnerRadius = (float)v.Radius + 0.14f,
                OuterRadius = (float)v.Radius + 0.26f,
                RingSegments = 6,
                Rings = 32,
            },
            MaterialOverride = _ringMat,
            Position = new Vector3(0, 0.03f, 0),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_ring);

        // Blob shadow — a cheap grounded contact cue that works on the mobile renderer.
        var blobMat = MeshFactory.UnshadedMaterial(new Color(0, 0, 0, 0.35f), transparent: true);
        AddChild(new MeshInstance3D
        {
            Mesh = new CylinderMesh
            {
                TopRadius = (float)v.Radius * 1.15f,
                BottomRadius = (float)v.Radius * 1.15f,
                Height = 0.01f,
                RadialSegments = 16,
                Rings = 1,
            },
            MaterialOverride = blobMat,
            Position = new Vector3(0, 0.015f, 0),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });

        // HP bar: billboarded quads that ignore depth so they never sink into a model.
        float barY = (float)v.Radius * 2f + 1.15f;
        var hpRoot = new Node3D { Name = "HpBar", Position = new Vector3(0, barY, 0) };

        _hpBack = MakeBar(new Color(0, 0, 0, 0.55f), HpBarWidth, 0.16f, 0f);
        hpRoot.AddChild(_hpBack);

        Color fillColor = v.Team == Team.Hero ? Palette.UiHpHero : Palette.UiHpEnemy;
        _hpFill = MakeBar(fillColor, HpBarWidth, 0.16f, 0.002f);
        hpRoot.AddChild(_hpFill);

        _shieldBar = MakeBar(Palette.VfxShield, HpBarWidth, 0.06f, 0.004f);
        _shieldBar.Position = new Vector3(0, 0.12f, 0.004f);
        _shieldBar.Visible = false;
        hpRoot.AddChild(_shieldBar);

        AddChild(hpRoot);

        // Prompt 10 — "mark visual": a small pulsing marker over the actor,
        // visible for as long as Prompt 7's MarkedTurns > 0.
        var markMat = MeshFactory.UnshadedMaterial(Palette.VfxMark, transparent: true);
        _markIcon = new MeshInstance3D
        {
            Mesh = new SphereMesh { Radius = 0.13f, Height = 0.26f, RadialSegments = 6, Rings = 4 },
            MaterialOverride = markMat,
            Position = new Vector3(0, barY + 0.3f, 0),
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_markIcon);
    }

    private static MeshInstance3D MakeBar(Color color, float w, float h, float z)
    {
        StandardMaterial3D mat = MeshFactory.UnshadedMaterial(color, transparent: true, billboard: true);
        mat.NoDepthTest = true;
        mat.RenderPriority = 10;
        return new MeshInstance3D
        {
            Mesh = new QuadMesh { Size = new Vector2(w, h) },
            MaterialOverride = mat,
            Position = new Vector3(0, 0, z),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
    }

    /// <summary>Position on the ground plane plus idle motion. `tSec` is a
    /// presentation clock — it never feeds back into the simulation.</summary>
    public void SetPose(float worldX, float worldZ, bool active, float tSec)
    {
        Position = new Vector3(worldX, 0, worldZ);

        float bob = active ? Mathf.Sin(tSec * 3f) * 0.04f : 0f;
        _body.Position = new Vector3(0, bob, 0);

        float a = tSec * 1.5f;
        float orbit = (float)_visual.Radius + 0.25f;
        _weapon.Position = new Vector3(
            Mathf.Cos(a) * orbit,
            (float)_visual.Radius + 0.35f + bob,
            Mathf.Sin(a) * orbit);
        _weapon.Rotation = new Vector3(0, -a, 0.5f);

        _ring.Visible = active;
        if (active)
        {
            float s = 1f + Mathf.Sin(tSec * 6f) * 0.06f;
            _ring.Scale = new Vector3(s, 1f, s);
            _ringMat.AlbedoColor = Palette.VfxUltimate with { A = 0.6f + Mathf.Sin(tSec * 6f) * 0.25f };
        }

        if (_markIcon.Visible)
        {
            float s = 1f + Mathf.Sin(tSec * 5f) * 0.18f;
            _markIcon.Scale = Vector3.One * s;
        }

        // Hit flash decay.
        if (tSec < _flashUntil)
        {
            float k = (_flashUntil - tSec) / 0.12f;
            _bodyMat.EmissionEnabled = true;
            _bodyMat.EmissionEnergyMultiplier = _baseEmissive + k * 1.2f;
        }
        else
        {
            _bodyMat.EmissionEnabled = _baseEmissive > 0.001f;
            _bodyMat.EmissionEnergyMultiplier = _baseEmissive;
        }
    }

    public void SetHealth(float ratio, bool hasShield)
    {
        float r = Mathf.Clamp(ratio, 0f, 1f);
        _hpFill.Scale = new Vector3(r, 1f, 1f);
        _hpFill.Position = new Vector3(-(HpBarWidth / 2f) * (1f - r), 0, 0.002f);
        _shieldBar.Visible = hasShield;
    }

    public void Flash(float clockSec) => _flashUntil = clockSec + 0.12f;

    public void SetAlive(bool alive) => Visible = alive;

    public void SetMarked(bool marked) => _markIcon.Visible = marked;
}

using Godot;
using Slingtt.Game;
using Slingtt.Sim;

namespace Slingtt.Render;

// The aim preview: a dotted trajectory, bounce markers, and a sling band from
// the hero to the drag point. The dots come from the REAL simulation running
// on a throwaway clone — an approximation here would diverge from the shot
// the player actually gets, which is the fastest way to destroy trust in the
// mechanic. Deliberately shows only a flat 60% taste of the shot (see
// BattleController.PreviewMaxTicks) with no ultimate-outcome indication at
// all — the player draws, sees roughly where it's heading, and commits.
public sealed partial class AimView : Node3D
{
    private const int MaxDots = 48;
    private const float DotSpacing = 0.42f; // sim units between dots

    private readonly MeshInstance3D[] _dots = new MeshInstance3D[MaxDots];
    private readonly StandardMaterial3D[] _dotMats = new StandardMaterial3D[MaxDots];
    private readonly MeshInstance3D[] _bounces = new MeshInstance3D[4];
    private readonly MeshInstance3D[] _enemyBounces = new MeshInstance3D[4]; // Prompt 10
    private MeshInstance3D _band = null!;
    private StandardMaterial3D _bandMat = null!;

    private float _arenaW;
    private float _arenaH;

    public static AimView Create(float arenaW, float arenaH)
    {
        var v = new AimView { Name = "AimView", _arenaW = arenaW, _arenaH = arenaH };
        v.Build();
        return v;
    }

    private void Build()
    {
        // Prompt 10 — each dot gets its OWN material (not a shared instance)
        // so the near-cutoff fade can vary alpha per dot, not just size.
        var dotMesh = new SphereMesh { Radius = 0.09f, Height = 0.18f, RadialSegments = 6, Rings = 3 };
        for (int i = 0; i < MaxDots; i++)
        {
            StandardMaterial3D mat = MeshFactory.UnshadedMaterial(Palette.VfxSpark with { A = 0.9f }, transparent: true);
            _dotMats[i] = mat;
            _dots[i] = new MeshInstance3D
            {
                Mesh = dotMesh,
                MaterialOverride = mat,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_dots[i]);
        }

        var bounceMat = MeshFactory.UnshadedMaterial(Palette.VfxUltimate with { A = 0.85f }, transparent: true);
        var bounceMesh = new TorusMesh { InnerRadius = 0.16f, OuterRadius = 0.26f, RingSegments = 12, Rings = 3 };
        for (int i = 0; i < _bounces.Length; i++)
        {
            _bounces[i] = new MeshInstance3D
            {
                Mesh = bounceMesh,
                MaterialOverride = bounceMat,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_bounces[i]);
        }

        // Prompt 10 — enemy bounces: same ring shape, distinct (enemy-team)
        // color so they read as a different kind of bounce than a wall's.
        var enemyBounceMat = MeshFactory.UnshadedMaterial(Palette.TeamEnemy with { A = 0.85f }, transparent: true);
        for (int i = 0; i < _enemyBounces.Length; i++)
        {
            _enemyBounces[i] = new MeshInstance3D
            {
                Mesh = bounceMesh,
                MaterialOverride = enemyBounceMat,
                Visible = false,
                CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            AddChild(_enemyBounces[i]);
        }

        _bandMat = MeshFactory.UnshadedMaterial(Palette.TeamHero with { A = 0.75f }, transparent: true);
        _band = new MeshInstance3D
        {
            Mesh = new BoxMesh { Size = new Vector3(0.09f, 0.02f, 1f) },
            MaterialOverride = _bandMat,
            Visible = false,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        AddChild(_band);

        Visible = false;
    }

    public void HidePreview() => Visible = false;

    /// <summary>Draw the preview for the current drag. `dragSim` is the live finger
    /// position in sim space; the shot fires opposite it.</summary>
    public void Show(AimState aim, Vec2 dragSim)
    {
        Visible = true;

        float selfX = (float)Coords.SimToWorldX(aim.Self.X, _arenaW);
        float selfZ = (float)Coords.SimToWorldZ(aim.Self.Y, _arenaH);
        var from = new Vector3(selfX, 0.5f, selfZ);

        // Sling band from the hero to the finger.
        float dragX = (float)Coords.SimToWorldX(dragSim.X, _arenaW);
        float dragZ = (float)Coords.SimToWorldZ(dragSim.Y, _arenaH);
        var to = new Vector3(dragX, 0.5f, dragZ);
        float len = from.DistanceTo(to);
        if (len > 0.05f)
        {
            _band.Visible = true;
            _band.Position = (from + to) * 0.5f;
            _band.LookAt(to, Vector3.Up);
            _band.Scale = new Vector3(1, 1, len);
            _bandMat.AlbedoColor = (aim.Legal ? Palette.TeamHero : Palette.TeamEnemy) with { A = 0.75f };
        }
        else
        {
            _band.Visible = false;
        }

        if (!aim.Legal || aim.Prediction is null)
        {
            HideDots();
            return;
        }

        // Resample the predicted path at a fixed arc-length so dot density reads as
        // speed-independent; a per-tick dot would bunch up as friction slows the shot.
        System.Collections.Generic.List<Vec2> pts = aim.Prediction.Points;
        int used = 0;
        float acc = DotSpacing;
        for (int i = 1; i < pts.Count && used < MaxDots; i++)
        {
            var prev = new Vector2((float)pts[i - 1].X, (float)pts[i - 1].Y);
            var cur = new Vector2((float)pts[i].X, (float)pts[i].Y);
            acc += prev.DistanceTo(cur);
            if (acc < DotSpacing)
            {
                continue;
            }
            acc = 0;
            _dots[used].Visible = true;
            _dots[used].Position = new Vector3(
                (float)Coords.SimToWorldX(cur.X, _arenaW),
                0.5f,
                (float)Coords.SimToWorldZ(cur.Y, _arenaH));
            used++;
        }
        for (int i = used; i < MaxDots; i++)
        {
            _dots[i].Visible = false;
        }

        // Prompt 10 — "a 60% path fraction with a fade rather than a hard
        // cut": the last ~20% of what's actually shown (not the whole
        // preview, and not the fixed MaxDots budget — the real truncation
        // point, BattleController's flat PreviewFractionBase) ramps size AND
        // alpha down toward the cutoff instead of the dotted line just
        // stopping.
        for (int i = 0; i < used; i++)
        {
            float t = used <= 1 ? 1f : i / (float)(used - 1);
            float edgeFade = t > 0.8f ? Mathf.Clamp(1f - (t - 0.8f) / 0.2f, 0f, 1f) : 1f;
            _dots[i].Scale = Vector3.One * (0.6f + 0.4f * edgeFade);
            _dotMats[i].AlbedoColor = Palette.VfxSpark with { A = 0.9f * edgeFade };
        }

        for (int i = 0; i < _bounces.Length; i++)
        {
            if (i < aim.Prediction.Bounces.Count)
            {
                Vec2 b = aim.Prediction.Bounces[i];
                _bounces[i].Visible = true;
                _bounces[i].Position = new Vector3(
                    (float)Coords.SimToWorldX(b.X, _arenaW),
                    0.35f,
                    (float)Coords.SimToWorldZ(b.Y, _arenaH));
            }
            else
            {
                _bounces[i].Visible = false;
            }
        }

        // Prompt 10 — "enemy bounce prediction."
        for (int i = 0; i < _enemyBounces.Length; i++)
        {
            if (i < aim.Prediction.EnemyBounces.Count)
            {
                Vec2 b = aim.Prediction.EnemyBounces[i];
                _enemyBounces[i].Visible = true;
                _enemyBounces[i].Position = new Vector3(
                    (float)Coords.SimToWorldX(b.X, _arenaW),
                    0.35f,
                    (float)Coords.SimToWorldZ(b.Y, _arenaH));
            }
            else
            {
                _enemyBounces[i].Visible = false;
            }
        }
    }

    private void HideDots()
    {
        foreach (MeshInstance3D d in _dots)
        {
            d.Visible = false;
        }
        foreach (MeshInstance3D b in _bounces)
        {
            b.Visible = false;
        }
        foreach (MeshInstance3D b in _enemyBounces)
        {
            b.Visible = false;
        }
    }
}

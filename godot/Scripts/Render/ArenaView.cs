using Godot;

namespace Slingtt.Render;

/// <summary>Static arena: floor slab, four walls, corner posts, and a faint grid.
/// Centred at the origin with the floor top at y=0, matching Slingtt.Game.Coords.</summary>
public static class ArenaView
{
    public static Node3D Build(float w, float h)
    {
        var root = new Node3D { Name = "Arena" };
        const float t = 0.4f;   // wall thickness
        const float wallH = 0.7f;
        float hw = w / 2f;
        float hh = h / 2f;

        StandardMaterial3D floorMat = MeshFactory.BodyMaterial(Palette.ArenaFloor, Colors.Black, 0f);
        floorMat.Roughness = 0.9f;
        StandardMaterial3D wallMat = MeshFactory.BodyMaterial(Palette.ArenaWall, Colors.Black, 0f);
        StandardMaterial3D postMat = MeshFactory.BodyMaterial(Palette.ArenaWallTop, Colors.Black, 0f);

        root.AddChild(Box(new Vector3(w, 0.3f, h), new Vector3(0, -0.15f, 0), floorMat));

        root.AddChild(Box(new Vector3(w + t, wallH, t), new Vector3(0, wallH / 2f, -hh - t / 2f), wallMat)); // far
        root.AddChild(Box(new Vector3(w + t, wallH, t), new Vector3(0, wallH / 2f, hh + t / 2f), wallMat));  // near
        root.AddChild(Box(new Vector3(t, wallH, h + t), new Vector3(-hw - t / 2f, wallH / 2f, 0), wallMat)); // left
        root.AddChild(Box(new Vector3(t, wallH, h + t), new Vector3(hw + t / 2f, wallH / 2f, 0), wallMat));  // right

        foreach (int sx in new[] { -1, 1 })
        {
            foreach (int sz in new[] { -1, 1 })
            {
                root.AddChild(Box(
                    new Vector3(t * 1.3f, wallH * 1.4f, t * 1.3f),
                    new Vector3(sx * (hw + t / 2f), wallH * 1.4f / 2f, sz * (hh + t / 2f)),
                    postMat));
            }
        }

        // Faint grid lines: they give the eye a speed reference during a fast travel.
        StandardMaterial3D gridMat = MeshFactory.UnshadedMaterial(Palette.ArenaGrid with { A = 0.5f }, transparent: true);
        for (int i = 1; i < (int)w; i++)
        {
            root.AddChild(Box(new Vector3(0.02f, 0.01f, h), new Vector3(-hw + i, 0.005f, 0), gridMat, shadows: false));
        }
        for (int i = 1; i < (int)h; i++)
        {
            root.AddChild(Box(new Vector3(w, 0.01f, 0.02f), new Vector3(0, 0.005f, -hh + i), gridMat, shadows: false));
        }

        return root;
    }

    private static MeshInstance3D Box(Vector3 size, Vector3 pos, Material mat, bool shadows = true) => new()
    {
        Mesh = new BoxMesh { Size = size },
        MaterialOverride = mat,
        Position = pos,
        CastShadow = shadows
            ? GeometryInstance3D.ShadowCastingSetting.On
            : GeometryInstance3D.ShadowCastingSetting.Off,
    };
}

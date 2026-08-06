using System;
using Godot;

namespace Slingtt.Render;

// Fixed 3/4 top-down camera. The arena is centred at the origin on the XZ plane.
// We frame it by solving for the camera distance that makes the arena's projected
// extent fill a fixed fraction of the viewport, for whatever aspect the device
// has. This fills a portrait phone properly — a bounding-sphere fit leaves the
// tilted arena looking tiny — while guaranteeing nothing clips off screen.
//
// Ported from the web original's src/render/cameraFit.ts, including the constants,
// so the mobile framing matches what the game looked like on the web.
public readonly struct CameraFit
{
    public Vector3 Position { get; init; }
    public Vector3 Target { get; init; }
    public float Fov { get; init; }
    /// <summary>Base distance from target, so impact zoom can dolly in by a
    /// fraction of it.</summary>
    public float Distance { get; init; }
}

public static class CameraFitter
{
    private const float ElevationDeg = 56f; // 0 = horizon, 90 = straight down
    public const float Fov = 42f;
    private const float TargetFill = 0.9f; // arena spans 90% of the tighter NDC axis

    /// <summary>Floor corners plus the same corners at actor height, so tall models
    /// never clip.</summary>
    private static Vector3[] FitPoints(float arenaW, float arenaH)
    {
        float hw = arenaW / 2f;
        float hh = arenaH / 2f;
        return new[]
        {
            new Vector3(-hw, 0f, -hh), new Vector3(hw, 0f, -hh),
            new Vector3(-hw, 0f, hh), new Vector3(hw, 0f, hh),
            new Vector3(-hw, 1.4f, -hh), new Vector3(hw, 1.4f, -hh),
            new Vector3(-hw, 1.4f, hh), new Vector3(hw, 1.4f, hh),
        };
    }

    private static Vector3 Direction()
    {
        float e = Mathf.DegToRad(ElevationDeg);
        return new Vector3(0, Mathf.Sin(e), Mathf.Cos(e)).Normalized();
    }

    /// <summary>Max |NDC| extent of the arena as seen from `distance`. Smaller
    /// distance -> larger extent, so this is monotonic and binary-search-able.
    /// Godot's Camera3D fov is vertical under KEEP_HEIGHT, matching three.js.</summary>
    private static float ExtentAtDistance(float aspect, float fov, float distance, Vector3[] pts)
    {
        Vector3 eye = Direction() * distance;
        // Look-at basis for a camera at `eye` aimed at the origin, with +Y up.
        Vector3 forward = (-eye).Normalized();          // camera looks along -Z in view space
        Vector3 right = forward.Cross(Vector3.Up).Normalized();
        Vector3 up = right.Cross(forward).Normalized();

        float tanHalf = Mathf.Tan(Mathf.DegToRad(fov) * 0.5f);
        float extent = 0f;

        foreach (Vector3 p in pts)
        {
            Vector3 rel = p - eye;
            float xc = rel.Dot(right);
            float yc = rel.Dot(up);
            float zc = rel.Dot(forward); // distance in front of the camera
            if (zc <= 0.001f)
            {
                return float.MaxValue; // behind the camera: treat as "way too close"
            }
            float ndcX = xc / (zc * tanHalf * aspect);
            float ndcY = yc / (zc * tanHalf);
            extent = Mathf.Max(extent, Mathf.Max(Mathf.Abs(ndcX), Mathf.Abs(ndcY)));
        }
        return extent;
    }

    public static CameraFit Compute(float arenaW, float arenaH, float aspect, float fov = Fov)
    {
        if (aspect <= 0.01f || float.IsNaN(aspect))
        {
            aspect = 0.5f; // a sane portrait default rather than a divide-by-zero camera
        }

        Vector3[] pts = FitPoints(arenaW, arenaH);
        float lo = 1f;
        float hi = 5f * Mathf.Sqrt(arenaW * arenaW + arenaH * arenaH);

        for (int i = 0; i < 40; i++)
        {
            float mid = (lo + hi) / 2f;
            // extent decreases as distance increases
            if (ExtentAtDistance(aspect, fov, mid, pts) > TargetFill)
            {
                lo = mid;
            }
            else
            {
                hi = mid;
            }
        }

        float distance = (lo + hi) / 2f;
        return new CameraFit
        {
            Position = Direction() * distance,
            Target = Vector3.Zero,
            Fov = fov,
            Distance = distance,
        };
    }

    /// <summary>Per-frame camera pose with juice: `trauma` (0..1) drives a decaying
    /// positional shake, `punch` (0..1) dollies the camera in for an impact zoom.
    /// Shake amplitude is capped by construction so an ultimate can't nauseate.</summary>
    public static void ApplyRig(Camera3D camera, CameraFit fit, float trauma, float punch, float clockSec)
    {
        Vector3 dir = fit.Position.Normalized(); // fit.Target is the origin
        float dolly = fit.Distance * (1f - punch * 0.12f);
        Vector3 pos = dir * dolly;

        float shake = trauma * trauma; // quadratic reads better than linear
        float amp = Mathf.Min(shake * 0.55f, 0.55f);
        pos.X += Mathf.Sin(clockSec * 37.1f) * amp;
        pos.Y += Mathf.Sin(clockSec * 29.3f) * amp * 0.5f;
        pos.Z += Mathf.Cos(clockSec * 41.7f) * amp;

        var lookTarget = new Vector3(fit.Target.X + Mathf.Sin(clockSec * 53.2f) * amp * 0.3f, fit.Target.Y, fit.Target.Z);
        camera.Fov = fit.Fov;
        camera.GlobalPosition = pos;
        camera.LookAt(lookTarget, Vector3.Up);
    }
}

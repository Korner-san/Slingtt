namespace Slingtt.Game;

// Sim <-> world-space mapping. The sim is a 2D plane (x, y) with its origin at a
// corner; the 3D scene puts it on the XZ ground plane with the arena CENTRED at
// the origin, so camera and shake math stay symmetric. Sim y (high value = the
// hero side) maps to +Z (near the camera); sim low y (enemies) maps to -Z (far).
// World Y is up.
public static class Coords
{
    public static double SimToWorldX(double sx, double arenaW) => sx - arenaW / 2;

    public static double SimToWorldZ(double sy, double arenaH) => sy - arenaH / 2;

    public static double WorldToSimX(double wx, double arenaW) => wx + arenaW / 2;

    public static double WorldToSimZ(double wz, double arenaH) => wz + arenaH / 2;
}

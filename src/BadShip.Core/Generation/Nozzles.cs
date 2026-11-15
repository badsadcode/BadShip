using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Engine nozzles with depth: an open bell with an inner wall and a thick lip, the glowing core set back inside it
/// (often round a central plug that glows at its tip), and a style per ship: "bell" (cooling ribs round the bell),
/// "iris" (overlapping petals round the mouth, like a jet's) or "ring" (a glowing ring inside the lip). Big nozzles get
/// gimbal actuators. The style comes from its own random stream, so the rest of the ship is as it was.
/// </summary>
public static class Nozzles
{
    public static readonly string[] Styles = ["bell", "iris", "ring"];

    /// <summary>The ship's nozzle style: by archetype, from the seed.</summary>
    public static string Pick(BuildContext c)
    {
        var r = Rng.Stream(c.Recipe.Seed, "nozzles");
        return c.Plan.Archetype switch
        {
            "warship" or "patrol" or "citadel" => r.Weighted<string>([("iris", 0.6), ("bell", 0.25), ("ring", 0.15)]),
            "civilian" or "research" or "ring" => r.Weighted<string>([("ring", 0.55), ("bell", 0.3), ("iris", 0.15)]),
            "raider" => r.Weighted<string>([("iris", 0.4), ("bell", 0.4), ("ring", 0.2)]),
            _ => r.Weighted<string>([("bell", 0.6), ("ring", 0.2), ("iris", 0.2)]),
        };
    }

    /// <summary>
    /// A nozzle from its throat (where it leaves the engine) to its mouth, widening from <paramref name="rThroat"/> to
    /// <paramref name="rExit"/>.
    /// </summary>
    public static void Build(BuildContext c, Mesh m, Vector3 throat, Vector3 exit, float rThroat, float rExit)
    {
        var style = c.NozzleStyle;
        var axis = exit - throat;
        var len = axis.Length();
        if (len < 1e-3f) return;
        var d = axis / len;
        var (u, v) = Frame(d);
        const int n = 16;
        var wall = MathF.Max(0.05f, rExit * 0.08f);
        // Outside and inside of the bell, and the lip between them at the mouth.
        Band(m, Roles.DarkMechanics, throat, exit, rThroat, rExit, u, v, n, inward: false);
        Band(m, Roles.DarkMechanics, throat + d * (len * 0.2f), exit, rThroat - wall * 0.8f, rExit - wall, u, v, n, inward: true);
        Annulus(m, Roles.HullSecondary, exit, rExit - wall, rExit + wall * 0.25f, u, v, d, n);
        // The core, set back inside the bell.
        var zCore = len * 0.22f;
        var coreR = float.Lerp(rThroat, rExit, 0.22f) - wall * 0.8f;
        Disc(m, Roles.EngineGlow, throat + d * zCore, coreR, u, v, d, n);
        var plug = style != "bell" || rExit > 1.5f;
        // Stator vanes across the core, just in front of the glow: dark spokes from the middle to the wall.
        var vanes = style == "iris" ? 8 : 6;
        var hub = plug ? coreR * 0.4f : coreR * 0.18f;
        if (!plug) Disc(m, Roles.DarkMechanics, throat + d * (zCore + wall * 0.5f), hub, u, v, d, 8);
        for (var i = 0; i < vanes; i++)
        {
            var a = i * MathF.Tau / vanes;
            var radial = u * MathF.Cos(a) + v * MathF.Sin(a);
            var tangent = Vector3.Cross(d, radial);
            var mid = throat + d * (zCore + wall * 0.5f) + radial * ((hub + coreR) / 2);
            Shapes.OBox(m, Roles.DarkMechanics, mid, radial, tangent, d, new Vector3((coreR - hub) / 2, MathF.Max(0.03f, coreR * 0.05f), wall * 0.5f));
        }
        if (plug)
        {
            // A central plug: a dark cone with a glowing tip.
            var baseC = throat + d * zCore;
            var tip = throat + d * (len * 0.62f);
            Shapes.Prism(m, Roles.DarkMechanics, baseC, Vector3.Lerp(baseC, tip, 0.75f), coreR * 0.42f, coreR * 0.22f, 12, 0, capA: false, capB: false);
            Shapes.Prism(m, Roles.EngineGlow, Vector3.Lerp(baseC, tip, 0.75f), tip, coreR * 0.22f, coreR * 0.05f, 12, 0, capA: false, capB: true);
        }
        switch (style)
        {
            case "bell":
                // Cooling ribs round the bell.
                foreach (var t in new[] { 0.3f, 0.62f })
                {
                    var rr = float.Lerp(rThroat, rExit, t) + wall * 0.6f;
                    Band(m, Roles.HullSecondary, throat + d * (len * (t - 0.05f)), throat + d * (len * (t + 0.05f)), rr, rr, u, v, n, inward: false);
                }
                break;
            case "iris":
                // Overlapping petals round the mouth, flaring a little.
                var petals = 12;
                var pl = len * 0.42f;
                for (var i = 0; i < petals; i++)
                {
                    var a = (i + 0.5f) * MathF.Tau / petals;
                    var radial = u * MathF.Cos(a) + v * MathF.Sin(a);
                    var tangent = Vector3.Normalize(Vector3.Cross(d, radial));
                    var flare = Vector3.Normalize(d + radial * 0.12f);
                    var outward = Vector3.Normalize(Vector3.Cross(tangent, flare));
                    if (Vector3.Dot(outward, radial) < 0) outward = -outward;
                    var centre = exit - flare * (pl * 0.45f) + radial * (rExit + wall * 0.6f);
                    var w = MathF.Tau * rExit / petals * 0.62f;
                    Shapes.OBox(m, i % 2 == 0 ? Roles.HullSecondary : Roles.DarkMechanics, centre, tangent, outward, flare, new Vector3(w / 2, wall * 0.35f, pl / 2));
                }
                break;
            case "ring":
                // A glowing ring just inside the lip.
                Annulus(m, Roles.EngineGlow, exit - d * (wall * 0.6f), rExit - wall * 2.2f, rExit - wall * 1.4f, u, v, d, n);
                break;
        }
        // Gimbal actuators on big nozzles: struts from the engine to the bell.
        if (rExit > 1.2f)
            for (var i = 0; i < 3; i++)
            {
                var a = i * MathF.Tau / 3 + MathF.PI / 6;
                var radial = u * MathF.Cos(a) + v * MathF.Sin(a);
                var from = throat - d * (len * 0.12f) + radial * (rThroat * 0.95f);
                var to = throat + d * (len * 0.45f) + radial * (float.Lerp(rThroat, rExit, 0.45f) + wall * 0.3f);
                Shapes.Beam(m, Roles.DarkMechanics, from, to, MathF.Max(0.08f, rExit * 0.07f));
                Shapes.Beam(m, Roles.Hazard, Vector3.Lerp(from, to, 0.35f), Vector3.Lerp(from, to, 0.5f), MathF.Max(0.1f, rExit * 0.09f));
            }
    }

    private static (Vector3 u, Vector3 v) Frame(Vector3 d)
    {
        var refv = MathF.Abs(d.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(refv, d));
        return (u, Vector3.Cross(d, u));
    }

    private static Vector3 Rim(Vector3 c, float r, Vector3 u, Vector3 v, int i, int n)
    {
        var a = i * MathF.Tau / n;
        return c + (u * MathF.Cos(a) + v * MathF.Sin(a)) * r;
    }

    /// <summary>A band round the axis from a (radius ra) to b (radius rb), facing out, or in for the inside of a bell.</summary>
    private static void Band(Mesh m, string mat, Vector3 a, Vector3 b, float ra, float rb, Vector3 u, Vector3 v, int n, bool inward)
    {
        for (var i = 0; i < n; i++)
        {
            var (p0, p1, q1, q0) = (Rim(a, ra, u, v, i, n), Rim(a, ra, u, v, i + 1, n), Rim(b, rb, u, v, i + 1, n), Rim(b, rb, u, v, i, n));
            var mid = (p0 + p1 + q0 + q1) / 4;
            var axisPoint = Vector3.Lerp(a, b, 0.5f);
            var radial = Vector3.Normalize(mid - axisPoint);
            m.AddQuad(mat, p0, p1, q1, q0, inward ? -radial : radial);
        }
    }

    /// <summary>A flat ring between two radii, facing along the axis.</summary>
    private static void Annulus(Mesh m, string mat, Vector3 c, float r0, float r1, Vector3 u, Vector3 v, Vector3 d, int n)
    {
        for (var i = 0; i < n; i++)
            m.AddQuad(mat, Rim(c, r0, u, v, i, n), Rim(c, r1, u, v, i, n), Rim(c, r1, u, v, i + 1, n), Rim(c, r0, u, v, i + 1, n), d);
    }

    /// <summary>A flat disc facing along the axis.</summary>
    private static void Disc(Mesh m, string mat, Vector3 c, float r, Vector3 u, Vector3 v, Vector3 d, int n) =>
        m.AddPolygon(mat, [.. Enumerable.Range(0, n).Select(i => Rim(c, r, u, v, i, n))], d);
}

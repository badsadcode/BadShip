using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Small craft parked in hangars and on flight decks: a boxy shuttle, a winged dropship or a slim strike craft, built to
/// fit the space they are given, in the ship's own colours (with its accent stripe), engines cold, standing on landing
/// legs. Each is its own part (type "shuttle", with its kind in the extras), its origin on the floor under its middle,
/// facing along its nose, so a game can swap it for a real model or a flyable one.
/// </summary>
public static class Craft
{
    public static readonly string[] Kinds = ["shuttle", "dropship", "striker"];

    /// <summary>
    /// A parked craft standing on the floor at <paramref name="floor"/>, nose along <paramref name="forward"/>, fitting a
    /// box <paramref name="maxL"/> long, <paramref name="maxW"/> wide and <paramref name="maxH"/> tall. Returns the part,
    /// or null when the space is too small for anything.
    /// </summary>
    public static Module? Park(BuildContext c, Rng r, string parentId, Vector3 floor, Vector3 forward, float maxL, float maxW, float maxH, string? kind = null)
    {
        kind ??= r.Weighted<string>([("shuttle", 0.5), ("dropship", 0.3), ("striker", 0.2)]);
        // Proportions per kind, scaled down until they fit.
        var (lw, lh) = kind switch { "dropship" => (0.75f, 0.42f), "striker" => (0.62f, 0.28f), _ => (0.5f, 0.42f) };
        var L = MathF.Min(maxL, MathF.Min(maxW / lw, maxH / lh));
        if (L < 4f) return null;
        var W = L * lw;
        var H = L * lh;
        var f = Vector3.Normalize(new Vector3(forward.X, 0, forward.Z));
        var up = Vector3.UnitY;
        var side = Vector3.Cross(up, f);
        var m = c.Add($"{kind}_{c.Ship.Modules.Count(x => x.Type == "shuttle")}", "shuttle", ModuleTier.Secondary, parentId);
        m.Pivot = floor;
        m.Extras = new() { ["kind"] = kind, ["length"] = MathF.Round(L, 2), ["width"] = MathF.Round(W, 2), ["height"] = MathF.Round(H, 2) };
        var mesh = m.Mesh;
        // A box in the craft's own frame: x across, y up, z along the nose, centred at (x, y, z).
        void B(string mat, float x, float y, float z, float sx, float sy, float sz) =>
            Shapes.OBox(mesh, mat, floor + side * x + up * y + f * z, side, up, f, new Vector3(sx, sy, sz) / 2);
        // A tapered block along the nose axis: from z0 (w0 wide, h0 tall, centred at y0) to z1 (w1, h1, y1).
        Vector3 P(float x, float y, float z) => floor + side * x + up * y + f * z;
        void T(string mat, float z0, float w0, float h0, float y0, float z1, float w1, float h1, float y1, string? topMat = null)
        {
            Vector3[] a = [P(-w0 / 2, y0 - h0 / 2, z0), P(w0 / 2, y0 - h0 / 2, z0), P(w0 / 2, y0 + h0 / 2, z0), P(-w0 / 2, y0 + h0 / 2, z0)];
            Vector3[] b = [P(-w1 / 2, y1 - h1 / 2, z1), P(w1 / 2, y1 - h1 / 2, z1), P(w1 / 2, y1 + h1 / 2, z1), P(-w1 / 2, y1 + h1 / 2, z1)];
            var mid = (P(0, y0, z0) + P(0, y1, z1)) / 2;
            for (var i = 0; i < 4; i++)
            {
                var j = (i + 1) % 4;
                var q = (a[i] + a[j] + b[i] + b[j]) / 4;
                mesh.AddQuad(i == 2 && topMat != null ? topMat : mat, a[i], a[j], b[j], b[i], q - mid);
            }
            mesh.AddPolygon(mat, b, z1 > z0 ? f : -f);
        }
        var gear = H * 0.16f;
        var body = Roles.HullSecondary;
        switch (kind)
        {
            case "dropship":
                // A wide body with a stepped nose, sponsons and stub wings.
                B(body, 0, gear + H * 0.25f, -L * 0.07f, W * 0.42f, H * 0.5f, L * 0.66f);
                T(body, L * 0.26f, W * 0.42f, H * 0.5f, gear + H * 0.25f, L * 0.5f, W * 0.24f, H * 0.22f, gear + H * 0.2f, Roles.Glass);
                foreach (var s in new[] { -1f, 1f })
                {
                    B(Roles.HullMain, s * W * 0.32f, gear + H * 0.22f, -L * 0.12f, W * 0.2f, H * 0.36f, L * 0.5f);
                    B(Roles.HullMain, s * W * 0.42f, gear + H * 0.32f, -L * 0.2f, W * 0.18f, H * 0.05f, L * 0.24f);
                }
                B(Roles.HullMain, 0, gear + H * 0.56f, -L * 0.1f, W * 0.3f, H * 0.1f, L * 0.4f);
                break;
            case "striker":
                // A slim fuselage, a canopy, and a broad low wing.
                B(body, 0, gear + H * 0.3f, -L * 0.1f, W * 0.24f, H * 0.42f, L * 0.6f);
                T(body, L * 0.2f, W * 0.24f, H * 0.42f, gear + H * 0.3f, L * 0.5f, W * 0.06f, H * 0.1f, gear + H * 0.26f);
                T(Roles.Glass, L * 0.04f, W * 0.15f, H * 0.04f, gear + H * 0.53f, L * 0.24f, W * 0.1f, H * 0.04f, gear + H * 0.46f);
                B(Roles.Glass, 0, gear + H * 0.57f, -L * 0.02f, W * 0.15f, H * 0.12f, L * 0.14f);
                B(Roles.HullMain, 0, gear + H * 0.2f, -L * 0.12f, W, H * 0.06f, L * 0.34f);
                B(Roles.HullMain, 0, gear + H * 0.2f, L * 0.08f, W * 0.55f, H * 0.06f, L * 0.12f);
                B(Roles.HullMain, 0, gear + H * 0.66f, -L * 0.34f, W * 0.05f, H * 0.36f, L * 0.16f);
                break;
            default:
                // A boxy shuttle: cabin, raised cockpit, roof pod.
                B(body, 0, gear + H * 0.3f, -L * 0.06f, W * 0.86f, H * 0.6f, L * 0.7f);
                // The nose tapers down to a sloped windscreen; the tail tapers to the engines.
                T(body, L * 0.29f, W * 0.86f, H * 0.6f, gear + H * 0.3f, L * 0.5f, W * 0.56f, H * 0.3f, gear + H * 0.2f, Roles.Glass);
                T(body, -L * 0.41f, W * 0.86f, H * 0.6f, gear + H * 0.3f, -L * 0.5f, W * 0.6f, H * 0.42f, gear + H * 0.32f);
                B(Roles.HullMain, 0, gear + H * 0.66f, -L * 0.1f, W * 0.5f, H * 0.12f, L * 0.42f);
                foreach (var s in new[] { -1f, 1f })
                    B(Roles.Glass, s * W * 0.43f, gear + H * 0.4f, L * 0.02f, W * 0.02f, H * 0.1f, L * 0.4f);
                break;
        }
        // The operator's stripe down both flanks.
        var bodyW = kind switch { "dropship" => W * 0.42f, "striker" => W * 0.24f, _ => W * 0.86f };
        foreach (var s in new[] { -1f, 1f })
            B(Roles.Accent, s * (bodyW / 2 + 0.02f), gear + H * 0.3f, -L * 0.05f, 0.04f, H * 0.06f, L * 0.5f);
        // Two engines at the back, cold.
        var er = MathF.Min(W * 0.1f, H * 0.16f);
        foreach (var s in new[] { -1f, 1f })
        {
            var a = floor + side * (s * bodyW * 0.28f) + up * (gear + H * 0.3f) - f * (L * 0.4f);
            Shapes.Prism(mesh, Roles.DarkMechanics, a, a - f * (L * 0.12f), er, er * 1.15f, 8, MathF.PI / 8, capA: false, capB: true);
        }
        // Landing legs and feet.
        foreach (var (x, z) in new[] { (-0.3f, 0.3f), (0.3f, 0.3f), (-0.3f, -0.3f), (0.3f, -0.3f) })
        {
            var foot = floor + side * (x * bodyW) + f * (z * L);
            Shapes.Beam(mesh, Roles.DarkMechanics, foot, foot + up * (gear + H * 0.06f), MathF.Max(0.08f, L * 0.012f));
            Shapes.OBox(mesh, Roles.DarkMechanics, foot + up * 0.03f, side, up, f, new Vector3(L * 0.03f, 0.03f, L * 0.03f));
        }
        // Navigation lights: red to port, white to starboard and on the tail.
        B(Roles.NavRed, -bodyW / 2 - 0.05f, gear + H * 0.45f, L * 0.2f, 0.06f, 0.12f, 0.12f);
        B(Roles.Light, bodyW / 2 + 0.05f, gear + H * 0.45f, L * 0.2f, 0.06f, 0.12f, 0.12f);
        m.UpdateBounds();
        return m;
    }

    /// <summary>A landing pad painted on a floor: a hazard outline round where a craft stands.</summary>
    public static void Pad(Mesh m, Vector3 centre, Vector3 forward, float length, float width)
    {
        var f = Vector3.Normalize(new Vector3(forward.X, 0, forward.Z));
        var side = Vector3.Cross(Vector3.UnitY, f);
        var t = MathF.Max(0.12f, width * 0.04f);
        var c = centre + Vector3.UnitY * 0.03f;
        foreach (var s in new[] { -1f, 1f })
        {
            Shapes.OBox(m, Roles.Hazard, c + side * (s * width / 2), side, Vector3.UnitY, f, new Vector3(t / 2, 0.02f, length / 2));
            Shapes.OBox(m, Roles.Hazard, c + f * (s * length / 2), side, Vector3.UnitY, f, new Vector3(width / 2, 0.02f, t / 2));
        }
    }
}

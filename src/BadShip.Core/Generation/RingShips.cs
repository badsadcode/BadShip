using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Ring ships: a slim spine hull (the shared hull body, prow, engines and bridge) carrying big chamfered rings on
/// spokes from a collar round the hull. Jobs (kept in the plan's payload):
/// science — one ring with a window band, research dishes and masts on the spine;
/// expedition — twin rings fore and aft, a long-range look;
/// habitat — one huge ring covered in rows of lit windows, more spokes, the "people live here" ship.
/// Decks, the bridge, the underside structure and the kit keep clear of the rings: decks and bellies are only
/// registered between them, and every later part checks its clearance against the rings' bounds.
/// </summary>
public static class RingShips
{
    public static readonly string[] Jobs = ["science", "expedition", "habitat"];

    public static void Plan(BuildContext c)
    {
        var r = c.PlanRng;
        var p = c.Plan;
        var lang = c.Lang;
        p.Family = "ring";
        p.Payload = r.Weighted<string>([("science", 0.4), ("expedition", 0.35), ("habitat", 0.25)]);
        if (c.Recipe.Overrides?.Payload is { } pinned && Jobs.Contains(pinned)) p.Payload = pinned;
        var (lmin, lmax) = p.Size switch { "small" => (60f, 110f), "large" => (200f, 320f), _ => (110f, 200f) };
        p.Length = c.Recipe.Length ?? r.Range(lmin, lmax);
        var L = p.Length;
        p.Beam = L * r.Range(0.13f, 0.18f);
        p.Height = L * r.Range(0.1f, 0.13f);
        p.HullForm = r.Chance(Math.Clamp(lang.Faceted + 0.15, 0, 1)) ? "faceted" : "slab";
        p.ProwStyle = r.Weighted<string>([("faceted", 0.4), ("wedge", 0.35), ("blunt", 0.25)]);
        p.BridgeStyle = r.Weighted<string>([("slope", 0.45), ("overhang", 0.35), ("blunt", 0.2)]);
        p.Tiers = p.Size == "large" ? r.Int(2, 3) : r.Int(1, 2);
        p.EngineLayout = p.Size == "small" ? r.Weighted<string>([("2x1", 0.6), ("1x2", 0.4)]) : r.Weighted<string>([("2x1", 0.3), ("2x2", 0.3), ("3x1", 0.25), ("1x2", 0.15)]);
        p.EngineStyle = r.Chance(lang.PodEngines) ? "pod" : "nacelle";
        p.Asymmetric = false;
        p.Ventral = r.Weighted<string>([("keel", 0.3), ("gondola", 0.2), ("none", 0.5)]);
        c.PreferredPalette = p.Payload switch
        {
            "science" => r.Chance(0.5) ? "research" : null,
            "habitat" => r.Chance(0.4) ? "civilian" : null,
            _ => r.Chance(0.25) ? "research" : null,
        };
    }

    public static void Build(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, B = p.Beam, H = p.Height;
        var engLen = L * r.Range(0.06f, 0.08f);
        var zRear = -L / 2 + engLen;
        var zFront = L / 2;
        var prowLen = Parts.ProwLength(p.ProwStyle, L, r, p.HullForm == "faceted") * 0.8f;
        var body = Parts.HullBody(c, 0, 0, B, H);
        var zBody0 = zRear + L * 0.025f;
        var zProw = zFront - prowLen;
        List<(float, Profile)> rings = [(zRear, body.Scaled(0.93f, 0.9f)), (zBody0, body), (zProw, body)];
        rings.AddRange(Parts.Prow(body, zProw, zFront, p.ProwStyle));
        var (hull, loft) = c.AddLoft("hull_main", "hull", ModuleTier.Primary, null, rings, Roles.HullMain);
        c.AddSides(hull, loft, 1, Profile.FacePosX, Profile.FaceNegX);
        Parts.Engines(c, hull.Id, "", zRear, 0, body.Cy + H * r.Range(-0.05f, 0.1f), B * r.Range(0.7f, 0.95f), H * r.Range(0.9f, 1.15f));

        // Ring stations along the body: (centre z, outer radius, depth).
        var reach = MathF.Sqrt(B * B + H * H) / 2;
        var span = zProw - zBody0;
        var stations = new List<(float z, float R, float depth)>();
        switch (p.Payload)
        {
            case "expedition":
            {
                var R = reach * r.Range(1.9f, 2.5f);
                var d = L * r.Range(0.04f, 0.06f);
                stations.Add((zBody0 + span * r.Range(0.25f, 0.33f), R, d));
                stations.Add((zBody0 + span * r.Range(0.68f, 0.76f), R * r.Range(0.8f, 1f), d));
                break;
            }
            case "habitat":
                stations.Add((zBody0 + span * r.Range(0.42f, 0.55f), reach * r.Range(3f, 3.8f), L * r.Range(0.08f, 0.11f)));
                break;
            default:
                stations.Add((zBody0 + span * r.Range(0.35f, 0.6f), reach * r.Range(2.2f, 2.9f), L * r.Range(0.05f, 0.08f)));
                break;
        }
        foreach (var (z, R, depth) in stations) RingAssembly(c, hull, body, z, R, depth, reach);

        // Decks and bellies only between the rings, so the bridge, equipment and underside keep clear of them.
        var margin = MathF.Max(L * 0.025f, 2f);
        var cuts = stations.Select(s => (a: s.z - s.depth / 2 - margin, b: s.z + s.depth / 2 + margin)).OrderBy(x => x.a).ToList();
        var sections = new List<(float a, float b)>();
        var start = zBody0 + 1;
        foreach (var (a, b) in cuts)
        {
            if (a > start) sections.Add((start, a));
            start = MathF.Max(start, b);
        }
        if (zProw - 0.5f > start) sections.Add((start, zProw - 0.5f));
        sections = sections.Where(s => s.b - s.a > L * 0.06f).ToList();
        Deck? main = null;
        foreach (var (a, b) in sections.OrderByDescending(s => s.b - s.a))
        {
            var d = c.AddDeck(hull, body.Top, -body.TopFlatHalf, body.TopFlatHalf, a, b, 0);
            main ??= d;
            c.AddBelly(hull, body, a, b, main: d == main);
        }
        if (main != null) Parts.Superstructure(c, main, p.Tiers, 0.6f, 0.9f, 0.9f, lengthMin: 0.35f, lengthMax: 0.6f, anchorAt: "aft");
        c.AddEnvelope("habitable", hull, new Aabb(new Vector3(-B * 0.3f, body.Cy, zBody0 + 2), new Vector3(B * 0.3f, body.Top - 1, zProw)));
    }

    /// <summary>A collar round the hull, spokes, and the ring itself, with its window band, lights and panel bands.</summary>
    private static void RingAssembly(BuildContext c, Module hull, Profile body, float z, float R, float depth, float reach)
    {
        var p = c.Plan;
        var r = c.Shape;
        var habitat = p.Payload == "habitat";
        var collarR = reach * 1.02f;
        var collarLen = depth * 1.3f;
        var collar = c.Add("ring_collar", "ring_collar", ModuleTier.Secondary, hull.Id);
        Shapes.Prism(collar.Mesh, Roles.HullSecondary, new Vector3(0, body.Cy, z - collarLen / 2), new Vector3(0, body.Cy, z + collarLen / 2), collarR, collarR, 12, MathF.PI / 12);
        Shapes.Prism(collar.Mesh, Roles.Accent, new Vector3(0, body.Cy, z - collarLen * 0.08f), new Vector3(0, body.Cy, z + collarLen * 0.08f), collarR * 1.03f, collarR * 1.03f, 12, MathF.PI / 12);

        var thick = R * (habitat ? r.Range(0.12f, 0.16f) : r.Range(0.08f, 0.12f));
        var rIn = R - thick;
        var ring = c.Add("ring", "ring", ModuleTier.Primary, collar.Id);
        var segs = habitat ? 48 : 36;
        Shapes.Ring(ring.Mesh, Roles.HullMain, 0, body.Cy, rIn, R, z - depth / 2, z + depth / 2, segs, thick * 0.25f, Roles.HullSecondary, r.Int(3, 6), MathF.PI / segs);
        // Window band round the outside, and on habitats rows on both faces too.
        var win = c.DetailsOf(ring);
        for (var i = 0; i < segs; i++)
        {
            if (r.Chance(habitat ? 0.1 : 0.35)) continue;
            var a = (i + 0.5f) * MathF.Tau / segs + MathF.PI / segs;
            var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            var tangent = new Vector3(-dir.Y, dir.X, 0);
            var seg = R * MathF.Tau / segs;
            Shapes.OBox(win.Mesh, Roles.Glass, new Vector3(0, body.Cy, z) + dir * (R + 0.02f), dir, tangent, Vector3.UnitZ, new Vector3(0.12f, seg * 0.32f, depth * 0.16f));
            if (!habitat) continue;
            foreach (var face in new[] { -1f, 1f })
                for (var row = 0; row < 2; row++)
                {
                    var rr = rIn + thick * (0.3f + row * 0.4f);
                    Shapes.OBox(win.Mesh, Roles.Glass, new Vector3(0, body.Cy, z + face * (depth / 2 + 0.02f)) + dir * rr, dir, tangent, Vector3.UnitZ, new Vector3(thick * 0.1f, seg * 0.28f, 0.1f));
                }
        }
        // Nav lights at the four quarters.
        for (var k = 0; k < 4; k++)
        {
            var a = k * MathF.PI / 2;
            var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            Shapes.Box(win.Mesh, k % 2 == 0 ? Roles.NavRed : Roles.Light, new Vector3(0, body.Cy, z) + dir * (R + 0.4f), new Vector3(0.8f, 0.8f, 0.8f));
        }

        // Spokes from the collar to the ring, on the diagonals so the deck and the belly stay clear.
        var spokes = habitat ? 6 : r.Chance(0.5) ? 4 : 3;
        var sp = c.Add("ring_spokes", "spoke", ModuleTier.Secondary, collar.Id);
        var sr = MathF.Max(0.5f, depth * 0.14f);
        for (var k = 0; k < spokes; k++)
        {
            var a = MathF.PI / 4 + k * MathF.Tau / spokes;
            var dir = new Vector3(MathF.Cos(a), MathF.Sin(a), 0);
            var from = new Vector3(0, body.Cy, z) + dir * collarR * 0.95f;
            var to = new Vector3(0, body.Cy, z) + dir * (rIn + thick * 0.2f);
            foreach (var dz in new[] { -depth * 0.22f, depth * 0.22f })
                Shapes.Prism(sp.Mesh, Roles.HullSecondary, from + new Vector3(0, 0, dz), to + new Vector3(0, 0, dz), sr, sr * 0.85f, 6, 0);
            // A transit tube between each pair of struts.
            Shapes.Prism(sp.Mesh, Roles.DarkMechanics, from, to, sr * 0.7f, sr * 0.7f, 8, MathF.PI / 8);
        }
        c.AddMarker("ring_hub", collar, new Vector3(0, body.Cy, z), Vector3.UnitZ, R);
    }
}

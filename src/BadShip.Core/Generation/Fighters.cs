using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Fighters and gunships in the stealth "badlook": flat facets with sharp creases, chines, sides that lean inwards
/// (tumblehome, the opposite of a boat), arrowhead and hunched silhouettes, canopies blended into the hull instead of
/// stacked superstructure, flat slit exhausts, canted fins and sawtooth trailing edges.
/// Four body forms: dart (sleek arrowhead with delta wings), wedge (one flat faceted delta, the whole craft is the
/// wing), brick (a bulky armoured gunship with a hunched brow) and fork (a cockpit body between two forward prongs).
/// Small craft are not shrunken big ones: everything here is sized for a pilot, a gun and an engine.
/// </summary>
public static class Fighters
{
    public static readonly string[] Forms = ["dart", "wedge", "brick", "fork", "hammer", "twinboom", "brawler"];
    public static readonly string[] Noses = ["point", "chisel", "blunt"];
    public static readonly string[] Cockpits = ["canopy", "slit"];
    public static readonly string[] Layouts = ["1x1", "2x1", "3x1"];
    public static readonly string[] EngineStyles = ["slit", "pod"];

    // ------------------------------------------------------------------ plan

    public static void Plan(BuildContext c)
    {
        var r = c.PlanRng;
        var p = c.Plan;
        p.Family = "fighter";
        (string, double)[] forms = p.Size switch
        {
            "small" => [("dart", 0.3), ("wedge", 0.25), ("brick", 0.1), ("hammer", 0.15), ("brawler", 0.2)],
            "large" => [("wedge", 0.1), ("brick", 0.25), ("fork", 0.22), ("hammer", 0.15), ("twinboom", 0.28)],
            _ => [("dart", 0.18), ("wedge", 0.14), ("brick", 0.14), ("fork", 0.12), ("hammer", 0.14), ("twinboom", 0.14), ("brawler", 0.14)],
        };
        p.HullForm = r.Weighted(forms);
        // A pinned form must also get that form's proportions and nose (the draw above still happens, so pinning
        // the form never reshuffles the other choices).
        if (c.Recipe.Overrides?.HullForm is { } pinned && Forms.Contains(pinned)) p.HullForm = pinned;
        var (lmin, lmax) = p.Size switch { "small" => (8f, 15f), "large" => (26f, 45f), _ => (15f, 26f) };
        p.Length = c.Recipe.Length ?? r.Range(lmin, lmax);
        var L = p.Length;
        (p.Beam, p.Height) = p.HullForm switch
        {
            "dart" => (L * r.Range(0.2f, 0.26f), L * r.Range(0.1f, 0.13f)),
            "wedge" => (L * r.Range(0.5f, 0.68f), L * r.Range(0.08f, 0.11f)),
            "brick" => (L * r.Range(0.28f, 0.36f), L * r.Range(0.17f, 0.23f)),
            "hammer" => (L * r.Range(0.26f, 0.32f), L * r.Range(0.13f, 0.17f)),
            "twinboom" => (L * r.Range(0.42f, 0.55f), L * r.Range(0.1f, 0.13f)),
            "brawler" => (L * r.Range(0.34f, 0.42f), L * r.Range(0.18f, 0.24f)),
            _ => (L * r.Range(0.36f, 0.46f), L * r.Range(0.1f, 0.13f)),
        };
        // The nose: a needle point, a flat chisel tip or a blunt armoured face. Every form can have any of them.
        (string, double)[] noses = p.HullForm switch
        {
            "dart" or "fork" => [("point", 0.5), ("chisel", 0.38), ("blunt", 0.12)],
            "wedge" => [("point", 0.6), ("chisel", 0.4)],
            "brick" => [("point", 0.15), ("chisel", 0.4), ("blunt", 0.45)],
            "twinboom" => [("point", 0.3), ("chisel", 0.45), ("blunt", 0.25)],
            _ => [("point", 0.1), ("chisel", 0.4), ("blunt", 0.5)],
        };
        p.ProwStyle = r.Weighted(noses);
        p.BridgeStyle = r.Chance(p.HullForm is "brick" or "hammer" or "brawler" ? 0.65 : 0.35) ? "slit" : "canopy";
        p.Tiers = 0;
        p.EngineLayout = r.Weighted<string>([("1x1", 0.3), ("2x1", 0.55), ("3x1", 0.15)]);
        if (p.Size == "small" && p.EngineLayout == "3x1") p.EngineLayout = "2x1";
        p.EngineStyle = p.HullForm switch { "brawler" => "pod", "brick" => r.Chance(0.6) ? "pod" : "slit", _ => "slit" };
        p.Payload = "";
        p.Asymmetric = false;
        p.Ventral = "none";
    }

    // ------------------------------------------------------------------ build

    public static void Build(BuildContext c)
    {
        // Bulk: how far the cross-section moves from the knife-edged stealth shape towards an armoured chamfered
        // block. Never all the way to a plain box: the sides keep a little lean and the top keeps its facets.
        var r = c.Shape;
        var (b0, b1) = c.Plan.HullForm switch
        {
            "dart" => (0f, 0.45f), "wedge" => (0f, 0.35f), "fork" => (0f, 0.55f),
            "brick" => (0.3f, 0.9f), "hammer" => (0.4f, 0.9f), "twinboom" => (0.2f, 0.8f), _ => (0.5f, 1f),
        };
        c.Bulk = r.Chance(0.3) ? b0 : r.Range(b0, b1);
        switch (c.Plan.HullForm)
        {
            case "dart": Dart(c); break;
            case "wedge": Wedge(c); break;
            case "brick": Brick(c); break;
            case "fork": Fork(c); break;
            case "hammer": Hammer(c); break;
            case "twinboom": Twinboom(c); break;
            case "brawler": Brawler(c); break;
            default: throw new ArgumentException($"Unknown fighter form '{c.Plan.HullForm}'. Use one of: {string.Join(", ", Forms)}.");
        }
    }

    private static float Bevel(BuildContext c) => Num.Clamp(c.Lang.Chamfer, 0.5f, 1.6f);

    /// <summary>
    /// The stealth cross-section: a flat belly, a small bevelled chine, sides leaning in as they rise and wide top
    /// facets meeting at a narrow ridge. <paramref name="ridge"/> is the top width relative to the bottom.
    /// </summary>
    public static Profile Stealth(float cx, float cy, float w, float h, float bevel, float ridge = 0.34f, float topFacet = 0.42f) =>
        new Profile(cx, cy, w, w * ridge, h, h * topFacet, h * 0.1f * bevel) { TopRun = 1.15f };

    /// <summary>The stealth section, blocked up by the ship's bulk: a wider top, smaller top facets, a bigger chine bevel.</summary>
    public static Profile Section(BuildContext c, float cx, float cy, float w, float h, float bevel, float ridge = 0.34f, float topFacet = 0.42f)
    {
        var b = c.Bulk;
        return new Profile(cx, cy, w, w * float.Lerp(ridge, MathF.Max(ridge, 0.82f), b), h, h * float.Lerp(topFacet, 0.2f, b),
            h * float.Lerp(0.1f, 0.2f, b) * bevel) { TopRun = float.Lerp(1.15f, 1.4f, b) };
    }

    /// <summary>The tip ring for the plan's nose style: the pointed tip as given, a flat chisel, or a blunt armoured face.</summary>
    public static Profile Nose(BuildContext c, Profile pointed, Profile before) => c.Plan.ProwStyle switch
    {
        "chisel" => pointed with
        {
            WBottom = before.WBottom * 0.55f, WTop = before.WTop * 0.5f, H = MathF.Max(pointed.H, before.H * 0.2f),
            ChamferTop = MathF.Max(pointed.H, before.H * 0.2f) * 0.3f, ChamferBottom = 0,
        },
        "blunt" => before.Scaled(0.62f, 0.55f) with { Cy = (pointed.Cy + before.Cy) / 2 },
        _ => pointed,
    };

    /// <summary>The cross-section of a ring list at <paramref name="z"/> (all fields interpolated).</summary>
    public static Profile ProfileAt(IReadOnlyList<(float z, Profile p)> rings, float z)
    {
        if (z <= rings[0].z) return rings[0].p;
        for (var i = 0; i + 1 < rings.Count; i++)
        {
            var (z0, a) = rings[i];
            var (z1, b) = rings[i + 1];
            if (z > z1) continue;
            var t = z1 - z0 < 1e-5f ? 1 : (z - z0) / (z1 - z0);
            return new Profile(float.Lerp(a.Cx, b.Cx, t), float.Lerp(a.Cy, b.Cy, t), float.Lerp(a.WBottom, b.WBottom, t),
                float.Lerp(a.WTop, b.WTop, t), float.Lerp(a.H, b.H, t), float.Lerp(a.ChamferTop, b.ChamferTop, t),
                float.Lerp(a.ChamferBottom, b.ChamferBottom, t)) { TopRun = float.Lerp(a.TopRun, b.TopRun, t) };
        }
        return rings[^1].p;
    }

    /// <summary>The highest point of the profile's outline that is still at least |x| wide: where something x off-centre meets the hull.</summary>
    public static float SurfaceY(Profile p, float x)
    {
        var dx = MathF.Abs(x - p.Cx);
        for (var i = 0; i <= 48; i++)
        {
            var y = p.Top - p.H * i / 48f;
            if (p.HalfWidthAt(y) >= dx) return y;
        }
        return p.Cy;
    }

    /// <summary>
    /// A flat plate with knife edges: the convex planar outline at mid-thickness, both faces inset by
    /// <paramref name="bevel"/>, so every edge is a sharp chamfered crease. Wings, fins, canards and sawtooth teeth.
    /// </summary>
    public static void BevelPlate(Mesh m, string mat, IReadOnlyList<Vector3> outline, float thickness, float bevel)
    {
        var n = Vector3.Zero;
        for (var i = 0; i < outline.Count; i++)
        {
            var a = outline[i];
            var b = outline[(i + 1) % outline.Count];
            n += new Vector3((a.Y - b.Y) * (a.Z + b.Z), (a.Z - b.Z) * (a.X + b.X), (a.X - b.X) * (a.Y + b.Y));
        }
        if (n.LengthSquared() < 1e-10f) return;
        n = Vector3.Normalize(n);
        var ctr = Vector3.Zero;
        foreach (var o in outline) ctr += o;
        ctr /= outline.Count;
        var top = new Vector3[outline.Count];
        var bot = new Vector3[outline.Count];
        for (var i = 0; i < outline.Count; i++)
        {
            var o = outline[i];
            var toC = ctr - o;
            var d = toC.Length();
            var inset = d < 1e-5f ? Vector3.Zero : toC / d * MathF.Min(bevel, d * 0.6f);
            top[i] = o + inset + n * (thickness / 2);
            bot[i] = o + inset - n * (thickness / 2);
        }
        m.AddPolygon(mat, top, n);
        m.AddPolygon(mat, bot, -n);
        for (var i = 0; i < outline.Count; i++)
        {
            var j = (i + 1) % outline.Count;
            var out0 = (outline[i] + outline[j]) * 0.5f - ctr;
            m.AddQuad(mat, outline[i], outline[j], top[j], top[i], out0 + n * out0.Length() * 0.5f);
            m.AddQuad(mat, outline[i], outline[j], bot[j], bot[i], out0 - n * out0.Length() * 0.5f);
        }
    }

    private static Module Body(BuildContext c, List<(float z, Profile p)> rings, string id = "hull_main", string type = "hull", string? parent = null,
        ModuleTier tier = ModuleTier.Primary, string kind = "hull")
    {
        var (m, _) = c.AddLoft(id, type, tier, parent, rings, Roles.HullMain, kind);
        return m;
    }

    // ------------------------------------------------------------------ the forms

    /// <summary>A sleek arrowhead body with cranked delta wings, canted tails and a blended canopy.</summary>
    private static void Dart(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, w = p.Beam, h = p.Height, zR = -L / 2, zN = L / 2;
        var bev = Bevel(c);
        var noseDrop = r.Range(0.15f, 0.55f);
        var hump = r.Range(1f, 1.3f);
        List<(float, Profile)> rings =
        [
            (zR, Section(c, 0, 0, w * 0.8f, h * 0.78f, bev)),
            (zR + L * 0.14f, Section(c, 0, 0, w, h, bev)),
            (zR + L * r.Range(0.5f, 0.58f), Section(c, 0, h * 0.04f, w * 0.86f, h * hump, bev)),
            (zR + L * r.Range(0.78f, 0.84f), Section(c, 0, -h * noseDrop * 0.5f, w * 0.4f, h * 0.6f, bev)),
        ];
        rings.Add((zN, Nose(c, Section(c, 0, -h * noseDrop, w * 0.06f, h * 0.1f, bev), rings[^1].Item2)));
        var hull = Body(c, rings);
        var bottom = -h / 2;

        // Cranked delta wings at the chine line, tips dropped a little (anhedral reads as aggressive).
        var span = L * r.Range(0.2f, 0.32f);
        var wy = bottom + h * 0.2f;
        var droop = span * r.Range(0f, 0.14f);
        var rootFront = zR + L * r.Range(0.5f, 0.62f);
        var tipFront = zR + L * r.Range(0.1f, 0.18f);
        var tipRear = zR + L * r.Range(0.02f, 0.05f);
        var wings = c.Add("wings", "wing", ModuleTier.Secondary, hull.Id);
        var t = MathF.Max(0.12f, h * 0.12f);
        foreach (var s in new[] { 1f, -1f })
        {
            var xt = s * (w / 2 + span);
            Vector3[] outline =
            [
                new(s * w * 0.3f, wy, zR + L * 0.05f), new(s * w * 0.3f, wy, rootFront),
                new(xt, wy - droop, tipFront), new(xt, wy - droop, tipRear),
            ];
            BevelPlate(wings.Mesh, Roles.HullMain, s > 0 ? outline : outline.Reverse().ToArray(), t, t * 0.9f);
            Sawtooth(c, wings, new Vector3(s * w * 0.42f, wy, zR + L * 0.05f), new Vector3(xt, wy - droop, tipRear), t * 0.7f, L, s);
            WingTip(c, wings, new Vector3(xt, wy - droop, (tipFront + tipRear) / 2), t, s);
            if (p.Size != "small" && r.Chance(0.7))
                Missiles(c, wings, new Vector3(s * (w / 2 + span * 0.45f), wy - droop * 0.45f - t / 2, (zR + L * 0.08f + tipFront) / 2 + L * 0.06f), L, r);
        }

        Fins(c, hull, rings, w * r.Range(0.22f, 0.3f), r.Range(18f, 34f), h * r.Range(0.9f, 1.4f), zR + L * 0.03f, L * r.Range(0.16f, 0.2f), L);
        if (r.Chance(0.5)) VentralFins(c, hull, rings, w * 0.3f, h * r.Range(0.4f, 0.7f), zR + L * 0.06f, L * 0.14f);
        if (r.Chance(0.4)) Canards(c, hull, rings, zR + L * r.Range(0.66f, 0.72f), L * 0.1f, w * r.Range(0.3f, 0.45f), -h * 0.1f);
        Cockpit(c, hull, rings, zR + L * 0.52f, zR + L * 0.8f, w * 0.34f, h * 0.45f);
        Intakes(c, hull, rings, zR + L * 0.3f, zR + L * 0.52f, w * 0.26f, h * 0.42f, -h * 0.12f);
        Guns(c, hull, [new(w * 0.4f, bottom + h * 0.18f, zR + L * 0.62f), new(-w * 0.4f, bottom + h * 0.18f, zR + L * 0.62f)], L * 0.2f, L);
        SlitEngines(c, hull, rings[0].Item2, zR, L);
        GlowLines(c, hull);
    }

    /// <summary>One flat faceted delta: the craft is its own wing. V-tails, sawtooth tips, a low faceted canopy.</summary>
    private static void Wedge(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, w = p.Beam, h = p.Height, zR = -L / 2, zN = L / 2;
        var bev = Bevel(c);
        var ridge = r.Range(0.14f, 0.24f);
        List<(float, Profile)> rings =
        [
            (zR, Section(c, 0, 0, w * 0.9f, h * 0.55f, bev, ridge * 1.3f)),
            (zR + L * 0.12f, Section(c, 0, 0, w, h * 0.8f, bev, ridge)),
            (zR + L * 0.45f, Section(c, 0, h * 0.04f, w * 0.62f, h, bev, ridge * 1.2f)),
            (zR + L * 0.75f, Section(c, 0, -h * 0.06f, w * 0.28f, h * 0.68f, bev, ridge * 1.4f)),
        ];
        rings.Add((zN, Nose(c, Section(c, 0, -h * 0.16f, w * 0.03f, h * 0.1f, bev), rings[^1].Item2)));
        var hull = Body(c, rings);
        SlitEngines(c, hull, rings[0].Item2 with { WBottom = w * 0.5f, WTop = w * 0.5f * ridge * 1.3f }, zR, L);
        var wy = -h * 0.5f + 0.05f;

        // Sawtooth points trailing from the outer rear corners: the "W" trailing edge.
        var t = MathF.Max(0.1f, h * 0.1f);
        var back = L * r.Range(0.07f, 0.13f);
        foreach (var s in new[] { 1f, -1f })
        {
            // One module per side: a single box spanning both would sit across the exhausts.
            var teeth = c.Add(s > 0 ? "trailing_edge_port" : "trailing_edge_starboard", "wing", ModuleTier.Secondary, hull.Id);
            Vector3[][] set =
            [
                [new(s * w * 0.46f, wy, zR + L * 0.1f), new(s * w * 0.38f, wy, zR + L * 0.03f), new(s * w * 0.44f, wy, zR - back)],
                [new(s * w * 0.37f, wy, zR + L * 0.05f), new(s * w * 0.29f, wy, zR + L * 0.02f), new(s * w * 0.34f, wy, zR - back * 0.6f)],
            ];
            WingTip(c, teeth, new Vector3(s * w * 0.45f, wy, zR + L * 0.1f), t, s);
            // The module's box is what the validator checks, so test each tooth together with what is already there.
            foreach (var tooth in set)
                if (!InExhaust(c, tooth.Concat([teeth.Mesh.Bounds().Min, teeth.Mesh.Bounds().Max]).ToArray(), t))
                    BevelPlate(teeth.Mesh, Roles.HullMain, tooth, t, t * 0.8f);
        }

        Fins(c, hull, rings, w * r.Range(0.1f, 0.16f), r.Range(32f, 45f), h * r.Range(1.3f, 1.9f), zR + L * 0.04f, L * r.Range(0.18f, 0.24f), L);
        Cockpit(c, hull, rings, zR + L * 0.48f, zR + L * 0.72f, w * 0.14f, h * 0.42f);
        Guns(c, hull, [new(w * 0.08f, -h * 0.42f, zR + L * 0.72f), new(-w * 0.08f, -h * 0.42f, zR + L * 0.72f)], L * 0.12f, L);
        GlowLines(c, hull);
    }

    /// <summary>A bulky armoured gunship: a heavy chamfered block with a hunched brow and a beak, side nacelles, stub wings with gun pods.</summary>
    private static void Brick(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, w = p.Beam, h = p.Height, zR = -L / 2, zN = L / 2;
        var bev = Bevel(c);
        var b = new Profile(0, 0, w * 0.92f, w * r.Range(0.66f, 0.8f), h, h * 0.3f * bev, h * 0.2f * bev) { TopRun = r.Range(1.2f, 1.7f) };
        var brow = r.Range(1.12f, 1.32f);
        List<(float, Profile)> rings =
        [
            (zR, b.Scaled(0.9f, 0.72f) with { Cy = -h * 0.12f }),
            (zR + L * 0.08f, b.Scaled(1f, 0.85f) with { Cy = -h * 0.06f }),
            (zR + L * r.Range(0.5f, 0.58f), b with { Cy = h * (brow - 1) / 2, H = h * brow }),
            (zR + L * r.Range(0.76f, 0.82f), b.Scaled(0.72f, 0.68f) with { Cy = -h * 0.1f }),
        ];
        rings.Add((zN, Nose(c, b.Scaled(0.3f, 0.26f) with { Cy = -h * 0.24f }, rings[^1].Item2)));
        var hull = Body(c, rings);

        var pods = p.EngineStyle == "pod";
        var podW = w * r.Range(0.3f, 0.38f);
        var podX = w / 2 + podW * 0.2f;
        if (pods) Nacelles(c, hull, podX, -h * 0.12f, podW, h * r.Range(0.5f, 0.62f), zR, L * r.Range(0.42f, 0.52f), L, bev);
        else SlitEngines(c, hull, rings[0].Item2, zR, L);

        // Stub wings with gun pods on the tips.
        var wings = c.Add("wings", "wing", ModuleTier.Secondary, hull.Id);
        var span = (pods ? podX + podW / 2 : w / 2) + L * r.Range(0.1f, 0.16f);
        var wy = -h * 0.2f;
        var t = MathF.Max(0.15f, h * 0.1f);
        foreach (var s in new[] { 1f, -1f })
        {
            Vector3[] outline = [new(s * w * 0.3f, wy, zR + L * 0.14f), new(s * w * 0.3f, wy, zR + L * 0.44f), new(s * span, wy, zR + L * 0.3f), new(s * span, wy, zR + L * 0.16f)];
            BevelPlate(wings.Mesh, Roles.HullSecondary, s > 0 ? outline : outline.Reverse().ToArray(), t, t);
            var podC = new Vector3(s * span, wy, zR + L * 0.26f);
            var pw = MathF.Max(0.35f, h * 0.22f);
            var ring = Section(c, podC.X, podC.Y, pw * 1.3f, pw, bev);
            var (gp, _) = c.AddLoft("gun_pod", "gun_pod", ModuleTier.Secondary, wings.Id,
                [(zR + L * 0.12f, ring.Scaled(0.8f, 0.8f)), (zR + L * 0.16f, ring), (zR + L * 0.38f, ring), (zR + L * 0.44f, ring.Scaled(0.6f, 0.6f))], Roles.HullSecondary, null);
            Guns(c, gp, [new(podC.X, podC.Y, zR + L * 0.42f)], L * 0.14f, L);
            WingTip(c, wings, new Vector3(s * span, wy + pw * 0.5f, zR + L * 0.2f), t, s);
            if (r.Chance(0.5)) Missiles(c, wings, new Vector3(s * (span * 0.62f + w * 0.12f), wy - t / 2, zR + L * 0.28f), L, r);
        }

        if (r.Chance(0.6)) Fins(c, hull, rings, w * r.Range(0.15f, 0.3f), r.Range(10f, 28f), h * r.Range(0.5f, 0.8f), zR + L * 0.04f, L * 0.16f, L);
        if (r.Chance(0.5)) VentralFins(c, hull, rings, w * 0.3f, h * r.Range(0.3f, 0.5f), zR + L * 0.06f, L * 0.16f);
        Cockpit(c, hull, rings, zR + L * 0.56f, zR + L * 0.8f, w * 0.46f, h * 0.3f);
        Guns(c, hull, [new(w * 0.08f, -h * 0.36f, zR + L * 0.86f), new(-w * 0.08f, -h * 0.36f, zR + L * 0.86f)], L * 0.16f, L, chin: true);
        GlowLines(c, hull);
    }

    /// <summary>A short cockpit body between two long forward prongs (mandibles) joined by a flat wing; guns at the prong tips.</summary>
    private static void Fork(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, w = p.Beam, h = p.Height, zR = -L / 2, zN = L / 2;
        var bev = Bevel(c);
        var wb = w * 0.36f;
        var bodyNose = zR + L * r.Range(0.78f, 0.86f);
        List<(float, Profile)> rings =
        [
            (zR, Section(c, 0, 0, wb * 0.85f, h * 0.8f, bev)),
            (zR + L * 0.12f, Section(c, 0, 0, wb, h, bev)),
            (zR + L * 0.5f, Section(c, 0, h * 0.05f, wb * 0.9f, h * 1.08f, bev)),
        ];
        rings.Add((bodyNose, Nose(c, Section(c, 0, -h * 0.12f, wb * 0.12f, h * 0.2f, bev), rings[^1].Item2)));
        var hull = Body(c, rings);

        var pw = w * r.Range(0.18f, 0.24f);
        var ph = h * r.Range(0.6f, 0.8f);
        var px = w / 2 - pw / 2;
        var py = -h * 0.12f;
        var pz0 = zR + L * r.Range(0.12f, 0.2f);
        var wy = py - ph * 0.15f;
        var wing = c.Add("wing", "wing", ModuleTier.Secondary, hull.Id);
        var t = MathF.Max(0.15f, h * 0.12f);
        foreach (var s in new[] { 1f, -1f })
        {
            Vector3[] plate = [new(0, wy, pz0 + L * 0.02f), new(s * px, wy, pz0 + L * 0.12f), new(s * px, wy, pz0 + L * 0.36f), new(0, wy, pz0 + L * 0.5f)];
            BevelPlate(wing.Mesh, Roles.HullSecondary, plate, t, t);
        }
        if (r.Chance(0.6)) VentralFins(c, hull, rings, wb * 0.3f, h * r.Range(0.4f, 0.7f), zR + L * 0.05f, L * 0.14f);
        foreach (var s in new[] { 1f, -1f })
        {
            var pr = Section(c, s * px, py, pw, ph, bev, 0.3f);
            var tipDrop = -ph * r.Range(0.1f, 0.3f);
            var prong = Body(c,
            [
                (pz0, pr.Scaled(0.8f, 0.75f)), (pz0 + L * 0.08f, pr), (zN - L * 0.28f, pr),
                (zN, Nose(c, pr.Scaled(0.25f, 0.35f) with { Cy = py + tipDrop }, pr)),
            ], s > 0 ? "prong_port" : "prong_starboard", "prong", wing.Id, ModuleTier.Secondary);
            Guns(c, prong, [new(s * (px - pw * 0.42f), py - ph * 0.2f, zN - L * 0.2f)], L * 0.24f, L);
            WingTip(c, wing, new Vector3(s * (px + pw * 0.4f), py, pz0 + L * 0.2f), t, s);
        }

        Fins(c, hull, rings, wb * 0.3f, r.Range(20f, 36f), h * r.Range(0.9f, 1.3f), zR + L * 0.03f, L * 0.16f, L);
        Cockpit(c, hull, rings, zR + L * 0.45f, bodyNose - L * 0.06f, wb * 0.42f, h * 0.45f);
        SlitEngines(c, hull, rings[0].Item2, zR, L);
        GlowLines(c, hull);
    }


    /// <summary>A narrow armoured neck carrying a wide head block: cockpit slits and guns in the head, swept stubs at the tail.</summary>
    private static void Hammer(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, w = p.Beam, h = p.Height, zR = -L / 2, zN = L / 2;
        var bev = Bevel(c);
        var b = Section(c, 0, 0, w * 0.85f, h, bev, 0.5f, 0.32f);
        var head = r.Range(1.08f, 1.28f);
        var headFront = zR + L * r.Range(0.82f, 0.88f);
        List<(float, Profile)> rings =
        [
            (zR, b.Scaled(0.85f, 0.8f)),
            (zR + L * 0.1f, b),
            (zR + L * r.Range(0.4f, 0.46f), b.Scaled(r.Range(0.62f, 0.75f), 0.82f) with { Cy = -h * 0.06f }),
            (zR + L * r.Range(0.56f, 0.6f), b.Scaled(head, 1.08f) with { Cy = h * 0.04f }),
            (headFront, b.Scaled(head, 1.05f) with { Cy = h * 0.03f }),
        ];
        rings.Add((zN, Nose(c, b.Scaled(0.2f, 0.25f) with { Cy = -h * 0.12f }, rings[^1].Item2)));
        var hull = Body(c, rings);

        var wings = c.Add("wings", "wing", ModuleTier.Secondary, hull.Id);
        var span = L * r.Range(0.1f, 0.2f);
        var wy = -h * 0.22f;
        var droop = span * r.Range(-0.1f, 0.25f);
        var t = MathF.Max(0.12f, h * 0.1f);
        foreach (var s in new[] { 1f, -1f })
        {
            var xt = s * (w * 0.42f + span);
            Vector3[] o = [new(s * w * 0.3f, wy, zR + L * 0.07f), new(s * w * 0.3f, wy, zR + L * 0.38f), new(xt, wy - droop, zR + L * 0.2f), new(xt, wy - droop, zR + L * 0.06f)];
            BevelPlate(wings.Mesh, Roles.HullMain, o, t, t);
            WingTip(c, wings, new Vector3(xt, wy - droop, zR + L * 0.13f), t, s);
        }
        var hx = w * 0.85f * head * 0.36f;
        Guns(c, hull, [new(hx, -h * 0.38f, headFront - L * 0.08f), new(-hx, -h * 0.38f, headFront - L * 0.08f)], L * 0.2f, L);
        Fins(c, hull, rings, w * r.Range(0.18f, 0.28f), r.Range(8f, 26f), h * r.Range(0.7f, 1.05f), zR + L * 0.03f, L * r.Range(0.16f, 0.2f), L);
        if (r.Chance(0.4)) VentralFins(c, hull, rings, w * 0.25f, h * r.Range(0.3f, 0.5f), zR + L * 0.05f, L * 0.13f);
        Cockpit(c, hull, rings, zR + L * 0.6f, headFront + L * 0.03f, w * 0.5f, h * 0.3f);
        SlitEngines(c, hull, rings[0].Item2, zR, L);
        GlowLines(c, hull);
    }

    /// <summary>Two blocky booms carrying the engines and tails, joined by a crossbar wing to a cockpit pod between them.</summary>
    private static void Twinboom(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, w = p.Beam, h = p.Height, zR = -L / 2, zN = L / 2;
        var bev = Bevel(c);
        var wb = w * r.Range(0.26f, 0.34f);
        var pod0 = zR + L * r.Range(0.24f, 0.32f);
        List<(float, Profile)> rings =
        [
            (pod0, Section(c, 0, 0, wb * 0.8f, h * 0.8f, bev)),
            (pod0 + L * 0.1f, Section(c, 0, 0, wb, h, bev)),
            (zR + L * 0.74f, Section(c, 0, h * 0.05f, wb * 0.92f, h * 1.06f, bev)),
        ];
        rings.Add((zN, Nose(c, Section(c, 0, -h * 0.14f, wb * 0.1f, h * 0.15f, bev), rings[^1].Item2)));
        var hull = Body(c, rings);

        var bw = w * r.Range(0.18f, 0.24f);
        var bh = h * r.Range(0.8f, 1f);
        var bx = w / 2 - bw / 2;
        var by = -h * 0.06f;
        var boomFront = zN - L * r.Range(0.06f, 0.16f);
        var wing = c.Add("wing", "wing", ModuleTier.Secondary, hull.Id);
        var wy = by - bh * 0.1f;
        var t = MathF.Max(0.15f, h * 0.12f);
        var outer = r.Chance(0.5) ? L * r.Range(0.06f, 0.12f) : 0;
        foreach (var s in new[] { 1f, -1f })
        {
            BevelPlate(wing.Mesh, Roles.HullSecondary, [new(0, wy, pod0 + L * 0.04f), new(s * bx, wy, pod0 + L * 0.06f), new(s * bx, wy, pod0 + L * 0.3f), new(0, wy, pod0 + L * 0.36f)], t, t);
            if (outer > 0)
            {
                BevelPlate(wing.Mesh, Roles.HullMain, [new(s * bx, wy, zR + L * 0.1f), new(s * bx, wy, zR + L * 0.34f), new(s * (bx + bw / 2 + outer), wy, zR + L * 0.16f), new(s * (bx + bw / 2 + outer), wy, zR + L * 0.09f)], t * 0.8f, t * 0.8f);
                WingTip(c, wing, new Vector3(s * (bx + bw / 2 + outer), wy, zR + L * 0.12f), t, s);
            }
        }
        var boomRings = new List<(float, Profile)>();
        foreach (var s in new[] { 1f, -1f })
        {
            var pr = Section(c, s * bx, by, bw, bh, bev, 0.5f, 0.3f);
            List<(float, Profile)> br =
            [
                (zR, pr.Scaled(0.9f, 0.85f)), (zR + L * 0.06f, pr), (boomFront - L * 0.14f, pr),
            ];
            br.Add((boomFront, Nose(c, pr.Scaled(0.3f, 0.4f) with { Cy = by - bh * 0.1f }, pr)));
            var boom = Body(c, br, s > 0 ? "boom_port" : "boom_starboard", "boom", wing.Id, ModuleTier.Secondary, "pod");
            SlitEngines(c, boom, br[0].Item2, zR, L, p.EngineLayout == "1x1" ? "1x1" : "2x1", s > 0 ? "engine_port" : "engine_starboard");
            Guns(c, boom, [new(s * (bx - bw * 0.3f), by - bh * 0.25f, boomFront - L * 0.12f)], L * 0.16f, L);
            if (s > 0)
            {
                boomRings = br;
                Fins(c, boom, br, bx, r.Range(4f, 20f), h * r.Range(0.8f, 1.2f), zR + L * 0.03f, L * r.Range(0.14f, 0.2f), L);
            }
        }
        Guns(c, hull, [new(wb * 0.22f, -h * 0.36f, zR + L * 0.84f), new(-wb * 0.22f, -h * 0.36f, zR + L * 0.84f)], L * 0.12f, L, chin: true);
        Cockpit(c, hull, rings, zR + L * 0.55f, zR + L * 0.86f, wb * 0.5f, h * 0.45f);
        GlowLines(c, hull);
    }

    /// <summary>Short and stout: an armoured body with big engine blocks on its shoulders, layered flank armour and drooped gun stubs.</summary>
    private static void Brawler(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, w = p.Beam, h = p.Height, zR = -L / 2, zN = L / 2;
        var bev = Bevel(c);
        var b = Section(c, 0, 0, w * 0.62f, h, bev, 0.6f, 0.3f);
        List<(float, Profile)> rings =
        [
            (zR, b.Scaled(0.9f, 0.8f)),
            (zR + L * 0.1f, b),
            (zR + L * r.Range(0.56f, 0.64f), b with { H = h * 1.1f, Cy = h * 0.05f }),
            (zR + L * r.Range(0.8f, 0.86f), b.Scaled(0.75f, 0.75f) with { Cy = -h * 0.05f }),
        ];
        rings.Add((zN, Nose(c, b.Scaled(0.28f, 0.3f) with { Cy = -h * 0.15f }, rings[^1].Item2)));
        var hull = Body(c, rings);

        var nw = w * r.Range(0.26f, 0.32f);
        Nacelles(c, hull, w * 0.31f + nw * 0.25f, h * r.Range(0.05f, 0.2f), nw, h * r.Range(0.5f, 0.62f), zR, L * r.Range(0.55f, 0.68f), L, bev);
        if (r.Chance(0.5)) SlitEngines(c, hull, rings[0].Item2, zR, L, "1x1");

        // Layered flank armour: overlapping plates like scales, each standing a little prouder than the one behind.
        var loft = hull.Lofts[0];
        var armour = c.Add("armour", "armour", ModuleTier.Secondary, hull.Id);
        var at = MathF.Max(0.08f, h * 0.06f);
        foreach (var f in new[] { Profile.FaceBottomPosX, Profile.FaceBottomNegX })
        {
            var n = loft.FaceNormal(1, f);
            if (n == Vector3.Zero) continue;
            var count = r.Int(2, 4);
            for (var i = 0; i < count; i++)
            {
                var u0 = i / (float)(count + 0.4f);
                var u1 = MathF.Min(1, u0 + 1.4f / count);
                var lift = n * (at * (0.6f + i * 0.5f));
                Vector3[] o = [loft.FacePoint(1, f, u0, 0.05f) + lift, loft.FacePoint(1, f, u1, 0.05f) + lift, loft.FacePoint(1, f, u1, 0.95f) + lift, loft.FacePoint(1, f, u0, 0.95f) + lift];
                BevelPlate(armour.Mesh, Roles.HullSecondary, o, at, at * 0.9f);
                c.UseSurface(BuildContext.FaceKey(hull, 1, f), 0, 1, 0, 1);
            }
        }

        // Drooped gun stubs.
        var wings = c.Add("wings", "wing", ModuleTier.Secondary, hull.Id);
        var span = L * r.Range(0.12f, 0.18f);
        var wy = -h * 0.25f;
        var t = MathF.Max(0.14f, h * 0.1f);
        foreach (var s in new[] { 1f, -1f })
        {
            var xt = s * (w * 0.5f + span);
            var drop = span * r.Range(0.2f, 0.45f);
            Vector3[] o = [new(s * w * 0.2f, wy, zR + L * 0.3f), new(s * w * 0.2f, wy, zR + L * 0.56f), new(xt, wy - drop, zR + L * 0.48f), new(xt, wy - drop, zR + L * 0.34f)];
            BevelPlate(wings.Mesh, Roles.HullMain, o, t, t);
            Guns(c, wings, [new(xt, wy - drop - t, zR + L * 0.46f)], L * 0.16f, L);
            WingTip(c, wings, new Vector3(xt, wy - drop + t, zR + L * 0.38f), t, s);
        }
        if (r.Chance(0.4)) Fins(c, hull, rings, w * 0.18f, r.Range(5f, 20f), h * r.Range(0.4f, 0.7f), zR + L * 0.04f, L * 0.16f, L);
        Guns(c, hull, [new(w * 0.1f, -h * 0.4f, zR + L * 0.8f), new(-w * 0.1f, -h * 0.4f, zR + L * 0.8f)], L * 0.14f, L, chin: true);
        Cockpit(c, hull, rings, zR + L * 0.6f, zR + L * 0.88f, w * 0.38f, h * 0.3f);
        GlowLines(c, hull);
    }

    // ------------------------------------------------------------------ components

    /// <summary>
    /// The cockpit, blended into the hull: a faceted glass canopy, or an armoured brow with narrow glass slits (the
    /// "eyes"). Its base follows the hull's surface ring by ring so it never floats.
    /// </summary>
    private static void Cockpit(BuildContext c, Module hull, List<(float z, Profile p)> body, float z0, float z1, float width, float height)
    {
        var slit = c.Plan.BridgeStyle == "slit";
        var len = z1 - z0;
        (float z, float w, float up)[] stations = slit
            ? [(z0, 0.75f, 0.4f), (z0 + len * 0.3f, 1f, 1f), (z0 + len * 0.62f, 0.95f, 0.92f), (z1, 0.5f, 0.15f)]
            : [(z0, 0.55f, 0.3f), (z0 + len * 0.38f, 1f, 1f), (z1, 0.32f, 0.12f)];
        var rings = new List<(float, Profile)>();
        foreach (var (z, ws, up) in stations)
        {
            var bp = ProfileAt(body, z);
            var cw = width * ws;
            var baseY = SurfaceY(bp, cw * 0.5f);
            var top = MathF.Max(baseY + height * up, bp.Top + height * up * 0.45f);
            var bottom = baseY - height * 0.3f;
            var hh = top - bottom;
            rings.Add((z, new Profile(0, (top + bottom) / 2, cw, cw * (slit ? 0.62f : 0.42f), hh, hh * (slit ? 0.28f : 0.4f), 0) { TopRun = slit ? 1.6f : 1f }));
        }
        var (m, loft) = c.AddLoft("cockpit", "cockpit", ModuleTier.Secondary, hull.Id, rings, slit ? Roles.HullSecondary : Roles.Glass, slit ? "tier" : null);
        if (slit)
        {
            // Glass slits across the sloped front facets.
            var s = loft.Segments - 1;
            foreach (var f in new[] { Profile.FaceTopPosX, Profile.FaceTop, Profile.FaceTopNegX })
            {
                var v0 = f == Profile.FaceTop ? 0.08f : 0.25f;
                Shapes.Patch(m.Mesh, Roles.Glass, loft, s, f, 0.1f, 0.42f, v0, 1 - v0, 0.03f);
                c.UseSurface(BuildContext.FaceKey(m, s, f), 0, 1, 0, 1);
            }
        }
        else
        {
            // A frame bar along the canopy's crest and one across it.
            var mid = loft.Rings[1];
            Shapes.Beam(m.Mesh, Roles.HullSecondary, (mid[4] + mid[5]) / 2 + new Vector3(0, 0.02f, 0), (loft.Rings[2][4] + loft.Rings[2][5]) / 2, MathF.Max(0.05f, height * 0.06f));
            for (var i = 2; i < 7; i++)
                Shapes.Beam(m.Mesh, Roles.HullSecondary, mid[i], mid[i + 1], MathF.Max(0.05f, height * 0.06f));
        }
        var crest = (rings[1].Item2.Top + rings[1].Item2.Cy) / 2;
        c.AddMarker("bridge", m, new Vector3(0, crest, rings[1].Item1), Vector3.UnitZ);
    }

    /// <summary>Canted tail fins: knife-edged trapezoids standing on the hull's top facets, leaning outwards.</summary>
    private static void Fins(BuildContext c, Module hull, List<(float z, Profile p)> body, float x, float cantDeg, float height, float z0, float rootChord, float L)
    {
        var r = c.Shape;
        var m = c.Add(hull.Id == "hull_main" ? "fins" : hull.Id + "_fins", "fin", ModuleTier.Secondary, hull.Id);
        var cant = cantDeg * MathF.PI / 180;
        var sweep = rootChord * r.Range(0.45f, 0.8f);
        var tip = rootChord * r.Range(0.3f, 0.5f);
        var thick = MathF.Max(0.08f, height * 0.07f);
        foreach (var s in new[] { 1f, -1f })
        {
            // Off-centre bodies (booms) are mirrored to the side being built.
            Profile At(float z) { var bp = ProfileAt(body, z); return bp with { Cx = s * MathF.Abs(bp.Cx) }; }
            var y = MathF.Min(SurfaceY(At(z0), s * x), SurfaceY(At(z0 + rootChord), s * x)) - thick;
            var up = new Vector3(s * MathF.Sin(cant), MathF.Cos(cant), 0);
            var r0 = new Vector3(s * x, y, z0);
            var r1 = r0 + new Vector3(0, 0, rootChord);
            var t0 = r0 + up * height + new Vector3(0, 0, sweep);
            var t1 = t0 + new Vector3(0, 0, tip);
            BevelPlate(m.Mesh, Roles.HullMain, s > 0 ? [r0, r1, t1, t0] : [t0, t1, r1, r0], thick, thick * 0.9f);
            // A thin glowing line along the leading edge, and a light at the tip.
            Shapes.Beam(m.Mesh, Roles.Glow, Vector3.Lerp(r1, t1, 0.15f) + up * 0.01f, Vector3.Lerp(r1, t1, 0.85f), MathF.Max(0.03f, thick * 0.4f));
            Shapes.Box(m.Mesh, Roles.Light, t1 - new Vector3(0, 0, tip * 0.3f), new Vector3(thick * 1.2f, thick * 1.2f, thick * 1.5f));
        }
    }

    /// <summary>Faceted air-intake-like side housings (sensor / cooling scoops) with dark mouths.</summary>
    private static void Intakes(BuildContext c, Module hull, List<(float z, Profile p)> body, float z0, float z1, float iw, float ih, float y)
    {
        foreach (var s in new[] { 1f, -1f })
        {
            var bp = ProfileAt(body, (z0 + z1) / 2);
            var x = s * (bp.HalfWidthAt(y) + iw * 0.15f);
            var pr = new Profile(x, y, iw, iw * 0.62f, ih, ih * 0.35f, ih * 0.12f);
            var (m, loft) = c.AddLoft(s > 0 ? "intake_port" : "intake_starboard", "intake", ModuleTier.Secondary, hull.Id,
                [(z0, pr.Scaled(0.55f, 0.6f)), (z0 + (z1 - z0) * 0.35f, pr), (z1, pr.Scaled(1f, 1.08f))], Roles.HullSecondary, null);
            var front = loft.Rings[^1];
            var ctr = new Vector3(x, pr.Cy, z1);
            m.Mesh.AddPolygon(Roles.DarkMechanics, front.Select(q => ctr + (q - ctr) * 0.78f + new Vector3(0, 0, 0.02f)).ToArray(), Vector3.UnitZ);
        }
    }

    /// <summary>Gun barrels pointing forward, each with a hardpoint marker at the muzzle.</summary>
    private static void Guns(BuildContext c, Module parent, Vector3[] mounts, float length, float L, bool chin = false)
    {
        var m = c.Add(parent.Id + "_guns", "gun", ModuleTier.Secondary, parent.Id);
        var rad = Num.Clamp(L * 0.008f, 0.06f, 0.3f);
        foreach (var a in mounts)
        {
            var muzzle = a + new Vector3(0, 0, length);
            if (chin)
            {
                // A chin turret: an armoured faceted block with the barrels.
                Shapes.Prism(m.Mesh, Roles.DarkMechanics, a - new Vector3(0, 0, length * 0.15f), a + new Vector3(0, 0, length * 0.25f), rad * 3.2f, rad * 2.4f, 6, MathF.PI / 6);
            }
            else Shapes.Prism(m.Mesh, Roles.HullSecondary, a - new Vector3(0, 0, length * 0.3f), a + new Vector3(0, 0, length * 0.15f), rad * 2.2f, rad * 1.8f, 6, MathF.PI / 6);
            Shapes.Prism(m.Mesh, Roles.DarkMechanics, a, muzzle, rad, rad * 0.85f, 6, MathF.PI / 6);
            Shapes.Prism(m.Mesh, Roles.DarkMechanics, muzzle - new Vector3(0, 0, length * 0.12f), muzzle, rad * 1.4f, rad * 1.4f, 6, MathF.PI / 6);
            c.AddMarker("hardpoint", m, muzzle, Vector3.UnitZ, rad * 1.4f);
        }
    }

    /// <summary>A pair of missiles on a pylon under a wing.</summary>
    private static void Missiles(BuildContext c, Module wing, Vector3 hang, float L, Rng r)
    {
        var m = c.Add(wing.Id + "_missiles", "missile_rack", ModuleTier.Secondary, wing.Id);
        var rad = Num.Clamp(L * 0.012f, 0.1f, 0.35f);
        var len = Num.Clamp(L * 0.16f, 1.2f, 4.5f);
        var pylon = rad * 1.4f;
        Shapes.Box(m.Mesh, Roles.DarkMechanics, hang - new Vector3(0, pylon / 2 - 0.02f, 0), new Vector3(rad * 0.8f, pylon, len * 0.45f));
        var n = r.Chance(0.5) ? 2 : 1;
        for (var i = 0; i < n; i++)
        {
            var x = hang.X + (n == 1 ? 0 : (i - 0.5f) * rad * 2.6f);
            var y = hang.Y - pylon - rad * 0.9f;
            var a = new Vector3(x, y, hang.Z - len / 2);
            var b = new Vector3(x, y, hang.Z + len * 0.32f);
            Shapes.Prism(m.Mesh, Roles.HullSecondary, a, b, rad, rad, 6, MathF.PI / 6);
            Shapes.Prism(m.Mesh, Roles.Accent, b, b + new Vector3(0, 0, len * 0.18f), rad, rad * 0.15f, 6, MathF.PI / 6);
            Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(x, y, hang.Z - len * 0.4f), new Vector3(rad * 3f, rad * 0.25f, len * 0.12f));
            Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(x, y, hang.Z - len * 0.4f), new Vector3(rad * 0.25f, rad * 3f, len * 0.12f));
        }
        c.AddMarker("hardpoint", m, hang - new Vector3(0, pylon, 0), -Vector3.UnitY, rad * 2);
    }

    /// <summary>Triangular teeth along a trailing edge (from a to b), pointing aft: the serrated stealth edge.</summary>
    private static void Sawtooth(BuildContext c, Module m, Vector3 a, Vector3 b, float thick, float L, float side)
    {
        var edge = b - a;
        var n = Math.Clamp((int)(edge.Length() / MathF.Max(0.6f, L * 0.05f)), 2, 6);
        var depth = MathF.Min(L * 0.035f, edge.Length() / n * 0.8f);
        for (var i = 0; i < n; i++)
        {
            var p0 = a + edge * (i / (float)n);
            var p1 = a + edge * ((i + 1) / (float)n);
            var tipP = (p0 + p1) / 2 - new Vector3(0, 0, depth);
            var front = (p0 + p1) / 2 + new Vector3(0, 0, depth * 0.6f);
            Vector3[] tooth = [p0, front, p1, tipP];
            BevelPlate(m.Mesh, Roles.HullMain, side > 0 ? tooth : tooth.Reverse().ToArray(), thick, thick * 0.8f);
        }
    }

    /// <summary>A wingtip light block: red nav light to port, white to starboard (the reference ships have no green).</summary>
    private static void WingTip(BuildContext c, Module m, Vector3 at, float t, float side)
    {
        var s = MathF.Max(0.08f, t * 0.9f);
        Shapes.Box(m.Mesh, side > 0 ? Roles.NavRed : Roles.Light, at, new Vector3(s * 1.4f, s, s * 2f));
    }

    /// <summary>Flat slit exhausts in the rear face: a dark frame and a glowing slot per engine, with exhaust markers.</summary>
    private static void SlitEngines(BuildContext c, Module parent, Profile rear, float zR, float L, string? layout = null, string id = "engines")
    {
        var n = (layout ?? c.Plan.EngineLayout) switch { "3x1" => 3, "2x1" => 2, _ => 1 };
        var m = c.Add(id, "engine", ModuleTier.Secondary, parent.Id);
        var usable = rear.WBottom * 0.68f;
        var ew = usable / n * 0.86f;
        var eh = MathF.Max(0.12f, rear.H * 0.26f);
        var y = rear.Cy - rear.H * 0.12f;
        var d = MathF.Max(0.25f, L * 0.03f);
        for (var i = 0; i < n; i++)
        {
            var x = rear.Cx + (n == 1 ? 0 : (i - (n - 1) / 2f) * usable / n);
            Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(x, y, zR), new Vector3(ew + eh * 0.5f, eh * 1.5f, d));
            Shapes.Box(m.Mesh, Roles.EngineGlow, new Vector3(x, y, zR - d / 2 + 0.03f), new Vector3(ew, eh, 0.12f));
            // Splitter vanes in the slot.
            for (var k = 1; k < 3; k++)
                Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(x - ew / 2 + ew * k / 3f, y, zR - d / 2 + 0.03f), new Vector3(MathF.Max(0.03f, ew * 0.03f), eh, 0.16f));
            c.AddMarker("engine_exhaust", m, new Vector3(x, y, zR - d / 2 - 0.05f), -Vector3.UnitZ, ew / 2, L * 0.5f);
        }
    }

    /// <summary>Faceted engine nacelles along the hull's flanks, slit exhausts at the back and dark intake mouths at the front.</summary>
    private static void Nacelles(BuildContext c, Module hull, float x, float cy, float w, float h, float zR, float len, float L, float bev)
    {
        foreach (var s in new[] { 1f, -1f })
        {
            var pr = Section(c, s * x, cy, w, h, bev, 0.5f, 0.32f);
            var id = s > 0 ? "nacelle_port" : "nacelle_starboard";
            var (m, loft) = c.AddLoft(id, "engine", ModuleTier.Secondary, hull.Id,
                [(zR, pr.Scaled(0.9f, 0.88f)), (zR + L * 0.05f, pr), (zR + len * 0.82f, pr), (zR + len, pr.ScaledOnBase(0.8f, 0.62f))], Roles.HullSecondary, "pod");
            var front = loft.Rings[^1];
            var ctr = front.Aggregate(Vector3.Zero, (a, q) => a + q) / front.Length;
            m.Mesh.AddPolygon(Roles.DarkMechanics, front.Select(q => ctr + (q - ctr) * 0.75f + new Vector3(0, 0, 0.02f)).ToArray(), Vector3.UnitZ);
            SlitEngines(c, m, pr.Scaled(0.9f, 0.88f), zR, L, n2(c), id + "_exhaust");
        }
        static string n2(BuildContext c) => c.Plan.EngineLayout == "3x1" ? "2x1" : "1x1";
    }

    /// <summary>Thin glowing accent lines along the hull's lower chines.</summary>
    private static void GlowLines(BuildContext c, Module hull)
    {
        // Glow lines are surface detail: none at "low" (silhouette-only) detail.
        if (c.DetailLevel == 0) return;
        var loft = hull.Lofts[0];
        var d = c.DetailsOf(hull);
        for (var s = 0; s < loft.Segments; s++)
        {
            if (loft.IsStep(s)) continue;
            var (along, _) = loft.FaceSize(s, Profile.FaceBottomPosX);
            if (along < c.Plan.Length * 0.2f) continue;
            foreach (var f in new[] { Profile.FaceBottomPosX, Profile.FaceBottomNegX })
            {
                var key = BuildContext.FaceKey(hull, s, f);
                if (!c.TryUseSurface(key, 0.06f, 0.94f, 0.3f, 0.7f)) continue;
                Shapes.Patch(d.Mesh, Roles.Glow, loft, s, f, 0.06f, 0.94f, 0.35f, 0.65f, 0.02f);
            }
        }
    }

    /// <summary>True when a plate with this outline would sit in an engine's exhaust.</summary>
    private static bool InExhaust(BuildContext c, Vector3[] outline, float thick)
    {
        var box = Aabb.FromPoints(outline.Aggregate((a, b) => Vector3.Min(a, b)) - new Vector3(thick), outline.Aggregate((a, b) => Vector3.Max(a, b)) + new Vector3(thick));
        return c.Ship.Markers.Where(k => k.Kind == "engine_exhaust").Select(Validation.ShipValidator.ExhaustVolume).Any(v => box.Penetration(v) > 0.05f);
    }

    /// <summary>Small fins under the tail, canted outwards and down: the craft looks ready to pounce even parked.</summary>
    private static void VentralFins(BuildContext c, Module hull, List<(float z, Profile p)> body, float x, float height, float z0, float chord)
    {
        var m = c.Add("ventral_fins", "fin", ModuleTier.Secondary, hull.Id);
        var thick = MathF.Max(0.08f, height * 0.09f);
        var cant = c.Shape.Range(25f, 45f) * MathF.PI / 180;
        foreach (var s in new[] { 1f, -1f })
        {
            var y = ProfileAt(body, z0 + chord / 2).Bottom + thick;
            var down = new Vector3(s * MathF.Sin(cant), -MathF.Cos(cant), 0);
            var r0 = new Vector3(s * x, y, z0);
            var r1 = r0 + new Vector3(0, 0, chord);
            var t0 = r0 + down * height + new Vector3(0, 0, chord * 0.35f);
            var t1 = t0 + new Vector3(0, 0, chord * 0.4f);
            BevelPlate(m.Mesh, Roles.HullMain, [r0, r1, t1, t0], thick, thick * 0.9f);
        }
    }

    /// <summary>Knife-edged canards beside the nose, swept and slightly drooped.</summary>
    private static void Canards(BuildContext c, Module hull, List<(float z, Profile p)> body, float z0, float chord, float span, float y)
    {
        var m = c.Add("canards", "wing", ModuleTier.Secondary, hull.Id);
        var bp = ProfileAt(body, z0 + chord * 0.5f);
        var root = bp.HalfWidthAt(y) * 0.7f;
        var t = MathF.Max(0.06f, bp.H * 0.08f);
        foreach (var s in new[] { 1f, -1f })
        {
            Vector3[] o = [new(s * root, y, z0), new(s * root, y, z0 + chord), new(s * (root + span), y - span * 0.1f, z0 + chord * 0.15f), new(s * (root + span), y - span * 0.1f, z0 - chord * 0.2f)];
            BevelPlate(m.Mesh, Roles.HullMain, o, t, t * 0.9f);
        }
    }
}

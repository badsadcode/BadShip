using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Citadels: flying fortresses, the capital ships. A long, tall armoured hull with a ram at the bow, buttress ribs
/// down the flanks with broadside gun decks and rows of lit windows between them, a stepped fortress keep rising from
/// the deck with watch towers and masts, big turrets along the spine, keel blades or a hanging under-fortress below,
/// and a wall of engine nozzles at the stern. Hangar mouths open in the flanks.
/// Variety: prow (ram, blade, bastion), keep position (aft, mid), keep tiers, nozzle grid, underside (keel, blades,
/// undercroft) and hull form (slab, faceted).
/// </summary>
public static class Citadels
{
    public static readonly string[] Prows = ["ram", "blade", "bastion"];
    public static readonly string[] Keeps = ["aft", "mid"];
    public static readonly string[] Layouts = ["2x2", "3x2", "4x2", "3x3"];
    public static readonly string[] Undersides = ["keel", "blades", "undercroft"];

    public static void Plan(BuildContext c)
    {
        var r = c.PlanRng;
        var p = c.Plan;
        p.Family = "citadel";
        var (lmin, lmax) = p.Size switch { "small" => (260f, 380f), "large" => (560f, 820f), _ => (380f, 560f) };
        p.Length = c.Recipe.Length ?? r.Range(lmin, lmax);
        var L = p.Length;
        p.Beam = L * r.Range(0.16f, 0.21f);
        p.Height = L * r.Range(0.13f, 0.17f);
        p.ProwStyle = r.Weighted<string>([("ram", 0.5), ("blade", 0.25), ("bastion", 0.25)]);
        p.HullForm = r.Chance(Math.Clamp(c.Lang.Faceted + 0.2, 0, 1)) ? "faceted" : "slab";
        p.BridgeStyle = r.Chance(0.65) ? "aft" : "mid";
        p.Tiers = Math.Clamp(r.Int(3, 5) + (p.Size == "large" ? 1 : p.Size == "small" ? -1 : 0), 2, 7);
        p.EngineLayout = r.Weighted<string>(p.Size switch
        {
            "small" => [("2x2", 0.6), ("3x2", 0.4)],
            "large" => [("3x2", 0.3), ("4x2", 0.35), ("3x3", 0.35)],
            _ => [("2x2", 0.2), ("3x2", 0.4), ("4x2", 0.2), ("3x3", 0.2)],
        });
        p.EngineStyle = "nozzle";
        p.Payload = "";
        p.Asymmetric = false;
        p.Ventral = r.Weighted<string>([("keel", 0.4), ("blades", 0.3), ("undercroft", 0.3)]);
    }

    public static void Build(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, B = p.Beam, H = p.Height, zR = -L / 2, zN = L / 2;
        var ch = Num.Clamp(c.Lang.Chamfer, 0.6f, 1.5f);
        var faceted = p.HullForm == "faceted";

        var engLen = L * r.Range(0.05f, 0.07f);
        var ramLen = p.ProwStyle switch { "bastion" => L * r.Range(0.07f, 0.1f), "blade" => L * r.Range(0.2f, 0.26f), _ => L * r.Range(0.15f, 0.21f) };
        var zBody0 = zR + engLen;
        var zProw = zN - ramLen;
        var body = new Profile(0, 0, B, B * (faceted ? 0.8f : 0.9f), H, H * (faceted ? 0.2f : 0.1f) * ch, H * 0.22f * ch) { TopRun = faceted ? 1.6f : 1f };

        // ---------------------------------------------------------------- hull and prow
        List<(float, Profile)> rings = [(zBody0 - engLen * 0.3f, body.Scaled(0.94f, 0.92f)), (zBody0, body), (zProw, body)];
        switch (p.ProwStyle)
        {
            case "blade":
                rings.Add((zProw + ramLen * 0.3f, body.Scaled(0.55f, 0.82f) with { Cy = -H * 0.06f }));
                rings.Add((zN, new Profile(0, -H * 0.3f, B * 0.04f, B * 0.02f, H * 0.22f, H * 0.05f, 0)));
                break;
            case "bastion":
            {
                // Armoured terraces stepping down to a blunt face.
                var t1 = body.ScaledOnBase(0.86f, 0.78f);
                var t2 = body.ScaledOnBase(0.7f, 0.55f);
                rings.Add((zProw, t1));
                rings.Add((zProw + ramLen * 0.45f, t1));
                rings.Add((zProw + ramLen * 0.45f, t2));
                rings.Add((zN - ramLen * 0.1f, t2));
                rings.Add((zN, t2.Scaled(0.85f, 0.8f)));
                break;
            }
            default:
                // A tall dagger ram: the hull narrows into a vertical blade whose top slopes down to the point.
                // The blade reaches below the keel line, like a cutwater.
                rings.Add((zProw + ramLen * 0.45f, body.Scaled(0.6f, 1.02f) with { Cy = -H * 0.1f }));
                rings.Add((zN, new Profile(0, -H * 0.3f, B * 0.07f, B * 0.03f, H * 0.85f, H * 0.14f, H * 0.12f)));
                break;
        }
        var (hull, loft) = c.AddLoft("hull_main", "hull", ModuleTier.Primary, null, rings, Roles.HullMain);
        c.AddSides(hull, loft, 1, Profile.FacePosX, Profile.FaceNegX);
        c.AddEnvelope("habitable", hull, new Aabb(new Vector3(-B * 0.35f, -H * 0.3f, zBody0 + 4), new Vector3(B * 0.35f, H * 0.4f, zProw)));
        if (p.ProwStyle == "ram") Ram(c, hull, rings, zProw, zN, B, H);

        // ---------------------------------------------------------------- the stern: a wall of nozzles
        Engines(c, hull, body, zR, zBody0, engLen);

        // ---------------------------------------------------------------- flanks: buttresses, gun decks, windows, hangars
        var keepLen = (zProw - zBody0) * r.Range(0.3f, 0.42f);
        var keepZ0 = p.BridgeStyle == "aft" ? zBody0 + L * 0.015f : zBody0 + (zProw - zBody0) * r.Range(0.4f, 0.5f) - keepLen / 2;
        Flanks(c, hull, body, zBody0, zProw, L, B, H);

        // ---------------------------------------------------------------- the keep and the spine turrets
        var top = body.Top;
        var deckHalf = body.TopFlatHalf;
        // The keep sits down into the hull's shoulders and takes most of its width, not just the flat strip on top.
        var shoulder = body.Top - body.ClampedChamfers().ct * 0.9f;
        Keep(c, hull, shoulder, body.HalfWidthAt(shoulder) * 0.9f, keepZ0, keepLen, B, H);
        var turretZs = Turrets(c, hull, top, deckHalf, zBody0, zProw, keepZ0, keepZ0 + keepLen, B, H, L);
        Bastions(c, hull, body, zBody0, zProw, keepZ0, keepZ0 + keepLen, turretZs, H, L);

        // ---------------------------------------------------------------- below
        Underside(c, hull, body, zBody0, zProw, B, H, L);
    }

    // ------------------------------------------------------------------ parts

    /// <summary>A raised armoured ridge along the ram's crest and heavy bow guns at its root.</summary>
    private static void Ram(BuildContext c, Module hull, List<(float z, Profile p)> rings, float zProw, float zN, float B, float H)
    {
        var a = Fighters.ProfileAt(rings, zProw + (zN - zProw) * 0.05f);
        var b = Fighters.ProfileAt(rings, zProw + (zN - zProw) * 0.8f);
        var ridgeW = B * 0.06f;
        var (m, _) = c.AddLoft("ram_crest", "armour", ModuleTier.Secondary, hull.Id,
        [
            (zProw + (zN - zProw) * 0.05f, new Profile(0, a.Top - H * 0.02f, ridgeW, ridgeW * 0.5f, H * 0.1f, H * 0.04f, 0)),
            (zProw + (zN - zProw) * 0.8f, new Profile(0, b.Top - H * 0.02f, ridgeW * 0.6f, ridgeW * 0.3f, H * 0.05f, H * 0.02f, 0)),
        ], Roles.HullSecondary, null);
        var gz = zProw + (zN - zProw) * 0.12f;
        var gp = Fighters.ProfileAt(rings, gz);
        var gy = gp.Cy - gp.H * 0.1f;
        foreach (var s in new[] { 1f, -1f })
        {
            var x = s * gp.HalfWidthAt(gy);
            var rad = B * 0.025f;
            Shapes.Prism(m.Mesh, Roles.DarkMechanics, new Vector3(x, gy, gz - B * 0.15f), new Vector3(x, gy, gz + B * 0.05f), rad * 2.4f, rad * 2.1f, 8, MathF.PI / 8);
            Shapes.Prism(m.Mesh, Roles.DarkMechanics, new Vector3(x, gy, gz), new Vector3(x, gy, gz + B * 0.55f), rad, rad * 0.9f, 8, MathF.PI / 8);
            c.AddMarker("hardpoint", m, new Vector3(x, gy, gz + B * 0.55f), Vector3.UnitZ, rad * 2);
        }
    }

    /// <summary>The engine block: a slightly smaller housing at the stern with a grid of flared nozzles.</summary>
    private static void Engines(BuildContext c, Module hull, Profile body, float zR, float zBody0, float engLen)
    {
        var p = c.Plan;
        var cols = p.EngineLayout[0] - '0';
        var rows = p.EngineLayout[2] - '0';
        var housing = body.Scaled(0.86f, 0.8f);
        var (m, _) = c.AddLoft("engine_block", "engine", ModuleTier.Secondary, hull.Id,
            [(zR, housing.Scaled(0.95f, 0.95f)), (zR + engLen * 0.3f, housing), (zBody0 + engLen * 0.2f, housing)], Roles.HullSecondary, "pod");
        var w = housing.WBottom * 0.82f;
        var h = housing.H * 0.78f;
        var rad = MathF.Min(w / cols, h / rows) * 0.42f;
        var nozLen = rad * 1.2f;
        for (var i = 0; i < cols; i++)
            for (var j = 0; j < rows; j++)
            {
                var x = (i - (cols - 1) / 2f) * w / cols;
                var y = housing.Cy + (j - (rows - 1) / 2f) * h / rows;
                var a = new Vector3(x, y, zR + 0.2f);
                var b = new Vector3(x, y, zR - nozLen);
                Shapes.Prism(m.Mesh, Roles.DarkMechanics, a, b, rad * 0.8f, rad, 12, 0, capA: false, capB: true, capBMat: Roles.EngineGlow);
                // An armoured collar round each nozzle.
                Shapes.Prism(m.Mesh, Roles.HullSecondary, a + new Vector3(0, 0, 0.5f), a - new Vector3(0, 0, nozLen * 0.35f), rad * 1.08f, rad * 1.08f, 8, MathF.PI / 8);
                c.AddMarker("engine_exhaust", m, b - new Vector3(0, 0, 0.2f), -Vector3.UnitZ, rad, p.Length * 0.25f);
            }
    }

    /// <summary>
    /// Buttress ribs down both flanks; between them, broadside gun decks (dark port strips with barrels) and rows of
    /// lit windows; one or two bays per side open as hangar mouths instead.
    /// </summary>
    private static void Flanks(BuildContext c, Module hull, Profile body, float z0, float z1, float L, float B, float H)
    {
        var r = c.Shape;
        var ribs = c.Add("buttresses", "buttress", ModuleTier.Secondary, hull.Id);
        var guns = c.Add("broadsides", "broadside", ModuleTier.Secondary, hull.Id);
        var span = z1 - z0;
        var bays = Math.Max(6, (int)(span / (L * r.Range(0.045f, 0.06f))));
        var bay = span / bays;
        var ribW = bay * 0.16f;
        var ribD = B * 0.045f;
        var decks = Num.Clamp((int)(H / 16f), 2, 4);
        var yLow = body.Bottom + body.ClampedChamfers().cb + H * 0.06f;
        var yHigh = body.Top - body.ClampedChamfers().ct - H * 0.08f;
        var hangarBays = new HashSet<int> { r.Int(1, Math.Max(1, bays / 3)) };
        if (bays > 9 && r.Chance(0.6)) hangarBays.Add(r.Int(bays / 3 + 1, bays / 2));
        var barrel = Num.Clamp(L * 0.0035f, 0.6f, 2.4f);
        foreach (var s in new[] { 1f, -1f })
        {
            for (var i = 0; i <= bays; i++)
            {
                // Rib: a deep plate standing off the flank, stepped in at the top like a buttress.
                var z = z0 + i * bay;
                var xs = s * body.HalfWidthAt(0);
                Shapes.Box(ribs.Mesh, Roles.HullSecondary, new Vector3(xs + s * ribD * 0.4f, (yLow + yHigh) / 2 - H * 0.04f, z), new Vector3(ribD * 1.6f, (yHigh - yLow) * 0.95f, ribW));
                Shapes.Box(ribs.Mesh, Roles.HullSecondary, new Vector3(xs + s * ribD * 0.2f, yHigh + H * 0.03f, z), new Vector3(ribD, H * 0.1f, ribW * 0.8f));
            }
            for (var i = 0; i < bays; i++)
            {
                var zc = z0 + (i + 0.5f) * bay;
                var inner = bay - ribW * 1.2f;
                if (s > 0 && hangarBays.Contains(i) || s < 0 && hangarBays.Contains(bays - 1 - i))
                {
                    // Hangar mouth: a dark recess with a lit lintel and a hazard sill.
                    var hy0 = yLow + H * 0.02f;
                    var hy1 = (yLow + yHigh) / 2 + H * 0.08f;
                    var x = s * body.HalfWidthAt((hy0 + hy1) / 2);
                    Shapes.Box(guns.Mesh, Roles.DarkMechanics, new Vector3(x, (hy0 + hy1) / 2, zc), new Vector3(0.6f, hy1 - hy0, inner * 0.92f));
                    Shapes.Box(guns.Mesh, Roles.Light, new Vector3(x + s * 0.3f, hy1 - 0.6f, zc), new Vector3(0.3f, 0.6f, inner * 0.85f));
                    Shapes.Box(guns.Mesh, Roles.Hazard, new Vector3(x + s * 0.3f, hy0 + 0.4f, zc), new Vector3(0.4f, 0.8f, inner * 0.92f));
                    c.AddMarker("hangar", guns, new Vector3(x + s * 0.5f, (hy0 + hy1) / 2, zc), new Vector3(s, 0, 0), inner * 0.45f);
                    continue;
                }
                for (var d = 0; d < decks; d++)
                {
                    var y = float.Lerp(yLow, yHigh, (d + 0.5f) / decks) - H * 0.03f;
                    var x = s * body.HalfWidthAt(y);
                    // Port strip and barrels.
                    Shapes.Box(guns.Mesh, Roles.DarkMechanics, new Vector3(x, y, zc), new Vector3(0.5f, barrel * 3.2f, inner * 0.9f));
                    var n = inner > barrel * 9 ? 3 : 2;
                    for (var k = 0; k < n; k++)
                    {
                        var bz = zc + (k - (n - 1) / 2f) * inner / n;
                        Shapes.Prism(guns.Mesh, Roles.DarkMechanics, new Vector3(x, y, bz), new Vector3(x + s * barrel * 6, y, bz), barrel * 0.55f, barrel * 0.45f, 6, 0);
                    }
                    if (d == 0) c.AddMarker("broadside", guns, new Vector3(x + s * barrel * 6, y, zc), new Vector3(s, 0, 0), barrel);
                    // Lit windows above every deck: short warm strips with gaps.
                    var wy = y + (yHigh - yLow) / decks * 0.38f;
                    var wx = s * body.HalfWidthAt(wy);
                    var panes = Math.Max(2, (int)(inner / (barrel * 3)));
                    for (var k = 0; k < panes; k++)
                    {
                        if (r.Chance(0.25)) continue;
                        var pz = zc - inner * 0.45f + (k + 0.5f) * inner * 0.9f / panes;
                        Shapes.Box(guns.Mesh, Roles.Glass, new Vector3(wx, wy, pz), new Vector3(0.3f, barrel * 1.2f, inner * 0.9f / panes * 0.55f));
                    }
                }
            }
        }
    }

    /// <summary>
    /// The keep: stepped armoured tiers, each narrower and shorter, set back towards the stern so it rises like a
    /// crag; watch towers with stepped caps on the tier corners, lit window bands, masts and the command bridge on top.
    /// </summary>
    private static void Keep(BuildContext c, Module hull, float deckY, float deckHalf, float z0, float len, float B, float H)
    {
        var p = c.Plan;
        var r = c.Shape;
        var parent = hull;
        var y = deckY - 0.5f;
        var w = deckHalf * 2 * r.Range(0.85f, 1f);
        var zs = z0;
        var tierH = H * r.Range(0.28f, 0.4f);
        Profile last = default;
        float lastZ0 = 0, lastZ1 = 0;
        var ch = Num.Clamp(c.Lang.Chamfer, 0.6f, 1.5f);
        for (var t = 0; t < p.Tiers; t++)
        {
            var h = tierH * (t == p.Tiers - 1 ? 0.8f : 1f) + 0.5f;
            var pr = new Profile(0, y + h / 2, w, w * 0.86f, h, h * 0.18f * ch, 0) { TopRun = 1.3f };
            var ze = zs + len;
            var slope = len * 0.08f;
            var (m, loft) = c.AddLoft(t == 0 ? "keep" : $"keep_tier_{t}", "keep", t == 0 ? ModuleTier.Primary : ModuleTier.Secondary, parent.Id,
                [(zs, pr.ScaledOnBase(0.92f, 0.85f)), (zs + slope, pr), (ze - slope * 1.5f, pr), (ze, pr.ScaledOnBase(0.8f, 0.7f))], Roles.HullMain, "tier");
            // Window bands on the sides and front.
            foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX })
            {
                var (along, across) = loft.FaceSize(1, f);
                if (along < 6 || across < 1) continue;
                var n = Math.Max(3, (int)(along / (H * 0.06f)));
                for (var k = 0; k < n; k++)
                {
                    if (r.Chance(0.2)) continue;
                    var u0 = (k + 0.2f) / n;
                    var u1 = (k + 0.75f) / n;
                    Shapes.Patch(m.Mesh, Roles.Glass, loft, 1, f, u0, u1, Parts.UpV(f, 0.35f), Parts.UpV(f, 0.6f), 0.25f);
                }
                c.UseSurface(BuildContext.FaceKey(m, 1, f), 0, 1, 0, 1);
            }
            last = pr;
            lastZ0 = zs + slope;
            lastZ1 = ze - slope * 1.5f;
            // Towers on the corners of all but the top tier.
            if (t < p.Tiers - 1)
            {
                var towers = t <= 1 ? 4 : r.Chance(0.7) ? 2 : 0;
                var tw = MathF.Max(3f, MathF.Min(w * 0.2f, H * r.Range(0.1f, 0.15f)));
                for (var k = 0; k < towers; k++)
                {
                    var tx = (k % 2 == 0 ? 1 : -1) * (w * 0.43f - tw / 2);
                    var tz = k < 2 ? zs + len * 0.12f : ze - len * 0.15f;
                    if (towers == 2) tz = r.Chance(0.5) ? zs + len * 0.12f : ze - len * 0.15f;
                    Tower(c, m, new Vector3(tx, pr.Top - 0.3f, tz), tw, H * r.Range(0.4f, 0.75f) * (1 - t * 0.12f));
                }
            }
            parent = m;
            // Next tier: narrower, shorter, set back.
            y = pr.Top - 0.4f;
            var nlen = len * r.Range(0.66f, 0.8f);
            zs = zs + (len - nlen) * r.Range(0.15f, 0.45f);
            len = nlen;
            w *= r.Range(0.72f, 0.85f);
            if (w < B * 0.15f || len < H * 0.4f) break;
        }
        // The command bridge: a forward-leaning glazed block on top, with masts.
        var bw = MathF.Max(w * 0.8f, B * 0.14f);
        var bh = tierH * 0.6f;
        var bridge = new Profile(0, last.Top - 0.3f + bh / 2, bw, bw * 0.8f, bh, bh * 0.2f, 0);
        var bz1 = lastZ1;
        var bz0 = MathF.Max(lastZ0, bz1 - len * 0.5f);
        var (br, bl) = c.AddLoft("command_bridge", "bridge", ModuleTier.Secondary, parent.Id,
            [(bz0, bridge.ScaledOnBase(0.9f, 0.9f)), (bz1 - bh * 0.6f, bridge), (bz1, bridge.ScaledOnBase(1.05f, 0.55f))], Roles.HullSecondary, "tier");
        foreach (var f in new[] { Profile.FaceTopPosX, Profile.FaceTop, Profile.FaceTopNegX, Profile.FacePosX, Profile.FaceNegX })
            Shapes.Patch(br.Mesh, Roles.Glass, bl, 1, f, 0.15f, 0.85f, 0.2f, 0.8f, 0.2f);
        c.AddMarker("bridge", br, new Vector3(0, bridge.Cy, bz1), Vector3.UnitZ);
        for (var k = 0; k < 2; k++)
        {
            var mx = (k == 0 ? 1 : -1) * bw * 0.3f;
            var mz = (bz0 + bz1) / 2;
            var mh = H * r.Range(0.3f, 0.6f);
            Shapes.Beam(br.Mesh, Roles.DarkMechanics, new Vector3(mx, bridge.Top - 0.2f, mz), new Vector3(mx, bridge.Top + mh, mz), MathF.Max(0.8f, H * 0.012f));
            Shapes.Beam(br.Mesh, Roles.DarkMechanics, new Vector3(mx - bw * 0.1f, bridge.Top + mh * 0.7f, mz), new Vector3(mx + bw * 0.1f, bridge.Top + mh * 0.7f, mz), MathF.Max(0.5f, H * 0.008f));
            Shapes.Box(br.Mesh, Roles.NavRed, new Vector3(mx, bridge.Top + mh, mz), new Vector3(1.2f, 1.2f, 1.2f));
            c.AddMarker("sensor", br, new Vector3(mx, bridge.Top + mh, mz), Vector3.UnitY, 1);
        }
    }

    /// <summary>A watch tower: a chamfered square shaft, a wider gallery with lit slits, a stepped cap and a mast.</summary>
    private static void Tower(BuildContext c, Module parent, Vector3 foot, float w, float h)
    {
        var m = c.Add("tower", "tower", ModuleTier.Secondary, parent.Id);
        var rot = MathF.PI / 4;
        var R = w * 0.7071f;
        Shapes.Prism(m.Mesh, Roles.HullMain, foot, foot + new Vector3(0, h, 0), R, R * 0.85f, 4, rot);
        var g = foot + new Vector3(0, h, 0);
        Shapes.Prism(m.Mesh, Roles.HullSecondary, g, g + new Vector3(0, w * 0.6f, 0), R * 1.15f, R * 1.15f, 8, MathF.PI / 8);
        for (var k = 0; k < 4; k++)
        {
            var a = k * MathF.PI / 2;
            var d = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
            Shapes.Box(m.Mesh, Roles.Glass, g + new Vector3(0, w * 0.3f, 0) + d * R * 1.08f, new Vector3(MathF.Abs(d.Z) * w * 0.5f + 0.3f, w * 0.15f, MathF.Abs(d.X) * w * 0.5f + 0.3f));
        }
        var cap = g + new Vector3(0, w * 0.6f, 0);
        Shapes.Prism(m.Mesh, Roles.HullMain, cap, cap + new Vector3(0, w * 0.35f, 0), R * 1.05f, R * 0.6f, 4, rot);
        Shapes.Prism(m.Mesh, Roles.HullMain, cap + new Vector3(0, w * 0.35f, 0), cap + new Vector3(0, w * 0.6f, 0), R * 0.45f, R * 0.3f, 4, rot);
        Shapes.Beam(m.Mesh, Roles.DarkMechanics, cap + new Vector3(0, w * 0.5f, 0), cap + new Vector3(0, w * 0.6f + h * 0.4f, 0), MathF.Max(0.4f, w * 0.06f));
        Shapes.Box(m.Mesh, Roles.Light, cap + new Vector3(0, w * 0.6f + h * 0.4f, 0), new Vector3(0.9f, 0.9f, 0.9f));
    }

    /// <summary>Big multi-barrel turrets along the spine, fore and aft of the keep, some raised on barbettes to fire over the others.</summary>
    private static List<(float z, float r)> Turrets(BuildContext c, Module hull, float deckY, float deckHalf, float z0, float z1, float keep0, float keep1, float B, float H, float L)
    {
        var placed = new List<(float z, float r)>();
        var r = c.Shape;
        var R = Num.Clamp(B * r.Range(0.1f, 0.14f), 4f, 14f);
        var gap = R * 3.2f;
        var spots = new List<(float z, bool forward)>();
        // Forward of the keep: turrets facing the bow; behind a mid keep: turrets facing the stern.
        for (var z = keep1 + gap * 0.8f; z < z1 - R * 1.5f; z += gap) spots.Add((z, true));
        for (var z = keep0 - gap * 0.8f; z > z0 + R * 2f; z -= gap) spots.Add((z, false));
        var count = 0;
        foreach (var (z, forward) in spots.OrderBy(s => MathF.Abs(s.z - (s.forward ? keep1 : keep0))))
        {
            if (count >= 6) break;
            var raised = count % 2 == 1 && r.Chance(0.7);
            var m = c.Add("turret", "turret", ModuleTier.Secondary, hull.Id);
            var baseY = deckY - 0.4f;
            var barb = raised ? R * 0.9f : R * 0.35f;
            Shapes.Prism(m.Mesh, Roles.HullSecondary, new Vector3(0, baseY, z), new Vector3(0, baseY + barb, z), R * 1.05f, R, 8, MathF.PI / 8);
            var hy = baseY + barb;
            var dir = forward ? 1f : -1f;
            // A faceted gun house: wider at the back, sloped front.
            var houseP = new Profile(0, hy + R * 0.35f, R * 1.7f, R * 1.3f, R * 0.7f, R * 0.25f, 0);
            var za = z - dir * R * 0.9f;
            var zb = z + dir * R * 0.9f;
            var houseRings = forward
                ? new List<(float, Profile)> { (za, houseP), (zb - R * 0.35f, houseP), (zb, houseP.ScaledOnBase(0.85f, 0.55f)) }
                : [(zb, houseP.ScaledOnBase(0.85f, 0.55f)), (zb + R * 0.35f, houseP), (za, houseP)];
            var house = new Loft(houseRings);
            house.Build(m.Mesh, Roles.HullMain);
            var n = r.Chance(0.5) ? 3 : 2;
            for (var k = 0; k < n; k++)
            {
                var x = (k - (n - 1) / 2f) * R * 0.45f;
                var a = new Vector3(x, hy + R * 0.35f, zb - dir * R * 0.2f);
                var b = a + new Vector3(0, 0, dir * R * 2.6f);
                Shapes.Prism(m.Mesh, Roles.DarkMechanics, a, b, R * 0.11f, R * 0.09f, 8, MathF.PI / 8);
            }
            c.AddMarker("hardpoint", m, new Vector3(0, hy + R * 0.35f, zb + dir * R * 2.6f), new Vector3(0, 0, dir), R);
            placed.Add((z, R * 3.6f));
            count++;
        }
        return placed;
    }

    /// <summary>Small watch towers along both deck edges, like towers on a curtain wall, clear of the keep and the turrets.</summary>
    private static void Bastions(BuildContext c, Module hull, Profile body, float z0, float z1, float keep0, float keep1,
        List<(float z, float r)> turrets, float H, float L)
    {
        var r = c.Shape;
        var tw = H * r.Range(0.08f, 0.12f);
        var step = L * r.Range(0.07f, 0.11f);
        var h = H * r.Range(0.22f, 0.4f);
        // They stand on the shoulder facets, half sunk into them.
        var y = body.Top - body.ClampedChamfers().ct * 0.6f;
        var x = body.HalfWidthAt(y) - tw * 0.7f;
        for (var z = z0 + step * 0.6f; z < z1 - tw * 2; z += step)
        {
            if (z > keep0 - tw * 2 && z < keep1 + tw * 2) continue;
            // Clear of the turrets' gun houses (radius about r / 3.6 · 1.7).
            if (turrets.Any(t => MathF.Abs(t.z - z) < t.r * 0.5f + tw && x - tw < t.r * 0.5f)) continue;
            foreach (var s in new[] { 1f, -1f })
                Tower(c, hull, new Vector3(s * x, y - tw * 0.3f, z), tw, h);
        }
    }

    /// <summary>What hangs below: a keel, a row of blades and spikes, or an undercroft (an inverted stepped fortress).</summary>
    private static void Underside(BuildContext c, Module hull, Profile body, float z0, float z1, float B, float H, float L)
    {
        var p = c.Plan;
        var r = c.VentralRng;
        var bottom = body.Bottom;
        var span = z1 - z0;
        switch (p.Ventral)
        {
            case "keel":
            {
                var kw = B * r.Range(0.08f, 0.14f);
                var kh = H * r.Range(0.35f, 0.55f);
                var pr = new Profile(0, bottom + H * 0.05f - kh / 2, kw, kw, kh, 0, kw * 0.45f);
                var ka = z0 + span * r.Range(0.1f, 0.2f);
                var kb = z1 - span * r.Range(0.02f, 0.1f);
                c.AddLoft("keel", "keel", ModuleTier.Primary, hull.Id,
                    [(ka, pr.ScaledOnTop(0.7f, 0.25f)), (ka + span * 0.12f, pr), (kb - span * 0.2f, pr), (kb, pr.ScaledOnTop(0.6f, 0.2f))], Roles.HullMain, "hull");
                break;
            }
            case "blades":
            {
                var m = c.Add("ventral_blades", "keel", ModuleTier.Secondary, hull.Id);
                var n = r.Int(3, 6);
                var t = MathF.Max(1.2f, B * 0.025f);
                for (var k = 0; k < n; k++)
                {
                    var zc = z0 + span * (0.15f + 0.75f * k / Math.Max(1, n - 1));
                    var bl = span * r.Range(0.07f, 0.12f);
                    var depth = H * r.Range(0.35f, 0.7f) * (1 - 0.3f * MathF.Abs(k - (n - 1) / 2f) / n);
                    Vector3[] o = [new(0, bottom + H * 0.05f, zc - bl / 2), new(0, bottom + H * 0.05f, zc + bl / 2), new(0, bottom - depth, zc - bl * 0.3f)];
                    Fighters.BevelPlate(m.Mesh, Roles.HullMain, o, t, t * 0.8f);
                    // Spikes beside each blade.
                    foreach (var s in new[] { 1f, -1f })
                    {
                        var foot = new Vector3(s * B * 0.28f, bottom + H * 0.03f, zc);
                        Shapes.Prism(m.Mesh, Roles.HullSecondary, foot, foot - new Vector3(0, depth * 0.35f, 0), B * 0.03f, B * 0.004f, 4, MathF.PI / 4);
                    }
                }
                break;
            }
            default:
            {
                // An undercroft: inverted terraces with lit windows, hanging from the belly.
                var parent = hull;
                var y = bottom + H * 0.04f;
                var w = body.BottomFlatHalf * 2 * 0.9f;
                var za = z0 + span * r.Range(0.2f, 0.3f);
                var len = span * r.Range(0.35f, 0.5f);
                for (var t = 0; t < 3 && w > B * 0.15f; t++)
                {
                    var h = H * r.Range(0.14f, 0.2f);
                    var pr = new Profile(0, y - h / 2, w * 0.86f, w, h, 0, h * 0.25f);
                    var (m, loft) = c.AddLoft(t == 0 ? "undercroft" : $"undercroft_{t}", "undercroft", t == 0 ? ModuleTier.Primary : ModuleTier.Secondary, parent.Id,
                        [(za, pr.ScaledOnTop(0.9f, 0.6f)), (za + len * 0.1f, pr), (za + len * 0.88f, pr), (za + len, pr.ScaledOnTop(0.85f, 0.5f))], Roles.HullMain, "hull");
                    foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX })
                    {
                        var n = Math.Max(3, (int)(len / (H * 0.07f)));
                        for (var k = 0; k < n; k++)
                            if (r.Chance(0.7)) Shapes.Patch(m.Mesh, Roles.Glass, loft, 1, f, (k + 0.25f) / n, (k + 0.7f) / n, 0.4f, 0.6f, 0.2f);
                        c.UseSurface(BuildContext.FaceKey(m, 1, f), 0, 1, 0, 1);
                    }
                    parent = m;
                    y -= h - 0.4f;
                    za += len * 0.15f;
                    len *= 0.7f;
                    w *= 0.68f;
                }
                break;
            }
        }
    }
}

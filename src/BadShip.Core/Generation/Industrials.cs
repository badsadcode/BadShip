using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Industrial ships: a chunky work hull (the same hull body, engines and bridge tiers as the other ships, with a blunt
/// work face at the bow) plus the tool that tells you what the ship does:
/// drill — a mining ship with a stepped, toothed drill head and ore hoppers on deck fed by a conveyor;
/// refinery — a processing plant on deck: tank farms, spheres, chimney stacks, pipe racks, and a gas scoop at the bow;
/// crane — a construction ship with a lattice crane boom over the bow, a gantry over the deck, girder stacks and a
/// work cradle at the bow;
/// salvage — hydraulic claw arms reaching from the bow, scrap bins on deck and floodlights.
/// The role is kept in the plan's payload; the rest of the ship (equipment, kit, ventral structure, livery, damage)
/// comes from the shared passes.
/// </summary>
public static class Industrials
{
    public static readonly string[] Jobs = ["drill", "refinery", "crane", "salvage"];

    public static void Plan(BuildContext c)
    {
        var r = c.PlanRng;
        var p = c.Plan;
        var lang = c.Lang;
        p.Family = "industrial";
        p.Payload = r.Weighted<string>([("drill", 0.3), ("refinery", 0.25), ("crane", 0.25), ("salvage", 0.2)]);
        if (c.Recipe.Overrides?.Payload is { } pinned && Jobs.Contains(pinned)) p.Payload = pinned;
        var (lmin, lmax) = p.Size switch { "small" => (60f, 110f), "large" => (200f, 360f), _ => (110f, 200f) };
        p.Length = c.Recipe.Length ?? r.Range(lmin, lmax);
        var L = p.Length;
        p.Beam = L * r.Range(0.22f, 0.3f);
        p.Height = L * r.Range(0.13f, 0.18f) * (p.Size == "small" ? 1.15f : 1f);
        p.HullForm = r.Chance(lang.Faceted * 0.6) ? "faceted" : "slab";
        p.ProwStyle = "blunt";
        p.BridgeStyle = r.Weighted<string>([("slope", 0.4), ("overhang", 0.35), ("blunt", 0.25)]);
        p.Tiers = p.Size switch { "small" => 1, "large" => r.Int(2, 3), _ => r.Int(1, 2) };
        p.EngineLayout = p.Size == "small"
            ? r.Weighted<string>([("2x1", 0.6), ("1x2", 0.4)])
            : r.Weighted<string>([("2x1", 0.35), ("2x2", 0.3), ("3x1", 0.2), ("1x2", 0.15)]);
        p.EngineStyle = r.Chance(lang.PodEngines) ? "pod" : "nacelle";
        p.Asymmetric = false;
        p.Ventral = r.Weighted<string>([("keel", 0.35), ("gondola", 0.2), ("pods", 0.2), ("none", 0.25)]);
        // Most industrial ships wear the industrial scheme (hazard yellow) unless a palette is asked for.
        c.PreferredPalette = r.Chance(0.7) ? "industrial" : null;
    }

    public static void Build(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, B = p.Beam, H = p.Height, zN = L / 2;
        var engLen = L * r.Range(0.06f, 0.08f);
        var toolLen = p.Payload switch { "drill" => L * r.Range(0.18f, 0.24f), "salvage" => L * r.Range(0.2f, 0.27f), "refinery" => L * r.Range(0.06f, 0.09f), _ => L * r.Range(0.03f, 0.05f) };
        var zRear = -L / 2 + engLen;
        var zFront = zN - toolLen;
        var body = Parts.HullBody(c, 0, 0, B, H);
        var zBody0 = zRear + L * 0.025f;
        List<(float, Profile)> rings = [(zRear, body.Scaled(0.93f, 0.9f)), (zBody0, body), (zFront - L * 0.035f, body), (zFront, body.Scaled(0.9f, 0.86f))];
        var (hull, loft) = c.AddLoft("hull_main", "hull", ModuleTier.Primary, null, rings, Roles.HullMain);
        var deck = c.AddDeck(hull, body.Top, -body.TopFlatHalf, body.TopFlatHalf, zBody0 + 1, zFront - L * 0.04f, 0);
        c.AddSides(hull, loft, 1, Profile.FacePosX, Profile.FaceNegX);
        c.AddBelly(hull, body, zBody0 + 1, zFront - L * 0.04f, main: true);
        c.AddEnvelope("habitable", hull, new Aabb(new Vector3(-B * 0.3f, body.Cy, zBody0 + 2), new Vector3(B * 0.3f, body.Top - 1, zFront - 2)));
        Parts.Engines(c, hull.Id, "", zRear, 0, body.Cy + H * r.Range(-0.05f, 0.1f), B * r.Range(0.6f, 0.85f), H * r.Range(0.85f, 1.1f));

        // The work face: hazard chevrons and floodlights round the bow.
        WorkFace(c, hull, body, zFront, B, H);

        // The bridge: forward over the tool for drills and claws (the crew watches the work), aft for the plant.
        var anchor = p.Payload is "drill" or "salvage" ? "fore" : "aft";
        Parts.Superstructure(c, deck, p.Tiers, 0.4f, 0.65f, 0.9f, lengthMin: 0.14f, lengthMax: 0.24f, anchorAt: anchor);

        switch (p.Payload)
        {
            case "drill": Drill(c, hull, deck, body, zFront, zN, B, H, L); break;
            case "refinery": Refinery(c, hull, deck, body, zFront, zN, B, H, L); break;
            case "crane": Construction(c, hull, deck, body, zFront, zN, B, H, L); break;
            default: Salvage(c, hull, deck, body, zFront, zN, B, H, L); break;
        }
    }

    private static void WorkFace(BuildContext c, Module hull, Profile body, float zFront, float B, float H)
    {
        var m = c.Add("work_face", "work_face", ModuleTier.Secondary, hull.Id);
        var f = body.Scaled(0.9f, 0.86f);
        var n = Math.Max(4, (int)(f.WBottom / (H * 0.12f)));
        // A band of alternating hazard and dark blocks round the bow face's lower edge.
        for (var i = 0; i < n; i++)
        {
            var x = -f.WBottom * 0.42f + (i + 0.5f) * f.WBottom * 0.84f / n;
            Shapes.Box(m.Mesh, i % 2 == 0 ? Roles.Hazard : Roles.DarkMechanics, new Vector3(x, f.Bottom + f.H * 0.12f, zFront + 0.15f), new Vector3(f.WBottom * 0.84f / n, f.H * 0.08f, 0.5f));
        }
        foreach (var s in new[] { 1f, -1f })
        {
            var at = new Vector3(s * f.WTop * 0.38f, f.Top - f.H * 0.12f, zFront + 0.3f);
            Shapes.Box(m.Mesh, Roles.DarkMechanics, at, new Vector3(H * 0.08f, H * 0.06f, 0.8f));
            Shapes.Box(m.Mesh, Roles.Light, at + new Vector3(0, 0, 0.45f), new Vector3(H * 0.06f, H * 0.04f, 0.15f));
            c.AddMarker("floodlight", m, at + new Vector3(0, 0, 0.5f), Vector3.UnitZ, H * 0.04f);
        }
    }

    // ------------------------------------------------------------------ drill (mining)

    private static void Drill(BuildContext c, Module hull, Deck deck, Profile body, float zFront, float zN, float B, float H, float L)
    {
        var r = c.Shape;
        var m = c.Add("drill", "drill", ModuleTier.Secondary, hull.Id);
        var y = body.Cy;
        var R0 = MathF.Min(B, H) * r.Range(0.36f, 0.44f);
        var collar0 = zFront - L * 0.02f;
        var head0 = zFront + (zN - zFront) * 0.2f;
        // Collar with a hazard ring.
        Shapes.Prism(m.Mesh, Roles.HullSecondary, new Vector3(0, y, collar0), new Vector3(0, y, head0), R0 * 1.08f, R0 * 1.02f, 12, 0);
        Shapes.Prism(m.Mesh, Roles.Hazard, new Vector3(0, y, head0 - (head0 - zFront) * 0.35f), new Vector3(0, y, head0 - (head0 - zFront) * 0.15f), R0 * 1.12f, R0 * 1.12f, 12, 0);
        // Stepped head: frustums getting smaller towards the tip, each with a ring of teeth, twisted into a spiral.
        var stages = r.Int(3, 5);
        var len = zN - head0;
        var twist = r.Range(0.25f, 0.6f);
        for (var k = 0; k < stages; k++)
        {
            var a = head0 + len * k / stages;
            var b = head0 + len * (k + 1) / stages;
            var ra = R0 * (1 - 0.8f * k / stages);
            var rb = R0 * (1 - 0.8f * (k + 1) / stages);
            Shapes.Prism(m.Mesh, k % 2 == 0 ? Roles.DarkMechanics : Roles.HullSecondary, new Vector3(0, y, a), new Vector3(0, y, b), ra, rb * 1.05f, 12, k * twist);
            var teeth = Math.Max(6, 12 - k * 2);
            for (var i = 0; i < teeth; i++)
            {
                var ang = k * twist + i * MathF.Tau / teeth;
                var radial = new Vector3(MathF.Cos(ang), MathF.Sin(ang), 0);
                var foot = new Vector3(0, y, b) + radial * rb * 0.95f - new Vector3(0, 0, (b - a) * 0.3f);
                var dir = Vector3.Normalize(radial * 0.55f + Vector3.UnitZ * 0.85f);
                Shapes.Prism(m.Mesh, Roles.HullSecondary, foot, foot + dir * R0 * 0.32f, R0 * 0.07f, R0 * 0.01f, 4, ang);
            }
        }
        Shapes.Prism(m.Mesh, Roles.HullSecondary, new Vector3(0, y, zN - 0.2f), new Vector3(0, y, zN + R0 * 0.35f), R0 * 0.2f, R0 * 0.02f, 8, 0);
        c.AddMarker("tool", m, new Vector3(0, y, zN + R0 * 0.35f), Vector3.UnitZ, R0);

        // Ore hoppers on the deck, fed by a conveyor from the drill collar.
        var hop = c.Add("ore_hoppers", "hopper", ModuleTier.Secondary, hull.Id);
        var hw = MathF.Min(deck.Width * 0.7f, B * 0.45f);
        var hl = Num.Clamp(L * 0.08f, 5f, 24f);
        var hh = H * r.Range(0.18f, 0.28f);
        var placed = new List<float>();
        for (var i = 0; i < r.Int(2, 4); i++)
            if (deck.TryPlace(r, hw, hl, 1f, out var hx, out var hz, centerX: 0))
            {
                OpenBin(hop.Mesh, new Vector3(hx, deck.Y, hz), hw, hh, hl, Roles.HullSecondary);
                // Ore heaped inside.
                Shapes.Box(hop.Mesh, Roles.CargoB, new Vector3(hx, deck.Y + hh * 0.75f, hz), new Vector3(hw * 0.85f, hh * 0.3f, hl * 0.85f));
                placed.Add(hz);
            }
        if (placed.Count > 0)
        {
            var conv = c.Add("conveyor", "conveyor", ModuleTier.Secondary, hull.Id);
            var top = deck.Y + hh + H * 0.06f;
            // The belt rises from the drill collar to the hoppers, then runs along above them on trestles.
            var from = new Vector3(0, body.Top - H * 0.05f, zFront + 0.5f);
            var to = new Vector3(0, top, placed.Max());
            var dirv = Vector3.Normalize(to - from);
            var up = Vector3.Normalize(Vector3.Cross(dirv, Vector3.UnitX));
            Shapes.OBox(conv.Mesh, Roles.DarkMechanics, (from + to) / 2, Vector3.UnitX, up, dirv, new Vector3(hw * 0.12f, H * 0.02f, Vector3.Distance(from, to) / 2));
            Shapes.Box(conv.Mesh, Roles.DarkMechanics, new Vector3(0, top, (placed.Min() + placed.Max()) / 2), new Vector3(hw * 0.25f, H * 0.04f, placed.Max() - placed.Min() + hl * 0.5f));
            var legs = Math.Max(2, (int)((placed.Max() - placed.Min()) / 8f));
            for (var i = 0; i <= legs; i++)
            {
                var z = float.Lerp(placed.Min(), placed.Max(), i / (float)legs);
                Shapes.Beam(conv.Mesh, Roles.Hazard, new Vector3(hw * 0.14f, deck.Y, z), new Vector3(hw * 0.14f, top, z), 0.3f);
                Shapes.Beam(conv.Mesh, Roles.Hazard, new Vector3(-hw * 0.14f, deck.Y, z), new Vector3(-hw * 0.14f, top, z), 0.3f);
            }
        }
    }

    /// <summary>An open-topped bin: a floor and four walls.</summary>
    private static void OpenBin(Mesh m, Vector3 floor, float w, float h, float l, string mat)
    {
        var t = MathF.Max(0.3f, w * 0.04f);
        Shapes.Box(m, mat, floor + new Vector3(0, t / 2, 0), new Vector3(w, t, l));
        Shapes.Box(m, mat, floor + new Vector3(w / 2 - t / 2, h / 2, 0), new Vector3(t, h, l));
        Shapes.Box(m, mat, floor + new Vector3(-w / 2 + t / 2, h / 2, 0), new Vector3(t, h, l));
        Shapes.Box(m, mat, floor + new Vector3(0, h / 2, l / 2 - t / 2), new Vector3(w, h, t));
        Shapes.Box(m, mat, floor + new Vector3(0, h / 2, -l / 2 + t / 2), new Vector3(w, h, t));
        Shapes.Box(m, Roles.Hazard, floor + new Vector3(0, h, l / 2 - t / 2), new Vector3(w, t * 0.6f, t * 1.2f));
    }

    // ------------------------------------------------------------------ refinery (processing)

    private static void Refinery(BuildContext c, Module hull, Deck deck, Profile body, float zFront, float zN, float B, float H, float L)
    {
        var r = c.Shape;
        // Gas scoop at the bow: a flared octagonal funnel with a dark mouth and a grille ring.
        var sc = c.Add("gas_scoop", "scoop", ModuleTier.Secondary, hull.Id);
        var R0 = MathF.Min(B, H) * r.Range(0.28f, 0.36f);
        var y = body.Cy + H * 0.05f;
        Shapes.Prism(sc.Mesh, Roles.HullSecondary, new Vector3(0, y, zFront - 0.5f), new Vector3(0, y, zN), R0 * 0.6f, R0, 8, MathF.PI / 8, capB: true, capBMat: Roles.DarkMechanics);
        Shapes.Prism(sc.Mesh, Roles.Hazard, new Vector3(0, y, zN - (zN - zFront) * 0.15f), new Vector3(0, y, zN), R0 * 1.03f, R0 * 1.05f, 8, MathF.PI / 8, capB: false);
        c.AddMarker("tool", sc, new Vector3(0, y, zN), Vector3.UnitZ, R0);

        var plant = c.Add("process_plant", "plant", ModuleTier.Secondary, hull.Id);
        var tops = new List<Vector3>();
        // Tank farm: vertical tanks with domed caps and bands.
        for (var i = 0; i < r.Int(2, 5); i++)
        {
            var tr = MathF.Min(deck.Width * r.Range(0.14f, 0.22f), H * 0.35f);
            if (!deck.TryPlace(r, tr * 2.1f, tr * 2.1f, 0.8f, out var x, out var z)) continue;
            var th = H * r.Range(0.4f, 0.85f);
            var foot = new Vector3(x, deck.Y - 0.2f, z);
            Shapes.Prism(plant.Mesh, Roles.HullSecondary, foot, foot + new Vector3(0, th, 0), tr, tr, 10, 0);
            Shapes.Geodesic(plant.Mesh, Roles.HullSecondary, foot + new Vector3(0, th, 0), tr, 1, 0f);
            for (var b = 1; b <= 2; b++)
                Shapes.Prism(plant.Mesh, Roles.DarkMechanics, foot + new Vector3(0, th * b / 3f - 0.3f, 0), foot + new Vector3(0, th * b / 3f + 0.3f, 0), tr * 1.04f, tr * 1.04f, 10, 0);
            tops.Add(foot + new Vector3(0, th * 0.85f, 0));
        }
        // Spheres on short skirts.
        for (var i = 0; i < r.Int(0, 2); i++)
        {
            var sr = MathF.Min(deck.Width * r.Range(0.15f, 0.22f), H * 0.35f);
            if (!deck.TryPlace(r, sr * 2.1f, sr * 2.1f, 0.8f, out var x, out var z)) continue;
            var cy = deck.Y + sr * 1.25f;
            Shapes.Prism(plant.Mesh, Roles.DarkMechanics, new Vector3(x, deck.Y - 0.2f, z), new Vector3(x, cy, z), sr * 0.55f, sr * 0.5f, 8, 0);
            Shapes.Geodesic(plant.Mesh, Roles.Radome, new Vector3(x, cy, z), sr, 2);
            tops.Add(new Vector3(x, cy, z));
        }
        // Chimney stacks with a hot glow at the lip.
        for (var i = 0; i < r.Int(1, 3); i++)
        {
            var cr = Num.Clamp(B * r.Range(0.03f, 0.05f), 0.8f, 4f);
            if (!deck.TryPlace(r, cr * 2.6f, cr * 2.6f, 0.8f, out var x, out var z)) continue;
            var ch = H * r.Range(0.8f, 1.4f);
            var foot = new Vector3(x, deck.Y - 0.2f, z);
            Shapes.Prism(plant.Mesh, Roles.HullSecondary, foot, foot + new Vector3(0, ch, 0), cr * 1.2f, cr, 8, MathF.PI / 8);
            Shapes.Prism(plant.Mesh, Roles.DarkMechanics, foot + new Vector3(0, ch, 0), foot + new Vector3(0, ch + cr * 0.6f, 0), cr * 1.1f, cr * 1.1f, 8, MathF.PI / 8, capB: true, capBMat: Roles.EngineGlow);
            Shapes.Prism(plant.Mesh, Roles.Hazard, foot + new Vector3(0, ch * 0.8f, 0), foot + new Vector3(0, ch * 0.86f, 0), cr * 1.04f, cr * 1.03f, 8, MathF.PI / 8);
            c.AddMarker("vent", plant, foot + new Vector3(0, ch + cr * 0.6f, 0), Vector3.UnitY, cr);
        }
        // Pipe rack: a header pipe along the centre line on trestles, with a branch to every tank.
        if (tops.Count > 0)
        {
            var hr = Num.Clamp(H * 0.03f, 0.3f, 1.2f);
            var hy = deck.Y + H * 0.18f;
            var z0 = tops.Min(t => t.Z) - 2;
            var z1 = tops.Max(t => t.Z) + 2;
            Shapes.Prism(plant.Mesh, Roles.DarkMechanics, new Vector3(0, hy, z0), new Vector3(0, hy, z1), hr, hr, 8, MathF.PI / 8);
            for (var z = z0; z <= z1; z += MathF.Max(4f, (z1 - z0) / 6))
                Shapes.Beam(plant.Mesh, Roles.Hazard, new Vector3(0, deck.Y - 0.2f, z), new Vector3(0, hy, z), hr * 0.8f);
            foreach (var t in tops)
            {
                var k = new Vector3(t.X, hy, t.Z);
                Shapes.Beam(plant.Mesh, Roles.DarkMechanics, new Vector3(0, hy, t.Z), k, hr * 0.8f);
                Shapes.Beam(plant.Mesh, Roles.DarkMechanics, k, t, hr * 0.8f);
            }
        }
        if (plant.Mesh.IsEmpty) c.Ship.Modules.Remove(plant);
    }

    // ------------------------------------------------------------------ crane (construction)

    private static void Construction(BuildContext c, Module hull, Deck deck, Profile body, float zFront, float zN, float B, float H, float L)
    {
        var r = c.Shape;
        // The crane: a turntable and cab on the deck, a tapering lattice boom over the bow, backstays and a hook.
        var bw = MathF.Min(deck.Width * 0.5f, B * 0.3f);
        if (deck.TryPlace(r, bw, bw, 1f, out var cx, out var cz, centerX: 0))
        {
            var m = c.Add("crane", "crane", ModuleTier.Secondary, hull.Id);
            var y = deck.Y;
            Shapes.Prism(m.Mesh, Roles.DarkMechanics, new Vector3(cx, y - 0.2f, cz), new Vector3(cx, y + bw * 0.25f, cz), bw * 0.5f, bw * 0.45f, 10, 0);
            Shapes.Box(m.Mesh, Roles.Hazard, new Vector3(cx, y + bw * 0.5f, cz), new Vector3(bw * 0.7f, bw * 0.5f, bw * 0.8f));
            Shapes.Box(m.Mesh, Roles.Glass, new Vector3(cx + bw * 0.2f, y + bw * 0.55f, cz + bw * 0.41f), new Vector3(bw * 0.25f, bw * 0.22f, 0.2f));
            var pivot = new Vector3(cx, y + bw * 0.7f, cz + bw * 0.2f);
            var reach = MathF.Min(zN + L * 0.02f - pivot.Z, L * r.Range(0.4f, 0.6f));
            var elev = r.Range(0.3f, 0.6f);
            var dir = Vector3.Normalize(new Vector3(r.Range(-0.12f, 0.12f), MathF.Sin(elev), MathF.Cos(elev)));
            var len = reach / MathF.Max(dir.Z, 0.5f);
            var tip = pivot + dir * len;
            Truss(m.Mesh, pivot, tip, bw * 0.32f, bw * 0.12f, MathF.Max(0.2f, bw * 0.03f), Roles.Hazard);
            // A-frame and backstays.
            var aTop = new Vector3(cx, y + bw * 1.8f, cz - bw * 0.3f);
            Shapes.Beam(m.Mesh, Roles.Hazard, new Vector3(cx + bw * 0.3f, y + bw * 0.7f, cz - bw * 0.2f), aTop, bw * 0.06f);
            Shapes.Beam(m.Mesh, Roles.Hazard, new Vector3(cx - bw * 0.3f, y + bw * 0.7f, cz - bw * 0.2f), aTop, bw * 0.06f);
            Shapes.Beam(m.Mesh, Roles.DarkMechanics, aTop, tip, MathF.Max(0.12f, bw * 0.015f));
            // Hook on its cable.
            var drop = MathF.Min(tip.Y - deck.Y, H * r.Range(0.5f, 0.9f));
            Shapes.Beam(m.Mesh, Roles.DarkMechanics, tip, tip - new Vector3(0, drop, 0), MathF.Max(0.1f, bw * 0.012f));
            Shapes.Box(m.Mesh, Roles.Hazard, tip - new Vector3(0, drop, 0), new Vector3(bw * 0.18f, bw * 0.22f, bw * 0.12f));
            Shapes.Box(m.Mesh, Roles.DarkMechanics, tip - new Vector3(0, drop + bw * 0.16f, 0), new Vector3(bw * 0.06f, bw * 0.12f, bw * 0.06f));
            c.AddMarker("crane_hook", m, tip - new Vector3(0, drop + bw * 0.2f, 0), -Vector3.UnitY, bw * 0.1f);
        }
        // A gantry over the deck with a trolley.
        var gw = deck.Width * 0.95f;
        var gl = Num.Clamp(L * 0.05f, 4f, 16f);
        if (deck.TryPlace(r, gw, gl, 0.5f, out var gx, out var gz))
        {
            var g = c.Add("gantry", "gantry", ModuleTier.Secondary, hull.Id);
            var gh = H * r.Range(0.35f, 0.55f);
            var t = MathF.Max(0.4f, gw * 0.03f);
            foreach (var s in new[] { 1f, -1f })
                foreach (var dz in new[] { -gl / 2 + t, gl / 2 - t })
                    Shapes.Beam(g.Mesh, Roles.HullSecondary, new Vector3(gx + s * (gw / 2 - t), deck.Y - 0.2f, gz + dz), new Vector3(gx + s * (gw / 2 - t), deck.Y + gh, gz + dz), t * 2);
            foreach (var dz in new[] { -gl / 2 + t, gl / 2 - t })
                Shapes.Box(g.Mesh, Roles.Hazard, new Vector3(gx, deck.Y + gh, gz + dz), new Vector3(gw, t * 2.2f, t * 2.2f));
            Shapes.Box(g.Mesh, Roles.DarkMechanics, new Vector3(gx + gw * r.Range(-0.3f, 0.3f), deck.Y + gh - t * 2, gz), new Vector3(gw * 0.12f, t * 3, gl * 0.9f));
        }
        // Girder stacks.
        var gs = c.Add("girders", "cargo", ModuleTier.Secondary, hull.Id);
        for (var i = 0; i < r.Int(1, 3); i++)
        {
            var sw = MathF.Min(deck.Width * 0.4f, B * 0.25f);
            var sl = Num.Clamp(L * 0.12f, 6f, 30f);
            if (!deck.TryPlace(r, sw, sl, 1f, out var sx, out var sz)) continue;
            var gwid = sw / 4;
            for (var layer = 0; layer < 3; layer++)
                for (var k = 0; k < 4 - layer % 2; k++)
                {
                    var x = sx - sw / 2 + gwid * (k + 0.5f + (layer % 2) * 0.5f);
                    Shapes.Box(gs.Mesh, layer % 2 == 0 ? Roles.HullSecondary : Roles.CargoA, new Vector3(x, deck.Y + gwid * 0.45f * (layer + 0.5f), sz), new Vector3(gwid * 0.8f, gwid * 0.42f, sl));
                }
        }
        if (gs.Mesh.IsEmpty) c.Ship.Modules.Remove(gs);
        // A work cradle at the bow: two girder arms with cross members.
        var cr = c.Add("work_cradle", "cradle", ModuleTier.Secondary, hull.Id);
        var aw = body.WBottom * 0.36f;
        var reachZ = zN - L * r.Range(0f, 0.01f);
        var ay = body.Cy - body.H * 0.15f;
        var bt = MathF.Max(0.4f, H * 0.04f);
        foreach (var s in new[] { 1f, -1f })
            Shapes.Beam(cr.Mesh, Roles.Hazard, new Vector3(s * aw, ay, zFront - 1), new Vector3(s * aw, ay, reachZ), bt * 2);
        for (var k = 1; k <= 3; k++)
        {
            var z = float.Lerp(zFront, reachZ, k / 3f);
            Shapes.Beam(cr.Mesh, Roles.DarkMechanics, new Vector3(-aw, ay, z), new Vector3(aw, ay, z), bt);
        }
        c.AddMarker("tool", cr, new Vector3(0, ay, reachZ), Vector3.UnitZ, aw);
    }

    /// <summary>A tapering square lattice truss from a to b: four chords and zig-zag bracing.</summary>
    private static void Truss(Mesh m, Vector3 a, Vector3 b, float wa, float wb, float t, string mat)
    {
        var axis = Vector3.Normalize(b - a);
        var side = Vector3.Normalize(Vector3.Cross(Vector3.UnitY, axis));
        if (float.IsNaN(side.X)) side = Vector3.UnitX;
        var up = Vector3.Cross(axis, side);
        Vector3[] Corners(Vector3 p, float w) => [p + (side + up) * w / 2, p + (side - up) * w / 2, p + (-side - up) * w / 2, p + (-side + up) * w / 2];
        var ca = Corners(a, wa);
        var cb = Corners(b, wb);
        for (var i = 0; i < 4; i++) Shapes.Beam(m, mat, ca[i], cb[i], t * 1.4f);
        var bays = Math.Max(3, (int)(Vector3.Distance(a, b) / (wa * 1.2f)));
        for (var k = 0; k < bays; k++)
        {
            var f0 = k / (float)bays;
            var f1 = (k + 1) / (float)bays;
            for (var i = 0; i < 4; i++)
            {
                var j = (i + 1) % 4;
                var p0 = Vector3.Lerp(ca[i], cb[i], f0);
                var p1 = Vector3.Lerp(ca[j], cb[j], f1);
                Shapes.Beam(m, mat, p0, p1, t);
            }
        }
    }

    // ------------------------------------------------------------------ salvage

    private static void Salvage(BuildContext c, Module hull, Deck deck, Profile body, float zFront, float zN, float B, float H, float L)
    {
        var r = c.Shape;
        var m = c.Add("claw_arms", "claw", ModuleTier.Secondary, hull.Id);
        var pairs = r.Chance(0.35) ? 2 : 1;
        var reach = zN - zFront;
        var t = MathF.Max(0.8f, MathF.Min(B, H) * 0.1f);
        for (var pr = 0; pr < pairs; pr++)
        {
            var y = pairs == 1 ? body.Cy - body.H * 0.05f : body.Cy + (pr == 0 ? 0.18f : -0.25f) * body.H;
            foreach (var s in new[] { 1f, -1f })
            {
                // Shoulder, upper arm out and forward, elbow, forearm in and forward, three-fingered claw.
                var shoulder = new Vector3(s * body.WBottom * 0.32f, y, zFront);
                var elbow = shoulder + new Vector3(s * B * r.Range(0.1f, 0.18f), y * 0 + H * r.Range(-0.05f, 0.08f), reach * 0.45f);
                var wrist = new Vector3(s * B * r.Range(0.12f, 0.2f), y + H * r.Range(-0.08f, 0.02f), zFront + reach * 0.88f);
                Shapes.Box(m.Mesh, Roles.DarkMechanics, shoulder + new Vector3(0, 0, t * 0.6f), new Vector3(t * 2.6f, t * 2.6f, t * 2));
                Shapes.Prism(m.Mesh, Roles.Hazard, shoulder, elbow, t * 1.1f, t * 0.95f, 6, 0);
                Shapes.Prism(m.Mesh, Roles.DarkMechanics, elbow - new Vector3(t * 1.3f, 0, 0), elbow + new Vector3(t * 1.3f, 0, 0), t * 1.25f, t * 1.25f, 8, MathF.PI / 8);
                Shapes.Prism(m.Mesh, Roles.Hazard, elbow, wrist, t * 0.95f, t * 0.8f, 6, 0);
                // Hydraulic rams alongside.
                var off = new Vector3(0, t * 1.3f, 0);
                Shapes.Prism(m.Mesh, Roles.HullSecondary, shoulder + off, Vector3.Lerp(shoulder, elbow, 0.75f) + off, t * 0.35f, t * 0.35f, 6, 0);
                Shapes.Prism(m.Mesh, Roles.Light, Vector3.Lerp(shoulder, elbow, 0.75f) + off, elbow + off * 0.6f, t * 0.2f, t * 0.2f, 6, 0);
                Shapes.Box(m.Mesh, Roles.DarkMechanics, wrist, new Vector3(t * 2.2f, t * 2.2f, t * 1.6f));
                for (var f = 0; f < 3; f++)
                {
                    var ang = f * MathF.Tau / 3 + (s > 0 ? 0 : MathF.PI / 3);
                    var spread = new Vector3(MathF.Cos(ang), MathF.Sin(ang), 0);
                    var knuckle = wrist + spread * t * 1.6f + new Vector3(0, 0, reach * 0.06f);
                    var tipP = wrist + spread * t * 0.5f + new Vector3(0, 0, reach * 0.14f);
                    Shapes.Prism(m.Mesh, Roles.DarkMechanics, wrist, knuckle, t * 0.4f, t * 0.35f, 4, MathF.PI / 4);
                    Shapes.Prism(m.Mesh, Roles.HullSecondary, knuckle, tipP, t * 0.35f, t * 0.08f, 4, MathF.PI / 4);
                }
                c.AddMarker("tool", m, wrist + new Vector3(0, 0, reach * 0.14f), Vector3.UnitZ, t * 2);
            }
        }
        // Scrap bins on deck, heaped with odd-angled junk.
        var bins = c.Add("scrap_bins", "hopper", ModuleTier.Secondary, hull.Id);
        for (var i = 0; i < r.Int(1, 3); i++)
        {
            var bw = MathF.Min(deck.Width * 0.6f, B * 0.4f);
            var bl = Num.Clamp(L * 0.08f, 5f, 20f);
            var bh = H * r.Range(0.15f, 0.25f);
            if (!deck.TryPlace(r, bw, bl, 1f, out var x, out var z)) continue;
            OpenBin(bins.Mesh, new Vector3(x, deck.Y, z), bw, bh, bl, Roles.HullSecondary);
            for (var k = 0; k < 6; k++)
            {
                var p = new Vector3(x + r.Range(-0.3f, 0.3f) * bw, deck.Y + bh * r.Range(0.6f, 1.1f), z + r.Range(-0.3f, 0.3f) * bl);
                var a = r.Range(0, MathF.PI);
                var ax = new Vector3(MathF.Cos(a), 0, MathF.Sin(a));
                var ay = Vector3.Normalize(new Vector3(0, 1, r.Range(-0.6f, 0.6f)));
                var az = Vector3.Normalize(Vector3.Cross(ax, ay));
                ay = Vector3.Cross(az, ax);
                Shapes.OBox(bins.Mesh, r.Pick(Roles.Cargo), p, ax, ay, az, new Vector3(bw * r.Range(0.08f, 0.2f), bh * r.Range(0.15f, 0.35f), bl * r.Range(0.08f, 0.2f)));
            }
        }
        if (bins.Mesh.IsEmpty) c.Ship.Modules.Remove(bins);
    }
}

using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// The warship ladder, on the shared hull body, prow, engines and bridge tiers. The class (kept in the plan's payload)
/// sets the length, the bridge, the turrets and the missile cells:
/// corvette (60–90 m, one turret), frigate (90–150 m, two), destroyer (150–220 m, three, superfiring),
/// cruiser (220–320 m, four or five), carrier (300–450 m: an overhanging flight deck with runway markings, an island
/// bridge on the starboard edge, a hangar mouth at the bow, an elevator and point defence on the deck edges).
/// </summary>
public static class Warships
{
    public static readonly string[] Classes = ["corvette", "frigate", "destroyer", "cruiser", "carrier"];

    public static void Plan(BuildContext c)
    {
        var r = c.PlanRng;
        var p = c.Plan;
        var lang = c.Lang;
        p.Family = "warship";
        p.Payload = p.Size switch
        {
            "small" => r.Weighted<string>([("corvette", 0.55), ("frigate", 0.45)]),
            "large" => r.Weighted<string>([("cruiser", 0.55), ("carrier", 0.45)]),
            _ => r.Weighted<string>([("frigate", 0.3), ("destroyer", 0.5), ("cruiser", 0.2)]),
        };
        if (c.Recipe.Overrides?.Payload is { } pinned && Classes.Contains(pinned)) p.Payload = pinned;
        var (lmin, lmax) = p.Payload switch
        {
            "corvette" => (60f, 90f), "frigate" => (90f, 150f), "destroyer" => (150f, 220f), "cruiser" => (220f, 320f), _ => (300f, 450f),
        };
        p.Length = c.Recipe.Length ?? r.Range(lmin, lmax);
        var L = p.Length;
        var carrier = p.Payload == "carrier";
        p.Beam = L * (carrier ? r.Range(0.16f, 0.2f) : r.Range(0.13f, 0.17f));
        p.Height = L * (carrier ? r.Range(0.1f, 0.12f) : r.Range(0.09f, 0.12f)) * (p.Payload == "corvette" ? 1.2f : 1f);
        p.HullForm = r.Chance(Math.Clamp(lang.Faceted + 0.3, 0, 1)) ? "faceted" : "slab";
        p.ProwStyle = carrier ? r.Weighted<string>([("blunt", 0.5), ("faceted", 0.5)]) : r.Weighted<string>([("wedge", 0.55), ("faceted", 0.45)]);
        p.BridgeStyle = r.Weighted<string>([("slope", 0.5), ("overhang", 0.3), ("blunt", 0.2)]);
        p.Tiers = p.Payload switch { "corvette" => 1, "frigate" => 2, "destroyer" => r.Int(2, 3), "cruiser" => r.Int(3, 4), _ => 2 };
        p.EngineLayout = p.Payload is "corvette" or "frigate" ? r.Weighted<string>([("2x1", 0.6), ("1x2", 0.4)]) : r.Weighted<string>([("2x2", 0.35), ("3x1", 0.35), ("4x1", 0.3)]);
        p.EngineStyle = r.Chance(lang.PodEngines) ? "pod" : "nacelle";
        p.Asymmetric = false;
        p.Ventral = carrier ? "none" : r.Weighted<string>([("keel", 0.5), ("none", 0.5)]);
        c.PreferredPalette = r.Chance(0.6) ? "military" : null;
    }

    public static void Build(BuildContext c)
    {
        var p = c.Plan;
        var r = c.Shape;
        float L = p.Length, B = p.Beam, H = p.Height;
        var engLen = L * r.Range(0.05f, 0.07f);
        var zRear = -L / 2 + engLen;
        var zFront = L / 2;
        var prowLen = Parts.ProwLength(p.ProwStyle, L, r, p.HullForm == "faceted") * (p.Payload == "carrier" ? 0.6f : 1f);
        var body = Parts.HullBody(c, 0, 0, B, H);
        var zBody0 = zRear + L * 0.02f;
        var zProw = zFront - prowLen;
        List<(float, Profile)> rings = [(zRear, body.Scaled(0.93f, 0.9f)), (zBody0, body), (zProw, body)];
        rings.AddRange(Parts.Prow(body, zProw, zFront, p.ProwStyle));
        var (hull, loft) = c.AddLoft("hull_main", "hull", ModuleTier.Primary, null, rings, Roles.HullMain);
        c.AddSides(hull, loft, 1, Profile.FacePosX, Profile.FaceNegX);
        c.AddBelly(hull, body, zBody0 + 1, zProw - 0.5f, main: true);
        c.AddEnvelope("habitable", hull, new Aabb(new Vector3(-B * 0.3f, body.Cy, zBody0 + 2), new Vector3(B * 0.3f, body.Top - 1, zProw)));
        Parts.Engines(c, hull.Id, "", zRear, 0, body.Cy + H * r.Range(-0.05f, 0.1f), B * r.Range(0.7f, 0.95f), H * r.Range(0.9f, 1.15f));
        var before = c.Ship.Modules.Count;
        if (p.Payload == "carrier") Carrier(c, hull, body, zBody0, zProw, zFront, L, B, H);
        else Combatant(c, hull, body, zBody0, zProw, L, B, H);
        Equipment.DropBlocking(c, before);
    }

    // ------------------------------------------------------------------ combatants

    private static void Combatant(BuildContext c, Module hull, Profile body, float zBody0, float zProw, float L, float B, float H)
    {
        var p = c.Plan;
        var r = c.Shape;
        var deck = c.AddDeck(hull, body.Top, -body.TopFlatHalf, body.TopFlatHalf, zBody0 + 1, zProw - 0.5f, 0);
        // The bridge amidships first, then the turrets fore and aft of it.
        var bridge = Parts.Superstructure(c, deck, p.Tiers, 0.55f, 0.85f, 0.85f, lengthMin: 0.18f, lengthMax: 0.3f, anchorAt: "mid");
        var turrets = p.Payload switch { "corvette" => 1, "frigate" => 2, "destroyer" => 3, _ => r.Int(4, 5) };
        // Turrets sit down into the hull's shoulders, so they can be wider than the flat strip on top of a faceted
        // hull. Their deck copies what the bridge already took, and hands back what the turrets take.
        var shoulder = body.Top - body.ClampedChamfers().ct * 0.8f;
        var hw = body.HalfWidthAt(shoulder) * 0.85f;
        var tdeck = new Deck { ModuleId = hull.Id, Y = shoulder, X0 = -hw, X1 = hw, Z0 = deck.Z0, Z1 = deck.Z1 };
        foreach (var u in deck.Used) tdeck.Take(-hw, hw, u.z0, u.z1);
        var R = Num.Clamp(MathF.Min(hw * 0.85f, L * 0.045f), 1.8f, 14f);
        var placed = 0;
        // Fore turrets, the second one raised to fire over the first.
        var fwd = (turrets + 1) / 2;
        for (var i = 0; i < turrets; i++)
        {
            var forward = i < fwd;
            var k = forward ? i : i - fwd;
            if (!tdeck.TryPlace(r, R * 2.2f, R * 3.4f, 0.8f, out var x, out var z, centerX: 0)) continue;
            deck.Take(deck.X0, deck.X1, z - R * 1.7f, z + R * 1.7f);
            // Turrets ahead of amidships face the bow, the others the stern.
            Turret(c, hull, new Vector3(0, tdeck.Y, z), R, facingBow: z > (deck.Z0 + deck.Z1) / 2, raised: k % 2 == 1, r);
            placed++;
        }
        // Missile cells on what is left of the deck.
        var cells = p.Payload switch { "corvette" => 1, "frigate" => 1, "destroyer" => 2, _ => 3 };
        for (var i = 0; i < cells; i++) Cells(c, tdeck, deck, L, r);
        _ = bridge;
        _ = placed;
    }

    /// <summary>A gun turret: a ring, a faceted gun house (sloped front) and two or three barrels, facing the bow or the stern.</summary>
    private static void Turret(BuildContext c, Module parent, Vector3 foot, float R, bool facingBow, bool raised, Rng r)
    {
        var m = c.Add("turret", "turret", ModuleTier.Secondary, parent.Id);
        var barb = raised ? R * 0.7f : R * 0.25f;
        Shapes.Prism(m.Mesh, Roles.HullSecondary, foot - new Vector3(0, 0.3f, 0), foot + new Vector3(0, barb, 0), R * 1.05f, R, 8, MathF.PI / 8);
        var hy = foot.Y + barb;
        var dir = facingBow ? 1f : -1f;
        var hp = new Profile(foot.X, hy + R * 0.32f, R * 1.6f, R * 1.25f, R * 0.64f, R * 0.22f, 0);
        var za = foot.Z - dir * R * 0.85f;
        var zb = foot.Z + dir * R * 0.85f;
        List<(float, Profile)> rings = facingBow
            ? [(za, hp), (zb - R * 0.35f, hp), (zb, hp.ScaledOnBase(0.85f, 0.55f))]
            : [(zb, hp.ScaledOnBase(0.85f, 0.55f)), (zb + R * 0.35f, hp), (za, hp)];
        new Loft(rings).Build(m.Mesh, Roles.HullMain);
        var n = r.Chance(0.5) ? 3 : 2;
        var len = R * r.Range(2f, 2.8f);
        for (var k = 0; k < n; k++)
        {
            var a = new Vector3(foot.X + (k - (n - 1) / 2f) * R * 0.42f, hy + R * 0.32f, zb - dir * R * 0.2f);
            Shapes.Prism(m.Mesh, Roles.DarkMechanics, a, a + new Vector3(0, 0, dir * len), R * 0.1f, R * 0.08f, 8, MathF.PI / 8);
        }
        c.AddMarker("hardpoint", m, new Vector3(foot.X, hy + R * 0.32f, zb + dir * len), new Vector3(0, 0, dir), R);
    }

    /// <summary>A grid of vertical missile cells: a dark frame with rows of hatches.</summary>
    private static void Cells(BuildContext c, Deck deck, Deck main, float L, Rng r)
    {
        var cw = MathF.Min(deck.Width * 0.6f, L * 0.05f);
        var cd = cw * r.Range(1.2f, 2.2f);
        if (cw < 2 || !deck.TryPlace(r, cw, cd, 0.8f, out var x, out var z)) return;
        main.Take(main.X0, main.X1, z - cd / 2, z + cd / 2);
        var m = c.Add("missile_cells", "missile_cells", ModuleTier.Secondary, deck.ModuleId);
        Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(x, deck.Y + 0.1f, z), new Vector3(cw, 0.5f, cd));
        var nx = Math.Max(2, (int)(cw / 1.6f));
        var nz = Math.Max(2, (int)(cd / 1.6f));
        for (var i = 0; i < nx; i++)
            for (var j = 0; j < nz; j++)
                Shapes.Box(m.Mesh, Roles.HullSecondary, new Vector3(x - cw / 2 + (i + 0.5f) * cw / nx, deck.Y + 0.4f, z - cd / 2 + (j + 0.5f) * cd / nz), new Vector3(cw / nx * 0.8f, 0.15f, cd / nz * 0.8f));
        c.AddMarker("missile_cells", m, new Vector3(x, deck.Y + 0.5f, z), Vector3.UnitY, MathF.Max(cw, cd) / 2);
    }

    // ------------------------------------------------------------------ carrier

    private static void Carrier(BuildContext c, Module hull, Profile body, float zBody0, float zProw, float zFront, float L, float B, float H)
    {
        var r = c.Shape;
        // The flight deck: a thick slab wider than the hull, from near the stern to past the prow's root.
        var fw = B * r.Range(1.3f, 1.55f);
        var fh = H * r.Range(0.12f, 0.18f);
        var y0 = body.Top - fh * 0.3f;
        var fz0 = zBody0 + L * 0.02f;
        var fz1 = zFront - L * r.Range(0.02f, 0.06f);
        var fp = new Profile(0, y0 + fh / 2, fw * 0.92f, fw, fh, fh * 0.15f, fh * 0.35f);
        var (fd, fl) = c.AddLoft("flight_deck", "flight_deck", ModuleTier.Primary, hull.Id,
            [(fz0, fp.ScaledOnTop(0.96f, 0.8f)), (fz0 + L * 0.02f, fp), (fz1 - L * 0.03f, fp), (fz1, fp.ScaledOnTop(0.94f, 0.7f))], Roles.HullSecondary, "tier");
        var top = fp.Top;
        foreach (var f in new[] { Profile.FaceTop })
            c.UseSurface(BuildContext.FaceKey(fd, 1, f), 0, 1, 0, 1);
        // Supports from the hull to the overhanging deck.
        var sup = c.Add("deck_supports", "strut", ModuleTier.Secondary, fd.Id);
        foreach (var s in new[] { 1f, -1f })
            for (var k = 0; k < 6; k++)
            {
                var z = float.Lerp(fz0 + L * 0.05f, fz1 - L * 0.08f, k / 5f);
                var a = new Vector3(s * body.HalfWidthAt(body.Cy) * 0.95f, body.Cy, z);
                var b = new Vector3(s * fw * 0.42f, y0 + 0.2f, z);
                Shapes.Beam(sup.Mesh, Roles.DarkMechanics, a, b, MathF.Max(0.6f, H * 0.04f));
            }
        // Runway markings: a dashed centre line, edge lines, and an angled landing lane with its own dashes.
        var mk = c.DetailsOf(fd);
        var lw = MathF.Max(0.3f, fw * 0.012f);
        var dash = L * 0.02f;
        for (var z = fz0 + L * 0.04f; z < fz1 - L * 0.04f; z += dash * 2)
            Shapes.Box(mk.Mesh, Roles.Light, new Vector3(-fw * 0.12f, top + 0.06f, z), new Vector3(lw, 0.1f, dash));
        foreach (var s in new[] { 1f, -1f })
            Shapes.Box(mk.Mesh, Roles.Hazard, new Vector3(s * fw * 0.44f, top + 0.05f, (fz0 + fz1) / 2), new Vector3(lw * 1.5f, 0.1f, fz1 - fz0 - L * 0.06f));
        var a0 = new Vector3(-fw * 0.05f, top + 0.07f, fz0 + L * 0.08f);
        var a1 = new Vector3(-fw * 0.38f, top + 0.07f, fz0 + (fz1 - fz0) * 0.6f);
        var steps = 10;
        for (var k = 0; k < steps; k += 2)
        {
            var p0 = Vector3.Lerp(a0, a1, k / (float)steps);
            var p1 = Vector3.Lerp(a0, a1, (k + 1) / (float)steps);
            Shapes.Beam(mk.Mesh, Roles.Hazard, p0, p1, lw * 1.4f);
        }
        // An elevator: an outlined square near the stern.
        var ez = fz0 + (fz1 - fz0) * 0.2f;
        var es = fw * 0.18f;
        foreach (var (dx, dz, sx, sz) in new[] { (0f, -1f, 1f, 0f), (0f, 1f, 1f, 0f), (-1f, 0f, 0f, 1f), (1f, 0f, 0f, 1f) })
            Shapes.Box(mk.Mesh, Roles.Hazard, new Vector3(-fw * 0.15f + dx * es / 2, top + 0.06f, ez + dz * es / 2), new Vector3(sx * es + lw, 0.1f, sz * es + lw));
        c.AddMarker("elevator", fd, new Vector3(-fw * 0.15f, top, ez), Vector3.UnitY, es / 2);
        // Hangar mouth in the bow face of the flight deck, lit inside, and the launch lane marker.
        var hw = fw * 0.4f;
        Shapes.Box(mk.Mesh, Roles.DarkMechanics, new Vector3(-fw * 0.08f, y0 + fh * 0.45f, fz1 + 0.05f), new Vector3(hw, fh * 0.7f, 0.4f));
        Shapes.Box(mk.Mesh, Roles.Light, new Vector3(-fw * 0.08f, y0 + fh * 0.8f, fz1 + 0.1f), new Vector3(hw, fh * 0.08f, 0.3f));
        c.AddMarker("hangar", fd, new Vector3(-fw * 0.08f, y0 + fh * 0.45f, fz1 + 0.3f), Vector3.UnitZ, hw / 2);
        c.AddMarker("launch_lane", fd, new Vector3(-fw * 0.12f, top + 0.2f, fz1 - L * 0.05f), Vector3.UnitZ, fw * 0.1f);

        // The island: the bridge tiers on the starboard edge, with a mast and radars.
        var iw = fw * r.Range(0.16f, 0.22f);
        var il = (fz1 - fz0) * r.Range(0.16f, 0.24f);
        var ix = fw * 0.5f - iw * 0.6f;
        var iz = fz0 + (fz1 - fz0) * r.Range(0.4f, 0.55f);
        var islandDeck = c.AddDeck(fd, top, ix - iw / 2, ix + iw / 2, iz - il / 2, iz + il / 2, 1);
        Parts.Superstructure(c, islandDeck, c.Plan.Tiers + 1, 0.95f, 1f, 1.1f, "island", 0.9f, 1f, anchorAt: "mid");
        // Point defence along the port and starboard edges (narrow strips beside the runway).
        foreach (var s in new[] { 1f, -1f })
        {
            var strip = c.AddDeck(fd, top, s > 0 ? fw * 0.36f : -fw * 0.5f + 0.5f, s > 0 ? fw * 0.5f - 0.5f : -fw * 0.36f, fz0 + L * 0.03f, fz1 - L * 0.03f, 1);
            strip.Take(ix - iw, ix + iw, iz - il, iz + il);
        }
        for (var i = 0; i < c.Count(2, 4); i++) Equipment.PointDefence(c);
    }
}

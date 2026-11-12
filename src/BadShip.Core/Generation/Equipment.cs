using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Functional landmarks placed on decks and side faces after the hull exists: sensors, masts, cranes, containers,
/// docking collars, cargo bays and weapon hardpoint placeholders. Every item checks its footprint or clearance first.
/// </summary>
public static class Equipment
{
    public static void Place(BuildContext c)
    {
        var r = c.Equip;
        var L = c.Plan.Length;
        var before = c.Ship.Modules.Count;
        // Big deck machinery first (housings, generators, hab blocks), then the small landmarks around it.
        Repeat(r.Int(2, 5), () => MachineryBlock(c));
        switch (c.Plan.Archetype)
        {
            case "research":
                Repeat(r.Int(1, 3), () => Radome(c, Num.Clamp(L * r.Range(0.014f, 0.024f), 1.8f, 5f)));
                if (r.Chance(0.65)) Dish(c, Num.Clamp(L * r.Range(0.02f, 0.03f), 2.5f, 6f));
                LatticeMast(c, Num.Clamp(L * r.Range(0.08f, 0.13f), 7f, 22f));
                Repeat(r.Int(2, 4), () => Antenna(c));
                Repeat(r.Int(1, 2), () => DockingCollar(c));
                if (r.Chance(0.5)) CargoBay(c);
                break;
            case "freighter":
                Repeat(r.Int(1, 2), () => Crane(c));
                if (c.Plan.Family != "spine") Repeat(r.Int(1, 3), () => ContainerStack(c));
                Repeat(r.Int(1, 3), () => CargoBay(c));
                Repeat(r.Int(1, 2), () => DockingCollar(c));
                if (r.Chance(0.4)) Radome(c, Num.Clamp(L * 0.012f, 1.6f, 3.5f));
                if (r.Chance(0.5)) LatticeMast(c, Num.Clamp(L * r.Range(0.05f, 0.08f), 6f, 14f));
                Repeat(r.Int(1, 3), () => Antenna(c));
                break;
            default: // patrol
                Repeat(r.Int(3, 6), () => Hardpoint(c));
                if (r.Chance(0.6)) Radome(c, Num.Clamp(L * 0.018f, 1.5f, 3.2f));
                if (r.Chance(0.5)) DockingCollar(c);
                if (r.Chance(0.4)) CargoBay(c);
                Repeat(r.Int(1, 3), () => Antenna(c));
                break;
        }
        DropBlocking(c, before);
    }

    /// <summary>
    /// Removes equipment that ended up inside an engine's exhaust or a docking corridor (a crane boom swung aft,
    /// a mast beside a collar): a bounded fix instead of an invalid ship.
    /// </summary>
    private static void DropBlocking(BuildContext c, int firstNew)
    {
        var ship = c.Ship;
        var volumes = ship.Markers.Where(k => k.Kind == "engine_exhaust").Select(Validation.ShipValidator.ExhaustVolume)
            .Concat(ship.Markers.Where(k => k.Kind == "docking_port").Select(Validation.ShipValidator.DockingVolume)).ToList();
        var owners = ship.Markers.Where(k => k.Kind is "engine_exhaust" or "docking_port").Select(k => k.ModuleId).ToHashSet();
        for (var i = ship.Modules.Count - 1; i >= firstNew; i--)
        {
            var m = ship.Modules[i];
            if (owners.Contains(m.Id)) continue;
            m.UpdateBounds();
            if (!volumes.Any(v => m.Bounds.Penetration(v) > 0.05f)) continue;
            ship.Modules.RemoveAt(i);
            ship.Markers.RemoveAll(k => k.ModuleId == m.Id);
        }
    }

    private static void Repeat(int n, Action a)
    {
        for (var i = 0; i < n; i++) a();
    }

    /// <summary>Tries decks in preference order (highest first when <paramref name="high"/>) for a free footprint.</summary>
    private static (Deck deck, float x, float z)? Spot(BuildContext c, float w, float d, bool high, float pad = 1f, Func<Deck, bool>? filter = null)
    {
        var r = c.Equip;
        var decks = c.Decks.Where(dk => filter?.Invoke(dk) ?? true)
            .OrderBy(dk => high ? -dk.Level - dk.Y * 0.01 : dk.Level + dk.Y * 0.01)
            .ThenBy(_ => r.NextDouble()).ToList();
        foreach (var deck in decks)
            if (deck.TryPlace(r, w, d, pad, out var x, out var z)) return (deck, x, z);
        return null;
    }

    /// <summary>A chamfered machinery housing on a main deck: fills empty decks with readable medium-scale masses.</summary>
    public static void MachineryBlock(BuildContext c)
    {
        var r = c.Equip;
        var p = c.Plan;
        var w = p.Beam * r.Range(0.16f, 0.32f);
        var d = p.Length * r.Range(0.05f, 0.11f);
        var h = p.Height * r.Range(0.15f, 0.32f);
        var upper = r.Chance(0.35);
        if (upper) { w *= 0.6f; d *= 0.7f; h *= 0.8f; }
        if (Spot(c, w, d, high: upper, 1.5f, dk => (upper || dk.Level == 0) && dk.Width > w + 2) is not var (deck, x, z)) return;
        // Off-centre on wide decks: industrial clutter, not a symmetrical wedding cake.

        var prof = new Profile(x, deck.Y - 0.3f + (h + 0.3f) / 2, w, w * r.Range(0.8f, 0.95f), h + 0.3f, MathF.Min(w, h) * r.Range(0.1f, 0.3f), 0);
        var front = prof.ScaledOnBase(0.9f, r.Range(0.6f, 0.9f));
        var (m, loft) = c.AddLoft("machinery", "machinery_block", ModuleTier.Secondary, deck.ModuleId,
            [(z - d / 2, prof.ScaledOnBase(0.92f, 0.85f)), (z - d / 2 + MathF.Min(h * 0.3f, d * 0.25f), prof), (z + d / 2 - MathF.Min(h * 0.4f, d * 0.3f), prof), (z + d / 2, front)],
            r.Chance(0.3) ? Roles.HullSecondary : Roles.HullMain, "tier");
        if (r.Chance(0.4)) Parts.SideWindows(c, m, loft, 1, 0.5f);
        c.AddDeck(m, prof.Top, x - prof.TopFlatHalf, x + prof.TopFlatHalf, z - d / 2 + MathF.Min(h * 0.3f, d * 0.25f) + 0.3f, z + d / 2 - MathF.Min(h * 0.4f, d * 0.3f) - 0.3f, 1);
    }

    // ------------------------------------------------------------------ sensors and masts

    public static void Radome(BuildContext c, float radius)
    {
        if (Spot(c, radius * 2.1f, radius * 2.1f, high: true) is not var (deck, x, z)) return;
        var m = c.Add("radome", "radome", ModuleTier.Secondary, deck.ModuleId);
        var baseH = radius * 0.55f;
        Shapes.Prism(m.Mesh, Roles.HullSecondary, new Vector3(x, deck.Y - 0.3f, z), new Vector3(x, deck.Y + baseH, z), radius * 0.6f, radius * 0.48f, 8, MathF.PI / 8);
        Shapes.Geodesic(m.Mesh, Roles.Radome, new Vector3(x, deck.Y + baseH + radius * 0.62f, z), radius, 1, -0.55f);
        c.AddMarker("sensor", m, new Vector3(x, deck.Y + baseH + radius * 1.6f, z), Vector3.UnitY, radius);
    }

    public static void Dish(BuildContext c, float radius)
    {
        var r = c.Equip;
        if (Spot(c, radius * 2f, radius * 2f, high: true) is not var (deck, x, z)) return;
        var m = c.Add("dish", "sensor_dish", ModuleTier.Secondary, deck.ModuleId);
        var postH = radius * 0.7f;
        Shapes.Prism(m.Mesh, Roles.HullSecondary, new Vector3(x, deck.Y - 0.3f, z), new Vector3(x, deck.Y + postH, z), radius * 0.2f, radius * 0.15f, 8, MathF.PI / 8);
        var hub = new Vector3(x, deck.Y + postH + radius * 0.35f, z);
        Shapes.Box(m.Mesh, Roles.DarkMechanics, hub - new Vector3(0, radius * 0.2f, 0), new Vector3(radius * 0.45f, radius * 0.4f, radius * 0.45f));
        var az = r.Range(0, MathF.Tau);
        var el = r.Range(0.5f, 1.1f);
        var axis = Vector3.Normalize(new Vector3(MathF.Cos(az) * MathF.Cos(el), MathF.Sin(el), MathF.Sin(az) * MathF.Cos(el)));
        Shapes.Prism(m.Mesh, Roles.Radome, hub - axis * radius * 0.1f, hub + axis * radius * 0.32f, radius * 0.22f, radius, 16, 0);
        Shapes.Beam(m.Mesh, Roles.DarkMechanics, hub + axis * radius * 0.3f, hub + axis * radius * 0.95f, 0.18f);
        Shapes.Box(m.Mesh, Roles.DarkMechanics, hub + axis * radius * 0.98f, new Vector3(0.4f));
        c.AddMarker("sensor", m, hub + axis * radius * 0.4f, axis, radius);
    }

    /// <summary>A tapering four-legged lattice tower with platforms, antennas and a red beacon.</summary>
    public static void LatticeMast(BuildContext c, float height)
    {
        var r = c.Equip;
        var s = height * 0.2f;
        if (Spot(c, s + 0.6f, s + 0.6f, high: true) is not var (deck, x, z)) return;
        var m = c.Add("lattice_mast", "lattice_mast", ModuleTier.Secondary, deck.ModuleId);
        var t = MathF.Max(0.2f, height * 0.014f);
        var y0 = deck.Y - 0.2f;
        Vector3 Corner(int i, float h)
        {
            var half = float.Lerp(s / 2, s * 0.17f, h / height);
            var sx = i is 0 or 3 ? -1 : 1;
            var sz = i is 0 or 1 ? -1 : 1;
            return new Vector3(x + sx * half, y0 + h, z + sz * half);
        }
        for (var i = 0; i < 4; i++) Shapes.Beam(m.Mesh, Roles.DarkMechanics, Corner(i, 0), Corner(i, height), t);
        var levels = Math.Max(3, (int)(height / 3.2f));
        for (var l = 1; l <= levels; l++)
        {
            var h0 = height * (l - 1) / levels;
            var h1 = height * l / levels;
            for (var i = 0; i < 4; i++)
            {
                Shapes.Beam(m.Mesh, Roles.DarkMechanics, Corner(i, h1), Corner((i + 1) % 4, h1), t * 0.8f);
                Shapes.Beam(m.Mesh, Roles.DarkMechanics, Corner(i, h0), Corner((i + 1) % 4, h1), t * 0.6f);
            }
        }
        foreach (var hf in new[] { r.Range(0.45f, 0.65f), 1f })
        {
            var half = float.Lerp(s / 2, s * 0.17f, hf) + 0.5f;
            Shapes.Box(m.Mesh, Roles.HullSecondary, new Vector3(x, y0 + height * hf, z), new Vector3(half * 2, 0.3f, half * 2));
        }
        var tip = y0 + height;
        var antennas = r.Int(1, 3);
        for (var i = 0; i < antennas; i++)
        {
            var off = new Vector3(r.Range(-s * 0.12f, s * 0.12f), 0, r.Range(-s * 0.12f, s * 0.12f));
            var ah = r.Range(2f, 6f);
            Shapes.Beam(m.Mesh, Roles.DarkMechanics, new Vector3(x, tip, z) + off, new Vector3(x, tip + ah, z) + off, 0.14f);
            if (i == 0)
            {
                Shapes.Box(m.Mesh, Roles.NavRed, new Vector3(x, tip + ah + 0.2f, z) + off, new Vector3(0.45f));
                c.AddMarker("nav_light", m, new Vector3(x, tip + ah + 0.2f, z) + off, Vector3.UnitY);
            }
        }
        // A small equipment box or sensor ball on the upper platform.
        if (r.Chance(0.6)) Shapes.Geodesic(m.Mesh, Roles.Radome, new Vector3(x, tip + 0.9f, z), 0.9f, 1, -0.5f);
        c.AddMarker("sensor", m, new Vector3(x, tip, z), Vector3.UnitY, s);
    }

    public static void Antenna(BuildContext c)
    {
        var r = c.Equip;
        if (Spot(c, 1.2f, 1.2f, high: r.Chance(0.7), 0.5f) is not var (deck, x, z)) return;
        var m = c.Add("antenna", "antenna", ModuleTier.Secondary, deck.ModuleId);
        var h = r.Range(3f, 11f) * Num.Clamp(c.Plan.Length / 140f, 0.6f, 1.3f);
        Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(x, deck.Y + 0.25f, z), new Vector3(0.9f, 1f, 0.9f));
        Shapes.Beam(m.Mesh, Roles.DarkMechanics, new Vector3(x, deck.Y + 0.5f, z), new Vector3(x, deck.Y + h, z), 0.16f);
        if (r.Chance(0.5)) Shapes.Beam(m.Mesh, Roles.DarkMechanics, new Vector3(x - 0.9f, deck.Y + h * 0.75f, z), new Vector3(x + 0.9f, deck.Y + h * 0.75f, z), 0.1f);
        if (r.Chance(0.6))
        {
            Shapes.Box(m.Mesh, Roles.NavRed, new Vector3(x, deck.Y + h + 0.15f, z), new Vector3(0.35f));
            c.AddMarker("nav_light", m, new Vector3(x, deck.Y + h + 0.15f, z), Vector3.UnitY);
        }
    }

    // ------------------------------------------------------------------ cargo handling

    /// <summary>A deck crane: slewing post, cab, inclined boom, cable and a hanging load (yellow, as cranes are).</summary>
    public static void Crane(BuildContext c)
    {
        var r = c.Equip;
        var s = Num.Clamp(c.Plan.Length * 0.008f, 0.9f, 1.8f);
        if (Spot(c, s * 3.2f, s * 3.2f, high: false, 1.5f) is not var (deck, x, z)) return;
        var m = c.Add("crane", "crane", ModuleTier.Secondary, deck.ModuleId);
        var paint = r.Chance(0.6) ? Roles.Hazard : Roles.Accent;
        var y = deck.Y;
        Shapes.Box(m.Mesh, Roles.DarkMechanics, new Vector3(x, y + s * 0.4f - 0.2f, z), new Vector3(s * 3f, s * 0.8f + 0.4f, s * 3f));
        var postTop = y + s * 4.5f;
        Shapes.Prism(m.Mesh, paint, new Vector3(x, y + s * 0.8f, z), new Vector3(x, postTop, z), s * 0.75f, s * 0.6f, 8, MathF.PI / 8);
        Shapes.Box(m.Mesh, paint, new Vector3(x, postTop + s * 0.5f, z), new Vector3(s * 1.8f, s * 1.2f, s * 1.8f));
        Shapes.Box(m.Mesh, Roles.Glass, new Vector3(x, postTop + s * 0.6f, z + s * 0.9f), new Vector3(s * 1.2f, s * 0.5f, 0.15f));
        // Swing the boom out over the side or along the deck.
        var az = r.Pick([0f, MathF.PI, MathF.PI / 2, -MathF.PI / 2]) + r.Range(-0.5f, 0.5f);
        var el = r.Range(0.35f, 0.75f);
        var boomLen = s * r.Range(8f, 12f);
        var dir = new Vector3(MathF.Sin(az) * MathF.Cos(el), MathF.Sin(el), MathF.Cos(az) * MathF.Cos(el));
        var root = new Vector3(x, postTop + s * 0.4f, z);
        var tip = root + dir * boomLen;
        var side = Vector3.Normalize(Vector3.Cross(dir, Vector3.UnitY));
        Shapes.Beam(m.Mesh, paint, root + side * s * 0.35f, tip + side * s * 0.15f, s * 0.35f);
        Shapes.Beam(m.Mesh, paint, root - side * s * 0.35f, tip - side * s * 0.15f, s * 0.35f);
        for (var i = 1; i < 6; i++)
        {
            var a = Vector3.Lerp(root, tip, i / 6f);
            var w = float.Lerp(0.35f, 0.15f, i / 6f) * s;
            Shapes.Beam(m.Mesh, Roles.DarkMechanics, a + side * w, a - side * w, s * 0.15f);
        }
        Shapes.Box(m.Mesh, Roles.DarkMechanics, tip, new Vector3(s * 0.7f));
        // Cable and load, kept above the deck.
        var load = new Vector3(s * 2.4f, s * 1.6f, s * 2.4f);
        var drop = MathF.Max(s, MathF.Min(s * r.Range(2f, 4f), tip.Y - y - load.Y - s * 1.5f));
        var hook = tip - new Vector3(0, drop, 0);
        Shapes.Beam(m.Mesh, Roles.DarkMechanics, tip, hook, 0.12f);
        Shapes.Box(m.Mesh, Roles.DarkMechanics, hook, new Vector3(s * 0.5f));
        Shapes.Box(m.Mesh, r.Pick(Roles.Cargo), hook - new Vector3(0, s * 0.3f + load.Y / 2, 0), load);
        c.AddMarker("crane_hook", m, hook, -Vector3.UnitY, s);
    }

    /// <summary>A block of containers sitting on a deck (barge and twin freighters).</summary>
    public static void ContainerStack(BuildContext c)
    {
        var r = c.Equip;
        var cw = Num.Clamp(c.Plan.Beam * 0.08f, 2.4f, 4f);
        var cl = cw * 3.4f;
        var nx = r.Int(2, 4);
        var nz = r.Int(1, 3);
        var ny = r.Int(1, 3);
        var w = nx * (cw + 0.3f);
        var d = nz * (cl + 0.5f);
        if (Spot(c, w, d, high: false, 1f, dk => dk.Level == 0) is not var (deck, x, z)) return;
        var m = c.Add("container_stack", "container_stack", ModuleTier.Secondary, deck.ModuleId);
        for (var ix = 0; ix < nx; ix++)
            for (var iz = 0; iz < nz; iz++)
            {
                var height = r.Chance(0.3) ? Math.Max(1, ny - 1) : ny;
                for (var iy = 0; iy < height; iy++)
                {
                    var center = new Vector3(x - w / 2 + (cw + 0.3f) * (ix + 0.5f), deck.Y + iy * (cw + 0.2f) + cw / 2 - 0.05f, z - d / 2 + (cl + 0.5f) * (iz + 0.5f));
                    Shapes.Box(m.Mesh, r.Chance(0.08) ? Roles.Accent : r.Pick(Roles.Cargo), center, new Vector3(cw, cw, cl));
                    foreach (var sz in new[] { -1f, 1f })
                        Shapes.Box(m.Mesh, Roles.DarkMechanics, center + new Vector3(0, 0, sz * (cl / 2 - 0.1f)), new Vector3(cw + 0.08f, cw + 0.08f, 0.3f));
                }
            }
    }

    // ------------------------------------------------------------------ side equipment

    /// <summary>A docking collar on a hull side, with its approach corridor kept clear.</summary>
    public static void DockingCollar(BuildContext c)
    {
        var r = c.Equip;
        var R = Num.Clamp(c.Plan.Length * 0.012f, 1.4f, 2.6f);
        var reach = R * 1.2f;
        var clearance = 12f;
        foreach (var side in c.Sides.OrderBy(_ => r.NextDouble()))
        {
            var (along, _) = side.Loft.FaceSize(side.Segment, side.Face);
            var du = R * 3f / along;
            if (du > 0.6f) continue;
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var u = r.Range(0.2f + du / 2, 0.8f - du / 2);
                if (!c.SurfaceFree(side.Key, u - du / 2, u + du / 2, 0.2f, 0.8f)) continue;
                var n = side.Loft.FaceNormal(side.Segment, side.Face);
                var axis = new Vector3(MathF.Sign(n.X), 0, 0);
                if (axis == Vector3.Zero) break;
                var pt = side.Loft.FacePoint(side.Segment, side.Face, u, 0.5f);
                var end = pt + axis * (reach + 0.4f);
                var corridor = Aabb.FromPoints(end + axis * 0.2f - new Vector3(0, R * 1.4f, R * 1.4f), end + axis * clearance + new Vector3(0, R * 1.4f, R * 1.4f));
                if (!c.Clear(corridor, side.Module.Id)) continue;
                c.UseSurface(side.Key, u - du / 2, u + du / 2, 0.15f, 0.85f);
                var m = c.Add("docking_collar", "docking_collar", ModuleTier.Secondary, side.Module.Id);
                Shapes.Prism(m.Mesh, Roles.HullSecondary, pt - axis * 1.5f, pt + axis * reach, R, R, 8, MathF.PI / 8);
                Shapes.Prism(m.Mesh, Roles.Hazard, pt + axis * reach * 0.45f, pt + axis * reach * 0.7f, R * 1.06f, R * 1.06f, 8, MathF.PI / 8);
                Shapes.Prism(m.Mesh, Roles.DarkMechanics, pt + axis * reach, end, R * 1.3f, R * 1.3f, 8, MathF.PI / 8, capBMat: Roles.DarkMechanics);
                foreach (var dz in new[] { -1f, 1f })
                    Shapes.Box(m.Mesh, Roles.Light, end + new Vector3(0, R * 1.1f, dz * R * 0.9f) + axis * 0.05f, new Vector3(0.35f, 0.35f, 0.35f));
                c.AddMarker("docking_port", m, end, axis, R, clearance);
                return;
            }
        }
    }

    /// <summary>A cargo bay opening on a hull side: dark recess, raised frame, interior light and a hazard sill.</summary>
    public static void CargoBay(BuildContext c)
    {
        var r = c.Equip;
        foreach (var side in c.Sides.OrderBy(_ => r.NextDouble()))
        {
            var loft = side.Loft;
            var (along, across) = loft.FaceSize(side.Segment, side.Face);
            if (across < 3.5f) continue;
            var bayLen = Num.Clamp(c.Plan.Length * r.Range(0.05f, 0.09f), 5f, 16f);
            var du = bayLen / along;
            if (du > 0.7f) continue;
            var lo = Parts.UpV(side.Face, 0.14f);
            var hi = Parts.UpV(side.Face, 0.78f);
            var (v0, v1) = lo < hi ? (lo, hi) : (hi, lo);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var u0 = r.Range(0.08f, 0.92f - du);
                if (!c.TryUseSurface(side.Key, u0 - 0.02f, u0 + du + 0.02f, v0 - 0.03f, v1 + 0.03f)) continue;
                var m = c.Add("cargo_bay", "cargo_bay", ModuleTier.Secondary, side.Module.Id);
                var s = side.Segment;
                var f = side.Face;
                var u1 = u0 + du;
                var fu = 0.5f / along;
                var fv = 0.45f / across;
                Shapes.Patch(m.Mesh, Roles.DarkMechanics, loft, s, f, u0, u1, v0, v1, 0.04f);
                Shapes.Patch(m.Mesh, Roles.HullSecondary, loft, s, f, u0 - fu, u0, v0 - fv, v1 + fv, 0.35f);
                Shapes.Patch(m.Mesh, Roles.HullSecondary, loft, s, f, u1, u1 + fu, v0 - fv, v1 + fv, 0.35f);
                Shapes.Patch(m.Mesh, Roles.HullSecondary, loft, s, f, u0, u1, v0 - fv, v0, 0.35f);
                Shapes.Patch(m.Mesh, Roles.HullSecondary, loft, s, f, u0, u1, v1, v1 + fv, 0.35f);
                // Light strip along the top of the opening, hazard sill along the bottom.
                var top = Parts.UpV(f, 0.72f);
                var top2 = Parts.UpV(f, 0.75f);
                Shapes.Patch(m.Mesh, Roles.Light, loft, s, f, u0 + du * 0.05f, u1 - du * 0.05f, MathF.Min(top, top2), MathF.Max(top, top2), 0.08f);
                var stripes = Math.Max(4, (int)(bayLen / 1.2f));
                var sv0 = Parts.UpV(f, 0.14f);
                var sv1 = Parts.UpV(f, 0.19f);
                for (var i = 0; i < stripes; i += 2)
                    Shapes.Patch(m.Mesh, Roles.Hazard, loft, s, f, u0 + du * i / stripes, u0 + du * (i + 1) / stripes, MathF.Min(sv0, sv1), MathF.Max(sv0, sv1), 0.09f);
                var n = loft.FaceNormal(s, f);
                c.AddMarker("cargo_door", m, loft.FacePoint(s, f, (u0 + u1) / 2, 0.5f) + n * 0.2f, n, bayLen / 2);
                return;
            }
        }
    }

    /// <summary>A weapon hardpoint placeholder: a mounting ring and cap. No weapon systems.</summary>
    public static void Hardpoint(BuildContext c)
    {
        var R = Num.Clamp(c.Plan.Length * 0.018f, 1.3f, 2.8f);
        if (Spot(c, R * 2.4f, R * 2.4f, high: false, 1.2f) is not var (deck, x, z)) return;
        var m = c.Add("hardpoint", "hardpoint", ModuleTier.Secondary, deck.ModuleId);
        Shapes.Prism(m.Mesh, Roles.DarkMechanics, new Vector3(x, deck.Y - 0.2f, z), new Vector3(x, deck.Y + 0.5f, z), R, R * 0.95f, 8, MathF.PI / 8);
        Shapes.Prism(m.Mesh, Roles.Accent, new Vector3(x, deck.Y + 0.5f, z), new Vector3(x, deck.Y + 0.7f, z), R * 0.95f, R * 0.95f, 8, MathF.PI / 8);
        Shapes.Box(m.Mesh, Roles.HullSecondary, new Vector3(x, deck.Y + 1.2f, z), new Vector3(R * 1.1f, R * 0.7f, R * 1.3f));
        c.AddMarker("hardpoint", m, new Vector3(x, deck.Y + 0.7f, z), Vector3.UnitY, R);
    }
}

using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// The kit pass: original parts in the categories of a hard-surface ship kit. Silhouette parts (stern winglets, heat
/// radiator panels, point-defence turrets, tanks strapped to the flanks) come after the equipment and draw from its
/// stream; surface parts (pipe runs along the flanks, clusters of machinery blocks on the decks) come at the end of
/// the detail pass. Everything checks the surface registry and its clearances first, and anything that ends up in an
/// exhaust or docking corridor is dropped. Ships only (fighters and citadels carry their own kit).
/// </summary>
public static class Kit
{
    public static void Silhouette(BuildContext c)
    {
        var r = c.Equip;
        var before = c.Ship.Modules.Count;
        var hull = c.Lofts.FirstOrDefault(l => l.Kind == "hull");
        if (hull is null) return;
        if (r.Chance(0.55)) Winglets(c, hull, r);
        if (r.Chance(c.Plan.Archetype == "research" ? 0.7 : 0.45)) Radiators(c, r);
        if (r.Chance(c.Plan.Archetype == "freighter" ? 0.5 : 0.25)) StrappedTanks(c, r);
        var pd = c.Count(0, 2) + (c.Plan.Archetype == "patrol" ? 1 : 0);
        for (var i = 0; i < pd; i++) Equipment.PointDefence(c);
        Equipment.DropBlocking(c, before);
    }

    public static void Surface(BuildContext c)
    {
        var r = c.Detail;
        Pipes(c, r);
        Greebles(c, r);
    }

    private static Aabb BoxOf(IEnumerable<Vector3> pts, float pad)
    {
        var b = Aabb.Empty;
        foreach (var p in pts) b = b.Include(p);
        return b.Expand(pad);
    }

    /// <summary>A pair of swept winglets low on the hull's flanks near the stern, at the chine.</summary>
    private static void Winglets(BuildContext c, LoftPart hull, Rng r)
    {
        var loft = hull.Loft;
        if (loft.Segments < 2) return;
        var p = loft.Profiles[1];
        var L = c.Plan.Length;
        var (_, cb) = p.ClampedChamfers();
        var y = p.Bottom + cb + p.H * 0.05f;
        var x0 = p.HalfWidthAt(y) - 0.3f;
        var z0 = loft.Zs[1] + L * r.Range(0.01f, 0.04f);
        var chord = L * r.Range(0.08f, 0.13f);
        var span = c.Plan.Beam * r.Range(0.14f, 0.28f);
        var droop = span * r.Range(-0.15f, 0.3f);
        var t = MathF.Max(0.3f, p.H * 0.035f);
        var outlines = new List<Vector3[]>();
        foreach (var s in new[] { 1f, -1f })
            outlines.Add([new(s * x0, y, z0), new(s * x0, y, z0 + chord), new(s * (x0 + span), y - droop, z0 + chord * 0.45f), new(s * (x0 + span), y - droop, z0 + chord * 0.1f)]);
        if (!outlines.All(o => c.Clear(BoxOf(o, t), hull.Module.Id))) return;
        var m = c.Add("winglets", "fin", ModuleTier.Secondary, hull.Module.Id);
        foreach (var o in outlines)
        {
            Fighters.BevelPlate(m.Mesh, Roles.HullSecondary, o, t, t * 0.9f);
            Shapes.Box(m.Mesh, Roles.NavRed, (o[2] + o[3]) / 2, new Vector3(t * 2, t * 2, t * 3));
        }
    }

    /// <summary>Paired side faces (port and starboard of the same loft segment), the largest first.</summary>
    private static List<(SideSurface port, SideSurface starboard)> SidePairs(BuildContext c) =>
        c.Sides.Where(s => s.Face == Profile.FacePosX)
            .Select(s => (s, c.Sides.FirstOrDefault(o => o.Module == s.Module && o.Segment == s.Segment && o.Face == Profile.FaceNegX)))
            .Where(x => x.Item2 != null && c.Ship.Modules.Contains(x.s.Module))
            .Select(x => (x.s, x.Item2!))
            .OrderByDescending(x => x.s.Loft.FaceSize(x.s.Segment, x.s.Face).along).ToList();

    /// <summary>Heat radiators: thin ribbed panels standing straight out from both flanks, stacked two or three high.</summary>
    private static void Radiators(BuildContext c, Rng r)
    {
        foreach (var (a, b) in SidePairs(c).Take(5))
        for (var attempt = 0; attempt < 6; attempt++)
        {
            var (along, across) = a.Loft.FaceSize(a.Segment, a.Face);
            var len = MathF.Min(along * r.Range(0.18f, 0.32f), c.Plan.Length * 0.16f);
            if (len < 3.5f || across < 2.5f) break;
            var du = len / along;
            var u0 = r.Range(0.08f, 0.92f - du);
            // The middle band of a flank is usually windows or side blocks: radiators take the low and high bands.
            // Two rows (low and high) first; then single rows, alternating.
            var n = across > 9 && attempt < 2 ? 2 : 1;
            var lowFirst = attempt % 2 == 0;
            var depth = c.Plan.Beam * r.Range(0.12f, 0.22f);
            var t = MathF.Max(0.15f, depth * 0.035f);
            var panels = new List<(SideSurface side, Vector3[] o, Vector3 n)>();
            var ok = true;
            foreach (var side in new[] { a, b })
            {
                var nrm = side.Loft.FaceNormal(side.Segment, side.Face);
                for (var k = 0; k < n; k++)
                {
                    var up = n == 1 ? (lowFirst ? 0.2f : 0.84f) : k == 0 ? 0.18f : 0.84f;
                    var v = Parts.UpV(side.Face, up);
                    var key = BuildContext.FaceKey(side.Module, side.Segment, side.Face);
                    if (!c.SurfaceFree(key, u0 - 0.01f, u0 + du + 0.01f, v - 0.04f, v + 0.04f)) { ok = false; break; }
                    var p0 = side.Loft.FacePoint(side.Segment, side.Face, u0, v);
                    var p1 = side.Loft.FacePoint(side.Segment, side.Face, u0 + du, v);
                    var flat = Vector3.Normalize(new Vector3(nrm.X, 0, nrm.Z));
                    Vector3[] o = [p0 - flat * 0.2f, p1 - flat * 0.2f, p1 + flat * depth * 0.85f, p0 + flat * depth];
                    panels.Add((side, o, nrm));
                }
                if (!ok) break;
            }
            if (!ok || !panels.All(x => c.Clear(BoxOf(x.o, t), x.side.Module.Id))) continue;
            var m = c.Add("radiators", "radiator", ModuleTier.Secondary, a.Module.Id);
            foreach (var (side, o, _) in panels)
            {
                Fighters.BevelPlate(m.Mesh, Roles.HullSecondary, o, t, t * 0.8f);
                var ribs = Math.Max(3, (int)(len / 1.6f));
                for (var i = 1; i < ribs; i++)
                {
                    var f = i / (float)ribs;
                    var q0 = Vector3.Lerp(o[0], o[1], f);
                    var q1 = Vector3.Lerp(o[3], o[2], f);
                    Shapes.Beam(m.Mesh, Roles.DarkMechanics, q0 + Vector3.UnitY * t * 0.6f, q1 + Vector3.UnitY * t * 0.6f, t * 0.8f);
                }
                Shapes.Beam(m.Mesh, Roles.Hazard, o[3] + Vector3.UnitY * t * 0.5f, o[2] + Vector3.UnitY * t * 0.5f, t * 1.2f);
                c.UseSurface(BuildContext.FaceKey(side.Module, side.Segment, side.Face), u0 - 0.01f, u0 + du + 0.01f, 0, 1);
            }
            return;
        }
    }

    /// <summary>Long tanks strapped low along both flanks, with dark strap bands and end caps.</summary>
    private static void StrappedTanks(BuildContext c, Rng r)
    {
        foreach (var (a, b) in SidePairs(c).Take(5))
        for (var attempt = 0; attempt < 4; attempt++)
        {
            var (along, across) = a.Loft.FaceSize(a.Segment, a.Face);
            var len = along * r.Range(0.25f, 0.5f);
            var R = MathF.Min(across * 0.085f, c.Plan.Length * 0.018f);
            if (len < 10 || R < 0.8f) continue;
            var du = len / along;
            var u0 = r.Range(0.06f, 0.94f - du);
            var up = r.Range(0.13f, 0.2f);
            var parts = new List<(SideSurface side, Vector3 p0, Vector3 p1, Vector3 n)>();
            foreach (var side in new[] { a, b })
            {
                var v = Parts.UpV(side.Face, up);
                var key = BuildContext.FaceKey(side.Module, side.Segment, side.Face);
                var dv = R * 2.4f / across;
                if (!c.SurfaceFree(key, u0, u0 + du, v - dv, v + dv)) break;
                var nrm = side.Loft.FaceNormal(side.Segment, side.Face);
                parts.Add((side, side.Loft.FacePoint(side.Segment, side.Face, u0, v) + nrm * R * 0.85f, side.Loft.FacePoint(side.Segment, side.Face, u0 + du, v) + nrm * R * 0.85f, nrm));
            }
            if (parts.Count < 2 || !parts.All(x => c.Clear(BoxOf([x.p0, x.p1], R), x.side.Module.Id))) continue;
            var m = c.Add("strapped_tanks", "tank", ModuleTier.Secondary, a.Module.Id);
            foreach (var (side, p0, p1, nrm) in parts)
            {
                Shapes.Prism(m.Mesh, Roles.HullSecondary, p0, p1, R, R, 8, MathF.PI / 8);
                var dir = Vector3.Normalize(p1 - p0);
                Shapes.Prism(m.Mesh, Roles.DarkMechanics, p0 - dir * R * 0.3f, p0, R * 0.6f, R * 0.95f, 8, MathF.PI / 8);
                Shapes.Prism(m.Mesh, Roles.DarkMechanics, p1, p1 + dir * R * 0.3f, R * 0.95f, R * 0.6f, 8, MathF.PI / 8);
                var straps = Math.Max(2, (int)(len / (R * 4)));
                for (var i = 0; i <= straps; i++)
                {
                    var q = Vector3.Lerp(p0, p1, (i + 0.5f) / (straps + 1));
                    Shapes.Prism(m.Mesh, Roles.DarkMechanics, q - dir * R * 0.12f, q + dir * R * 0.12f, R * 1.06f, R * 1.06f, 8, MathF.PI / 8);
                }
                var dv = R * 2.4f / across;
                var v = Parts.UpV(side.Face, up);
                c.UseSurface(BuildContext.FaceKey(side.Module, side.Segment, side.Face), u0, u0 + du, v - dv, v + dv);
            }
            return;
        }
    }

    /// <summary>Runs of parallel pipes along the flanks, with brackets, a coloured service line and elbows into the hull.</summary>
    private static void Pipes(BuildContext c, Rng r)
    {
        var L = c.Plan.Length;
        var rp = Num.Clamp(L * 0.0022f, 0.15f, 0.55f);
        foreach (var side in c.Sides.Where(s => c.Ship.Modules.Contains(s.Module)).ToList())
        {
            if (!r.Chance(0.5)) continue;
            var (along, across) = side.Loft.FaceSize(side.Segment, side.Face);
            var n = r.Int(2, 4);
            var dv = n * rp * 2.6f / across;
            if (dv > 0.5f) continue;
            var key = BuildContext.FaceKey(side.Module, side.Segment, side.Face);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var len = r.Range(0.25f, 0.7f);
                var u0 = r.Range(0.04f, 0.96f - len);
                var v0 = r.Range(0.1f, 0.9f - dv);
                if (!c.TryUseSurface(key, u0 - 0.01f, u0 + len + 0.01f, v0, v0 + dv)) continue;
                var m = c.DetailsOf(side.Module).Mesh;
                var nrm = side.Loft.FaceNormal(side.Segment, side.Face);
                var coloured = r.Int(-1, n - 1);
                for (var i = 0; i < n; i++)
                {
                    var v = v0 + (i + 0.5f) * dv / n;
                    var a = side.Loft.FacePoint(side.Segment, side.Face, u0, v) + nrm * rp * 1.3f;
                    var b = side.Loft.FacePoint(side.Segment, side.Face, u0 + len, v) + nrm * rp * 1.3f;
                    Shapes.Prism(m, i == coloured ? Roles.Hazard : Roles.DarkMechanics, a, b, rp, rp, 6);
                    Shapes.Prism(m, Roles.DarkMechanics, a, a - nrm * rp * 1.5f, rp, rp, 6);
                    Shapes.Prism(m, Roles.DarkMechanics, b, b - nrm * rp * 1.5f, rp, rp, 6);
                }
                var brackets = Math.Max(2, (int)(len * along / (L * 0.03f + 2)));
                for (var k = 0; k <= brackets; k++)
                {
                    var u = u0 + len * k / brackets;
                    var p0 = side.Loft.FacePoint(side.Segment, side.Face, u, v0);
                    var p1 = side.Loft.FacePoint(side.Segment, side.Face, u, v0 + dv);
                    Shapes.Beam(m, Roles.HullSecondary, p0 + nrm * rp * 1.4f, p1 + nrm * rp * 1.4f, rp * 0.9f);
                }
                break;
            }
        }
    }

    /// <summary>Clusters of machinery blocks on the decks: a little grid of boxes of different heights, with vents and a light.</summary>
    private static void Greebles(BuildContext c, Rng r)
    {
        var L = c.Plan.Length;
        foreach (var deck in c.Decks.Where(d => !d.Underside && c.Ship.Find(d.ModuleId) != null).ToList())
        {
            var w = Num.Clamp(L * r.Range(0.02f, 0.035f), 2f, 9f);
            var count = Math.Min(5, (int)(deck.Width * deck.Length / (w * w * 14)));
            for (var i = 0; i < count; i++)
            {
                var cw = w * r.Range(0.8f, 1.4f);
                var cd = w * r.Range(0.8f, 1.6f);
                if (!deck.TryPlace(r, cw, cd, 0.6f, out var x, out var z)) continue;
                var m = c.DetailsOf(c.Ship.Find(deck.ModuleId)!).Mesh;
                var gx = r.Int(2, 3);
                var gz = r.Int(2, 3);
                for (var a = 0; a < gx; a++)
                    for (var b = 0; b < gz; b++)
                    {
                        if (r.Chance(0.2)) continue;
                        var bw = cw / gx * r.Range(0.6f, 0.9f);
                        var bd = cd / gz * r.Range(0.6f, 0.9f);
                        var h = w * r.Range(0.12f, 0.45f);
                        var cx = x - cw / 2 + (a + 0.5f) * cw / gx;
                        var cz = z - cd / 2 + (b + 0.5f) * cd / gz;
                        var mat = r.Chance(0.5) ? Roles.DarkMechanics : Roles.HullSecondary;
                        Shapes.Box(m, mat, new Vector3(cx, deck.Y + h / 2 - 0.05f, cz), new Vector3(bw, h, bd));
                        if (r.Chance(0.3)) Shapes.Box(m, Roles.DarkMechanics, new Vector3(cx, deck.Y + h + 0.05f, cz), new Vector3(bw * 0.7f, 0.12f, bd * 0.7f));
                        else if (r.Chance(0.15)) Shapes.Box(m, Roles.Light, new Vector3(cx, deck.Y + h + 0.1f, cz), new Vector3(0.35f, 0.25f, 0.35f));
                    }
            }
        }
    }
}

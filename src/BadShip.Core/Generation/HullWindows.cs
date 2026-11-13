using System.Numerics;
using BadShip.Geometry;
using BadShip.Interior;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Real windows: when the recipe asks for an interior, it is planned right after the hull masses, and every window
/// it wants in a room along the hull wall is offered here. A window is accepted when one of the hull's side faces
/// (the vertical side or the chamfers next to it) covers its whole height there, the surface is still free, and
/// nothing stands outside it; it then reserves that patch of hull, so plates, markings, kit and damage stay off it.
/// The hull is rebuilt with the openings cut out, and each opening gets a see-through glass pane and a reveal (the
/// short tunnel through the hull's thickness to the interior wall), so people inside can be seen from outside.
/// </summary>
public static class HullWindows
{
    public const string Glass = "window_glass";

    public static void Apply(BuildContext c)
    {
        var ship = c.Ship;
        var hull = InteriorGenerator.Hull(ship);
        if (hull is null || hull.Lofts.Count == 0) return;
        var loft = hull.Lofts[0];
        var s = InteriorGenerator.BodySegment(loft);
        if (s < 0) return;
        var holes = new Dictionary<(int s, int f), List<(float u0, float u1, float v0, float v1)>>();
        var placed = new List<(WindowInfo w, int f)>();
        var za = loft.Zs[s];
        var zb = loft.Zs[s + 1];
        float U(float z) => (z - za) / (zb - za);

        var interior = InteriorGenerator.Build(ship, w =>
        {
            foreach (var f in w.Side > 0 ? new[] { Profile.FacePosX, Profile.FaceBottomPosX, Profile.FaceTopPosX } : new[] { Profile.FaceNegX, Profile.FaceBottomNegX, Profile.FaceTopNegX })
            {
                var ya = loft.Rings[s][f].Y;
                var yb = loft.Rings[s][(f + 1) % 8].Y;
                if (MathF.Abs(yb - ya) < 0.5f) continue;
                var lo = MathF.Min(ya, yb) + 0.15f;
                var hi = MathF.Max(ya, yb) - 0.15f;
                if (w.Y0 < lo || w.Y1 > hi) continue;
                float V(float y) => (y - ya) / (yb - ya);
                var (u0, u1) = (U(w.Z0), U(w.Z1));
                var (v0, v1) = (MathF.Min(V(w.Y0), V(w.Y1)), MathF.Max(V(w.Y0), V(w.Y1)));
                var key = BuildContext.FaceKey(hull, s, f);
                if (!c.SurfaceFree(key, u0 - 0.004f, u1 + 0.004f, v0 - 0.02f, v1 + 0.02f)) return false;
                var n = loft.FaceNormal(s, f);
                var box = Aabb.Empty;
                foreach (var (u, v) in new[] { (u0, v0), (u1, v0), (u1, v1), (u0, v1) })
                {
                    var p = loft.FacePoint(s, f, u, v);
                    box = box.Include(p + n * 0.2f).Include(p + n * 2f);
                }
                if (!c.Clear(box, hull.Id)) return false;
                c.UseSurface(key, u0 - 0.006f, u1 + 0.006f, v0 - 0.03f, v1 + 0.03f);
                if (!holes.TryGetValue((s, f), out var list)) holes[(s, f)] = list = [];
                list.Add((u0, u1, v0, v1));
                placed.Add((w, f));
                return true;
            }
            return false;
        });
        if (interior is null) return;
        ship.Interior = interior;
        AccessTrunks(c, interior);
        BlockWindows(c, interior);
        if (placed.Count == 0) return;

        // Rebuild the hull: the loft with openings, then everything that had been added to its mesh after the loft.
        var plain = new Mesh();
        loft.Build(plain, Roles.HullMain);
        var rebuilt = new Mesh();
        loft.BuildWithHoles(rebuilt, Roles.HullMain, holes);
        rebuilt.AppendFrom(hull.Mesh, plain.Positions.Count);
        hull.Mesh.Clear();
        hull.Mesh.AppendFrom(rebuilt, 0);

        ship.Materials[Glass] = new MaterialDef(Glass, Palettes.Linear("#9db4c9"), 0.05f, 0.1f, Vector3.Zero, 1) { Alpha = 0.3f, DoubleSided = true };
        var m = c.Add("hull_windows", "windows", ModuleTier.Secondary, hull.Id);
        foreach (var (w, f) in placed)
        {
            var ya = loft.Rings[s][f].Y;
            var yb = loft.Rings[s][(f + 1) % 8].Y;
            Vector3 H(float z, float y) => loft.FacePoint(s, f, U(z), (y - ya) / (yb - ya));
            Vector3 I(float z, float y) => new(w.WallX, y, z);
            var n = loft.FaceNormal(s, f);
            // The pane sits just inside the hull skin.
            Vector3[] pane = [H(w.Z0, w.Y0) - n * 0.05f, H(w.Z1, w.Y0) - n * 0.05f, H(w.Z1, w.Y1) - n * 0.05f, H(w.Z0, w.Y1) - n * 0.05f];
            m.Mesh.AddPolygon(Glass, pane, n);
            // The reveal: four faces from the hull opening to the interior wall's opening, facing into the tunnel.
            var centre = (H(w.Z0, w.Y0) + H(w.Z1, w.Y1) + I(w.Z0, w.Y0) + I(w.Z1, w.Y1)) / 4;
            void Face(Vector3 a, Vector3 b, Vector3 cc, Vector3 d) => m.Mesh.AddQuad(Roles.HullSecondary, a, b, cc, d, centre - (a + b + cc + d) / 4);
            Face(H(w.Z0, w.Y0), H(w.Z1, w.Y0), I(w.Z1, w.Y0), I(w.Z0, w.Y0));
            Face(H(w.Z0, w.Y1), I(w.Z0, w.Y1), I(w.Z1, w.Y1), H(w.Z1, w.Y1));
            Face(H(w.Z0, w.Y0), I(w.Z0, w.Y0), I(w.Z0, w.Y1), H(w.Z0, w.Y1));
            Face(H(w.Z1, w.Y0), H(w.Z1, w.Y1), I(w.Z1, w.Y1), I(w.Z1, w.Y0));
            c.AddMarker("window", m, (H(w.Z0, w.Y0) + H(w.Z1, w.Y1)) / 2, n, (w.Z1 - w.Z0) / 2);
        }
        hull.UpdateBounds();
    }

    /// <summary>
    /// A gondola hangs under the hull with open space between: the ladder up to it climbs inside an access trunk, a
    /// boxed tube from inside the gondola to inside the hull, built where the interior put the ladder.
    /// </summary>
    private static void AccessTrunks(BuildContext c, ShipInterior interior)
    {
        foreach (var l in interior.Lifts.Where(l => l.Kind == "ladder" && interior.Decks[l.Decks[0]].Part == "gondola"))
        {
            var gondola = c.Ship.Find("gondola");
            if (gondola is null) continue;
            var lo = interior.Decks[l.Decks[0]];
            var hi = interior.Decks[l.Decks[^1]];
            var (x0, x1, z0, z1) = (l.Shaft[0], l.Shaft[1], l.Shaft[2], l.Shaft[3]);
            var m = c.Add("access_trunk", "access_trunk", ModuleTier.Secondary, gondola.Id);
            const float skin = 0.45f;
            Shapes.BoxMinMax(m.Mesh, Roles.HullSecondary, new Vector3(x0 - skin, lo.CeilingY, z0 - skin), new Vector3(x1 + skin, hi.FloorY - Slab, z1 + skin));
            // Ribs round the trunk, so it reads as a pressurised tube.
            for (var y = lo.CeilingY + 0.8f; y < hi.FloorY - Slab - 0.3f; y += 1.2f)
                Shapes.BoxMinMax(m.Mesh, Roles.DarkMechanics, new Vector3(x0 - skin - 0.08f, y, z0 - skin - 0.08f), new Vector3(x1 + skin + 0.08f, y + 0.2f, z1 + skin + 0.08f));
        }
    }

    private const float Slab = InteriorGenerator.Slab;

    /// <summary>
    /// Windows in shaped block decks: the block's own glass panes over a room become openings. The block's loft is
    /// rebuilt with the holes, the pane's glass turns see-through, and a reveal joins the hole to the matching hole in
    /// the deck's inner skin (the same section, 0.3 m in).
    /// </summary>
    private static void BlockWindows(BuildContext c, ShipInterior interior)
    {
        var groups = interior.Windows.Where(w => w.Module != null).GroupBy(w => w.Module!).ToList();
        if (groups.Count == 0) return;
        c.Ship.Materials[Glass] = new MaterialDef(Glass, Palettes.Linear("#9db4c9"), 0.05f, 0.1f, Vector3.Zero, 1) { Alpha = 0.3f, DoubleSided = true };
        foreach (var g in groups)
        {
            var block = c.Ship.Find(g.Key);
            if (block is null || block.Lofts.Count == 0) continue;
            var loft = block.Lofts[0];
            var holes = new Dictionary<(int s, int f), List<(float u0, float u1, float v0, float v1)>>();
            foreach (var w in g)
            {
                if (!holes.TryGetValue((w.S, w.F), out var list)) holes[(w.S, w.F)] = list = [];
                list.Add((w.U0, w.U1, w.V0, w.V1));
            }
            // Rebuild: the loft with openings, then everything added to the block's mesh after it.
            var plain = new Mesh();
            loft.Build(plain, Roles.HullMain);
            var rebuilt = new Mesh();
            loft.BuildWithHoles(rebuilt, Roles.HullMain, holes);
            rebuilt.AppendFrom(block.Mesh, plain.Positions.Count);
            // The panes over the openings turn see-through.
            if (rebuilt.Groups.TryGetValue(Roles.Glass, out var glassIdx))
            {
                var keep = new List<int>();
                var clear = rebuilt.Groups.TryGetValue(Glass, out var ci) ? ci : rebuilt.Groups[Glass] = [];
                for (var t = 0; t + 2 < glassIdx.Count; t += 3)
                {
                    var centre = (rebuilt.Positions[glassIdx[t]] + rebuilt.Positions[glassIdx[t + 1]] + rebuilt.Positions[glassIdx[t + 2]]) / 3;
                    var target = g.Any(w => OnPane(loft, w, centre)) ? clear : keep;
                    target.Add(glassIdx[t]); target.Add(glassIdx[t + 1]); target.Add(glassIdx[t + 2]);
                }
                rebuilt.Groups[Roles.Glass] = keep;
                if (keep.Count == 0) rebuilt.Groups.Remove(Roles.Glass);
            }
            block.Mesh.Clear();
            block.Mesh.AppendFrom(rebuilt, 0);
            block.UpdateBounds();
            // Reveals from the block's opening to the skin's.
            var reveal = c.Add($"{block.Id}_windows", "windows", ModuleTier.Secondary, block.Id);
            foreach (var w in g)
            {
                float Z(float u) => loft.Zs[w.S] + (loft.Zs[w.S + 1] - loft.Zs[w.S]) * u;
                Vector3 H(float u, float v) => loft.FacePoint(w.S, w.F, u, v);
                Vector3 I(float u, float v)
                {
                    var sec = Skin.Inset(Skin.SectionAt(loft, Z(u)), 0.3f);
                    var p = Vector2.Lerp(sec[w.F], sec[(w.F + 1) % sec.Length], v);
                    return new Vector3(p, Z(u));
                }
                var corners = new[] { (w.U0, w.V0), (w.U1, w.V0), (w.U1, w.V1), (w.U0, w.V1) };
                var mid = (H(w.U0, w.V0) + H(w.U1, w.V1) + I(w.U0, w.V0) + I(w.U1, w.V1)) / 4;
                for (var i = 0; i < 4; i++)
                {
                    var (ua, va) = corners[i];
                    var (ub, vb) = corners[(i + 1) % 4];
                    var a = H(ua, va); var b = H(ub, vb); var cc = I(ub, vb); var d = I(ua, va);
                    reveal.Mesh.AddQuad(Roles.HullSecondary, a, b, cc, d, mid - (a + b + cc + d) / 4);
                }
                var n = loft.FaceNormal(w.S, w.F);
                c.AddMarker("window", reveal, (H(w.U0, w.V0) + H(w.U1, w.V1)) / 2, n, (Z(w.U1) - Z(w.U0)) / 2);
            }
            if (reveal.Mesh.IsEmpty) c.Ship.Modules.Remove(reveal);
        }
    }

    /// <summary>Whether a point lies on (or in the thin glass slab over) a window's pane.</summary>
    private static bool OnPane(Loft loft, WindowInfo w, Vector3 p)
    {
        var n = loft.FaceNormal(w.S, w.F);
        var q = new[] { loft.FacePoint(w.S, w.F, w.U0, w.V0), loft.FacePoint(w.S, w.F, w.U1, w.V0), loft.FacePoint(w.S, w.F, w.U1, w.V1), loft.FacePoint(w.S, w.F, w.U0, w.V1) };
        if (MathF.Abs(Vector3.Dot(p - q[0], n)) > 0.25f) return false;
        for (var i = 0; i < 4; i++)
        {
            var e = q[(i + 1) % 4] - q[i];
            var sign = Vector3.Dot(Vector3.Cross(e, p - q[i]), n);
            var sign0 = Vector3.Dot(Vector3.Cross(q[1] - q[0], q[2] - q[0]), n);
            if (sign * MathF.Sign(sign0) < -1e-4f) return false;
        }
        return true;
    }
}

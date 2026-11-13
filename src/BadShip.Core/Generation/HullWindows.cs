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
}

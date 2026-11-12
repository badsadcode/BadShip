using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// The last pass: armour plates, accent stripes, vents, running lights and manoeuvring thrusters. It goes into each
/// module's ".details" child so it can be dropped as a class, and it never covers windows, bays or collars (it checks
/// the surface registry). Large forms first, details last.
/// </summary>
public static class Details
{
    public static void Add(BuildContext c)
    {
        var r = c.Detail;
        var full = c.DetailLevel >= 2;
        var plateDepth = c.Plan.Archetype == "patrol" ? 0.38f : 0.18f;
        foreach (var part in c.Lofts)
        {
            var d = c.DetailsOf(part.Module);
            var loft = part.Loft;
            for (var s = 0; s < loft.Segments; s++)
            {
                if (loft.IsStep(s)) continue;
                for (var f = 0; f < 8; f++)
                {
                    var (along, across) = loft.FaceSize(s, f);
                    if (along < 3 || across < 2) continue;
                    var key = BuildContext.FaceKey(part.Module, s, f);
                    var isSide = f is Profile.FacePosX or Profile.FaceNegX;
                    var isTop = f == Profile.FaceTop;
                    var isBottomChamfer = f is Profile.FaceBottomPosX or Profile.FaceBottomNegX;
                    if (isSide || isTop || (full && f is Profile.FaceTopPosX or Profile.FaceTopNegX))
                        Plates(c, r, d.Mesh, loft, s, f, key, along, across, plateDepth, isTop ? 0.25 : 0.4);
                    if (full && isSide && part.Kind is "hull" or "tier") Vents(c, r, d.Mesh, loft, s, f, key, along, across);
                    if (isBottomChamfer && part.Kind is "hull" or "pod") RunningLights(r, d.Mesh, loft, s, f, along);
                }
            }
        }
        if (full) AccentStripes(c, r);
        Thrusters(c, r);
    }

    /// <summary>Raised panels on a grid with random gaps: the layered armour look, kept off anything already placed.</summary>
    private static void Plates(BuildContext c, Rng r, Mesh m, Loft loft, int s, int f, string key, float along, float across, float depth, double p)
    {
        var cellU = r.Range(5f, 11f);
        var cellV = r.Range(2.5f, 5f);
        var nu = Math.Max(1, (int)(along / cellU));
        var nv = Math.Max(1, (int)(across / cellV));
        var mu = 0.35f / along;
        var mv = 0.3f / across;
        for (var i = 0; i < nu; i++)
            for (var j = 0; j < nv; j++)
            {
                if (!r.Chance(p)) continue;
                // Sometimes a plate spans two cells along the hull.
                var span = i + 1 < nu && r.Chance(0.3) ? 2 : 1;
                var u0 = (float)i / nu + mu;
                var u1 = (float)(i + span) / nu - mu;
                var v0 = (float)j / nv + mv;
                var v1 = (float)(j + 1) / nv - mv;
                if (!c.TryUseSurface(key, u0, u1, v0, v1)) continue;
                var mat = r.Chance(0.75) ? Roles.HullSecondary : Roles.HullMain;
                Shapes.Patch(m, mat, loft, s, f, u0, u1, v0, v1, depth * r.Range(0.7f, 1.3f));
            }
    }

    private static void Vents(BuildContext c, Rng r, Mesh m, Loft loft, int s, int f, string key, float along, float across)
    {
        var count = r.Int(0, (int)(along / 25f) + 1);
        for (var k = 0; k < count; k++)
        {
            var w = 3f / along;
            var h = MathF.Min(0.25f, 1.6f / across);
            var u0 = r.Range(0.05f, 0.95f - w);
            var v0 = r.Range(0.1f, 0.9f - h);
            if (!c.TryUseSurface(key, u0 - 0.01f, u0 + w + 0.01f, v0 - 0.01f, v0 + h + 0.01f)) continue;
            Shapes.Patch(m, Roles.DarkMechanics, loft, s, f, u0, u0 + w, v0, v0 + h, 0.06f);
            var slats = 4;
            for (var i = 0; i < slats; i++)
            {
                var a = v0 + h * (i + 0.25f) / slats;
                Shapes.Patch(m, Roles.HullSecondary, loft, s, f, u0 + w * 0.06f, u0 + w * 0.94f, a, a + h * 0.35f / slats, 0.12f);
            }
        }
    }

    /// <summary>Small warm lights along the lower chamfers (ships in the references are lit from underneath).</summary>
    private static void RunningLights(Rng r, Mesh m, Loft loft, int s, int f, float along)
    {
        var spacing = r.Range(6f, 11f);
        var n = (int)(along / spacing);
        var v = r.Range(0.3f, 0.7f);
        for (var i = 0; i < n; i++)
        {
            var u = (i + 0.5f) / n;
            var du = 0.45f / along;
            Shapes.Patch(m, Roles.Light, loft, s, f, u - du, u + du, v - 0.06f, v + 0.06f, 0.12f);
        }
    }

    /// <summary>A bold diagonal-free colour band across the prow or the side (orange / yellow blocks in the references).</summary>
    private static void AccentStripes(BuildContext c, Rng r)
    {
        if (!r.Chance(0.55)) return;
        var hull = c.Lofts.FirstOrDefault(l => l.Kind == "hull");
        if (hull is null) return;
        var loft = hull.Loft;
        var s = Math.Min(loft.Segments - 1, 1);
        var u = r.Range(0.6f, 0.85f);
        var w = r.Range(0.03f, 0.06f);
        var d = c.DetailsOf(hull.Module);
        foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX, Profile.FaceTopPosX, Profile.FaceTopNegX })
        {
            var key = BuildContext.FaceKey(hull.Module, s, f);
            if (!c.TryUseSurface(key, u, u + w, 0.02f, 0.98f)) continue;
            Shapes.Patch(d.Mesh, Roles.Accent, loft, s, f, u, u + w, 0.02f, 0.98f, 0.1f);
        }
    }

    /// <summary>Manoeuvring thruster blocks on the corners of the main hull, each with an exhaust marker.</summary>
    private static void Thrusters(BuildContext c, Rng r)
    {
        var hull = c.Lofts.FirstOrDefault(l => l.Kind == "hull");
        if (hull is null) return;
        var d = c.DetailsOf(hull.Module);
        var loft = hull.Loft;
        var size = Num.Clamp(c.Plan.Length * 0.008f, 0.8f, 1.6f);
        var spots = new List<(int s, float u)> { (Math.Min(1, loft.Segments - 1), 0.08f), (Math.Min(1, loft.Segments - 1), 0.92f) };
        foreach (var (s, u) in spots)
            foreach (var f in new[] { Profile.FaceTopPosX, Profile.FaceTopNegX })
            {
                var n = loft.FaceNormal(s, f);
                if (n == Vector3.Zero) continue;
                var p = loft.FacePoint(s, f, u, 0.5f);
                var side = new Vector3(MathF.Sign(n.X), 0, 0);
                Shapes.Box(d.Mesh, Roles.DarkMechanics, p + n * (size * 0.4f), new Vector3(size * 1.6f, size * 1.2f, size * 1.6f));
                Shapes.Prism(d.Mesh, Roles.DarkMechanics, p + n * (size * 0.4f) + side * size * 0.6f, p + n * (size * 0.4f) + side * size * 1.1f, size * 0.3f, size * 0.42f, 8, 0);
                c.AddMarker("rcs_thruster", d, p + n * (size * 0.4f) + side * size * 1.1f, side, size * 0.42f);
            }
    }
}

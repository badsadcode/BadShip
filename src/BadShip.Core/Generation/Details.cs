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
        // Fighters: the same passes at their own scale (a 12 m craft gets hand-sized panels, not 8 m plates).
        var fighter = c.Plan.Family == "fighter";
        var k = c.DetailScale;
        if (Palettes.Banded(c.Plan.Palette)) SchemeBands(c);
        Surface.Before(c);
        var plateDepth = (c.Plan.Archetype is "patrol" or "fighter" ? 0.38f : 0.18f) * k;
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
                    if (along < 3 * k || across < 2 * k) continue;
                    var key = BuildContext.FaceKey(part.Module, s, f);
                    var isSide = f is Profile.FacePosX or Profile.FaceNegX;
                    var isTop = f == Profile.FaceTop;
                    var isBottomChamfer = f is Profile.FaceBottomPosX or Profile.FaceBottomNegX;
                    if (isSide || isTop || (full && f is Profile.FaceTopPosX or Profile.FaceTopNegX))
                        Plates(c, r, d.Mesh, loft, s, f, key, along, across, plateDepth, k, (isTop ? 0.25 : 0.4) * c.Lang.PlateDensity * (fighter ? 1.6 : c.Plan.Family == "citadel" ? 0.5 : 1));
                    if (full && isSide && part.Kind is "hull" or "tier") Vents(c, r, d.Mesh, loft, s, f, key, along, across, k);
                    if (isBottomChamfer && part.Kind is "hull" or "pod") RunningLights(c, r, d.Mesh, loft, s, f, key, along, k);
                }
            }
        }
        if (full || c.Lang.Signature == "accent_bands") AccentStripes(c, r);
        if (c.Plan.Family is not ("fighter" or "utility")) Thrusters(c, r);
        if (c.Plan.Family is not ("fighter" or "citadel" or "utility")) Kit.Surface(c);
        Surface.After(c);
    }

    /// <summary>Raised panels on a grid with random gaps: the layered armour look, kept off anything already placed.</summary>
    private static void Plates(BuildContext c, Rng r, Mesh m, Loft loft, int s, int f, string key, float along, float across, float depth, float k, double p)
    {
        var cellU = r.Range(5f, 11f) * k;
        var cellV = r.Range(2.5f, 5f) * k;
        var nu = Math.Max(1, (int)(along / cellU));
        var nv = Math.Max(1, (int)(across / cellV));
        var mu = 0.35f * k / along;
        var mv = 0.3f * k / across;
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
                // Pirates bolt on whatever plating they found.
                if (c.Plan.Palette == "pirate" && r.Chance(0.45)) mat = r.Pick(Roles.Cargo);
                Shapes.Patch(m, mat, loft, s, f, u0, u1, v0, v1, depth * r.Range(0.7f, 1.3f));
            }
    }

    private static void Vents(BuildContext c, Rng r, Mesh m, Loft loft, int s, int f, string key, float along, float across, float k)
    {
        var count = r.Int(0, (int)(along / (25f * k)) + 1);
        for (var n = 0; n < count; n++)
        {
            var w = 3f * k / along;
            var h = MathF.Min(0.25f, 1.6f * k / across);
            var u0 = r.Range(0.05f, 0.95f - w);
            var v0 = r.Range(0.1f, 0.9f - h);
            if (!c.TryUseSurface(key, u0 - 0.01f, u0 + w + 0.01f, v0 - 0.01f, v0 + h + 0.01f)) continue;
            Shapes.Patch(m, Roles.DarkMechanics, loft, s, f, u0, u0 + w, v0, v0 + h, 0.06f * k);
            var slats = 4;
            for (var i = 0; i < slats; i++)
            {
                var a = v0 + h * (i + 0.25f) / slats;
                Shapes.Patch(m, Roles.HullSecondary, loft, s, f, u0 + w * 0.06f, u0 + w * 0.94f, a, a + h * 0.35f / slats, 0.12f * k);
            }
        }
    }

    /// <summary>Small warm lights along the lower chamfers (ships in the references are lit from underneath).</summary>
    private static void RunningLights(BuildContext c, Rng r, Mesh m, Loft loft, int s, int f, string key, float along, float k)
    {
        var spacing = r.Range(6f, 11f) * k;
        var n = (int)(along / spacing);
        var v = r.Range(0.3f, 0.7f);
        for (var i = 0; i < n; i++)
        {
            var u = (i + 0.5f) / n;
            var du = 0.45f * k / along;
            if (!c.TryUseSurface(key, u - du, u + du, v - 0.06f, v + 0.06f)) continue;
            Shapes.Patch(m, Roles.Light, loft, s, f, u - du, u + du, v - 0.06f, v + 0.06f, 0.12f * k);
        }
    }

    /// <summary>
    /// The banded colour schemes (industrial, military, civilian, research, luxury) wear their accent in wide bands
    /// across the hull masses, pods and superstructure, like the concept sheets. They go on before the plates, so the
    /// plates work round them, and use their own random stream.
    /// </summary>
    private static void SchemeBands(BuildContext c)
    {
        var r = Rng.Stream(c.Recipe.StageSeeds?.GetValueOrDefault("details") is { } ds and > 0 ? ds : c.Recipe.Seed, "bands");
        var k = c.DetailScale;
        foreach (var part in c.Lofts.Where(l => l.Kind is "hull" or "pod" or "tier").Take(8).ToList())
        {
            var loft = part.Loft;
            var s = Enumerable.Range(0, loft.Segments).Where(i => !loft.IsStep(i)).OrderByDescending(i => loft.FaceSize(i, Profile.FacePosX).along).FirstOrDefault(-1);
            if (s < 0) continue;
            var (along, _) = loft.FaceSize(s, Profile.FacePosX);
            if (along < 4 * k) continue;
            var d = c.DetailsOf(part.Module);
            var bands = part.Kind == "hull" && along > 30 * k ? r.Int(1, 2) : 1;
            var w = Num.Clamp(r.Range(2.5f, 6f) * k / along, 0.03f, 0.2f);
            var u = r.Range(0.12f, 0.88f - w * bands * 2.5f);
            for (var b = 0; b < bands; b++)
            {
                var ub = u + b * w * 2.5f;
                foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX, Profile.FaceTopPosX, Profile.FaceTopNegX, Profile.FaceTop })
                {
                    var key = BuildContext.FaceKey(part.Module, s, f);
                    if (!c.TryUseSurface(key, ub, ub + w, 0.02f, 0.98f)) continue;
                    Shapes.Patch(d.Mesh, Roles.Accent, loft, s, f, ub, ub + w, 0.02f, 0.98f, 0.08f * k);
                }
            }
        }
    }

    /// <summary>A bold diagonal-free colour band across the prow or the side (orange / yellow blocks in the references).</summary>
    private static void AccentStripes(BuildContext c, Rng r)
    {
        var signature = c.Lang.Signature == "accent_bands";
        if (!signature && !r.Chance(Palettes.Banded(c.Plan.Palette) ? 0.9 : 0.4)) return;
        var hull = c.Lofts.FirstOrDefault(l => l.Kind == "hull");
        if (hull is null) return;
        var loft = hull.Loft;
        var s = Math.Min(loft.Segments - 1, 1);
        var u = r.Range(0.6f, 0.85f);
        var w = r.Range(0.03f, 0.06f);
        var d = c.DetailsOf(hull.Module);
        // The accent_bands signature: always a matched pair; otherwise a single band.
        for (var band = 0; band < (signature ? 2 : 1); band++)
        {
            var ub = u - band * w * 2.4f;
            foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX, Profile.FaceTopPosX, Profile.FaceTopNegX })
            {
                var key = BuildContext.FaceKey(hull.Module, s, f);
                if (!c.TryUseSurface(key, ub, ub + w, 0.02f, 0.98f)) continue;
                Shapes.Patch(d.Mesh, Roles.Accent, loft, s, f, ub, ub + w, 0.02f, 0.98f, 0.1f);
            }
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

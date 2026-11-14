using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// Surface detail on the big faces, after the plates, vents and lights (so it fills what they leave): flush panels in a
/// third hull tone, strakes (thin raised bands) along the flanks and the belly, rows of small blocks along the top
/// chamfer edges, and service hatches with hazard frames. Like the rest of the details it goes into each part's
/// ".details" child (so the LODs drop it), keeps off anything already on a surface (windows, bays, markings, plates)
/// and off other parts, and uses its own random stream, so every earlier pass stays as it was.
/// </summary>
public static class Surface
{
    /// <summary>The pieces that need room (strakes, hatches, machinery, edge blocks): before the plates.</summary>
    public static void Before(BuildContext c) => Run(c, true);

    /// <summary>The flush panels, filling what the plates leave: after them.</summary>
    public static void After(BuildContext c) => Run(c, false);

    private static void Run(BuildContext c, bool before)
    {
        if (c.DetailLevel < 1 || c.Plan.Family is "fighter") return;
        var r = Rng.Stream(c.Recipe.StageSeeds?.GetValueOrDefault("details") is { } ds and > 0 ? ds : c.Recipe.Seed, before ? "surface" : "panels");
        var k = c.DetailScale;
        var full = c.DetailLevel >= 2;
        // The parts that stand on the surfaces, for the details that stick out.
        var parts = c.Ship.Modules.Where(m => m.Tier != ModuleTier.Tertiary && !m.Mesh.IsEmpty)
            .Select(m => { m.UpdateBounds(); return (m.Id, m.Bounds); }).ToList();
        bool Clear(Aabb box, string own) => parts.All(p => p.Id == own || p.Bounds.Penetration(box) <= 0.02f);
        foreach (var part in c.Lofts.Where(p => p.Kind is "hull" or "tier" or "pod" or "spine" && c.Ship.Modules.Contains(p.Module)).ToList())
        {
            var loft = part.Loft;
            var mesh = c.DetailsOf(part.Module).Mesh;
            for (var s = 0; s < loft.Segments; s++)
            {
                if (loft.IsStep(s)) continue;
                for (var f = 0; f < 8; f++)
                {
                    var (along, across) = loft.FaceSize(s, f);
                    if (along < 4 * k || across < 1.5f * k) continue;
                    var key = BuildContext.FaceKey(part.Module, s, f);
                    var face = new Face(c, loft, s, f, key, along, across);
                    if (!before)
                    {
                        if (f is not (Profile.FaceTopPosX or Profile.FaceTopNegX)) Panels(face, r, mesh, k, f == Profile.FaceTop ? 0.55 : 0.6);
                        continue;
                    }
                    switch (f)
                    {
                        case Profile.FacePosX or Profile.FaceNegX:
                            if (across >= 3.5f * k) Strakes(face, r, mesh, k, r.Chance(0.5) ? [0.2f, 0.8f] : [r.Chance(0.5) ? 0.2f : 0.8f]);
                            Hatches(face, r, mesh, k, full ? 3 : 1);
                            if (full) Machinery(face, r, mesh, k, part.Module.Id, Clear, 4);
                            break;
                        case Profile.FaceTop:
                            Hatches(face, r, mesh, k, full ? 4 : 2);
                            if (full) Machinery(face, r, mesh, k, part.Module.Id, Clear, 3);
                            break;
                        case Profile.FaceTopPosX or Profile.FaceTopNegX:
                            if (full && part.Kind is "hull" or "tier" or "spine") EdgeBlocks(face, r, mesh, k, part.Module.Id, Clear);
                            break;
                        case Profile.FaceBottom:
                            if (across >= 3f * k) Strakes(face, r, mesh, k, [0.25f, 0.75f]);
                            Hatches(face, r, mesh, k, full ? 2 : 1);
                            break;
                        case Profile.FaceBottomPosX or Profile.FaceBottomNegX:
                            Hatches(face, r, mesh, k, full ? 2 : 1);
                            break;
                    }
                }
            }
        }
    }

    /// <summary>One face of a loft and what is needed to place things on it: points, its frame, its surface key.</summary>
    private sealed record Face(BuildContext C, Loft Loft, int S, int F, string Key, float Along, float Across)
    {
        public Vector3 At(float u, float v) => Loft.FacePoint(S, F, u, v);
        public Vector3 Normal => Loft.FaceNormal(S, F);
        public bool Free(float u0, float u1, float v0, float v1) => C.SurfaceFree(Key, u0, u1, v0, v1);
        public void Use(float u0, float u1, float v0, float v1) => C.UseSurface(Key, u0, u1, v0, v1);
        public void Patch(Mesh m, string mat, float u0, float u1, float v0, float v1, float depth) => Shapes.Patch(m, mat, Loft, S, F, u0, u1, v0, v1, depth);

        /// <summary>A flat panel just off the face (one quad): for flush details whose edges are too thin to see.</summary>
        public void Decal(Mesh m, string mat, float u0, float u1, float v0, float v1, float lift)
        {
            var o = Normal * lift;
            m.AddQuad(mat, At(u0, v0) + o, At(u1, v0) + o, At(u1, v1) + o, At(u0, v1) + o, Normal);
        }
    }

    /// <summary>Thin raised bands running along the face at the given heights, broken where something is already there.</summary>
    private static void Strakes(Face face, Rng r, Mesh m, float k, float[] heights)
    {
        var dv = 0.35f * k / face.Across;
        var step = 0.5f * k / face.Along;
        var mat = r.Chance(0.75) ? Roles.HullSecondary : Roles.DarkMechanics;
        foreach (var v in heights)
        {
            var (v0, v1) = (v - dv / 2, v + dv / 2);
            float? start = null;
            for (var u = 0.02f; u <= 0.98f + 1e-4f; u += step)
            {
                var free = u + step <= 0.98f + 1e-4f && face.Free(u, u + step, v0, v1);
                if (free) { start ??= u; continue; }
                if (start is { } a && (u - a) * face.Along >= 2 * k)
                {
                    face.Patch(m, mat, a, u, v0, v1, 0.2f * k);
                    face.Use(a, u, v0, v1);
                }
                start = null;
            }
        }
    }

    /// <summary>Service hatches: a hazard-striped frame round a dark plate with a handle.</summary>
    private static void Hatches(Face face, Rng r, Mesh m, float k, int max)
    {
        var count = Math.Min(max, (int)(face.Along * face.Across / (220f * k * k)) + (r.Chance(0.5) ? 1 : 0));
        for (var n = 0; n < count; n++)
        {
            var w = (r.Chance(0.5) ? 1.6f : 2.6f) * k / face.Along;
            var h = MathF.Min(0.6f, 1.6f * k / face.Across);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var u0 = r.Range(0.05f, 0.95f - w);
                var v0 = r.Range(0.12f, 0.88f - h);
                var pu = 0.3f * k / face.Along;
                var pv = 0.3f * k / face.Across;
                if (!face.Free(u0 - pu, u0 + w + pu, v0 - pv, v0 + h + pv)) continue;
                face.Use(u0 - pu, u0 + w + pu, v0 - pv, v0 + h + pv);
                var iu = 0.14f * k / face.Along;
                var iv = 0.14f * k / face.Across;
                face.Decal(m, Roles.Hazard, u0, u0 + w, v0, v0 + h, 0.03f * k);
                face.Decal(m, Roles.DarkMechanics, u0 + iu, u0 + w - iu, v0 + iv, v0 + h - iv, 0.05f * k);
                var hu = u0 + w * 0.5f;
                face.Patch(m, Roles.HullSecondary, hu - 0.25f * k / face.Along, hu + 0.25f * k / face.Along, v0 + h * 0.75f, v0 + h * 0.75f + 0.1f * k / face.Across, 0.16f * k);
                break;
            }
        }
    }

    /// <summary>Flush panels in the third hull tone, on a grid with gaps, in whatever the earlier passes left free.</summary>
    private static void Panels(Face face, Rng r, Mesh m, float k, double p)
    {
        var nu = Math.Max(1, (int)(face.Along / (r.Range(7f, 14f) * k)));
        var nv = Math.Max(1, (int)(face.Across / (r.Range(3f, 6f) * k)));
        var mu = 0.25f * k / face.Along;
        var mv = 0.25f * k / face.Across;
        for (var i = 0; i < nu; i++)
            for (var j = 0; j < nv; j++)
            {
                if (!r.Chance(p)) continue;
                var (u0, u1, v0, v1) = ((float)i / nu + mu, (float)(i + 1) / nu - mu, (float)j / nv + mv, (float)(j + 1) / nv - mv);
                // Shrink until it fits round what is there, or give up.
                for (var t = 0; t < 3 && !face.Free(u0, u1, v0, v1); t++)
                {
                    var cu = (u0 + u1) / 2;
                    var cv = (v0 + v1) / 2;
                    (u0, u1, v0, v1) = (cu - (cu - u0) * 0.6f, cu + (u1 - cu) * 0.6f, cv - (cv - v0) * 0.6f, cv + (v1 - cv) * 0.6f);
                }
                if ((u1 - u0) * face.Along < 1.5f * k || (v1 - v0) * face.Across < 1f * k || !face.Free(u0, u1, v0, v1)) continue;
                face.Use(u0, u1, v0, v1);
                face.Decal(m, Roles.HullPanel, u0, u1, v0, v1, 0.03f * k);
            }
    }

    /// <summary>
    /// A machinery recess: a dark sunken-looking patch packed with small blocks of different heights in a little grid,
    /// sometimes a pipe along it and a light: the classic greeble.
    /// </summary>
    private static void Machinery(Face face, Rng r, Mesh m, float k, string own, Func<Aabb, string, bool> clear, int max)
    {
        var count = Math.Min(max, (int)(face.Along * face.Across / (200f * k * k)));
        var n = face.Normal;
        for (var i = 0; i < count; i++)
        {
            var w = r.Range(3f, 6.5f) * k / face.Along;
            var h = MathF.Min(0.7f, r.Range(1.3f, 2.6f) * k / face.Across);
            for (var attempt = 0; attempt < 6; attempt++)
            {
                var u0 = r.Range(0.04f, 0.96f - w);
                var v0 = r.Range(0.08f, 0.92f - h);
                if (!face.Free(u0, u0 + w, v0, v0 + h)) continue;
                var mid = face.At(u0 + w / 2, v0 + h / 2);
                var reach = MathF.Max(w * face.Along, h * face.Across) / 2;
                if (!clear(new Aabb(mid - new Vector3(reach), mid + new Vector3(reach)), own)) continue;
                face.Use(u0, u0 + w, v0, v0 + h);
                face.Decal(m, Roles.DarkMechanics, u0, u0 + w, v0, v0 + h, 0.03f * k);
                var gu = r.Int(2, 5);
                var gv = r.Int(1, 3);
                var along = Vector3.Normalize(face.At(u0 + w, v0) - face.At(u0, v0));
                var across = Vector3.Normalize(Vector3.Cross(n, along));
                var cw = w * face.Along / gu;
                var ch = h * face.Across / gv;
                for (var a = 0; a < gu; a++)
                    for (var b = 0; b < gv; b++)
                    {
                        if (r.Chance(0.15)) continue;
                        var bw = cw * r.Range(0.55f, 0.85f);
                        var bh = ch * r.Range(0.55f, 0.85f);
                        var tall = r.Range(0.12f, 0.55f) * k;
                        var c = face.At(u0 + (a + 0.5f) * w / gu, v0 + (b + 0.5f) * h / gv) + n * (0.03f * k + tall / 2);
                        var mat = r.Chance(0.45) ? Roles.HullSecondary : r.Chance(0.5) ? Roles.HullPanel : Roles.DarkMechanics;
                        Shapes.OBox(m, mat, c, along, n, across, new Vector3(bw / 2, tall / 2, bh / 2));
                        if (r.Chance(0.12)) Shapes.OBox(m, Roles.Light, c + n * (tall / 2 + 0.04f * k), along, n, across, new Vector3(0.15f, 0.04f, 0.15f) * k);
                    }
                // A pipe along the recess.
                if (r.Chance(0.5))
                {
                    var pv = v0 + h * r.Range(0.15f, 0.85f);
                    var pr = 0.12f * k;
                    Shapes.Prism(m, r.Chance(0.3) ? Roles.Hazard : Roles.DarkMechanics, face.At(u0, pv) + n * (0.03f * k + pr * 2), face.At(u0 + w, pv) + n * (0.03f * k + pr * 2), pr, pr, 6);
                }
                break;
            }
        }
    }

    /// <summary>Groups of small blocks along the upper edge of a top chamfer, like the machinery along a ship's gunwale.</summary>
    private static void EdgeBlocks(Face face, Rng r, Mesh m, float k, string own, Func<Aabb, string, bool> clear)
    {
        if (!r.Chance(0.7)) return;
        // The edge next to the top face: whichever end of v is higher.
        var upper = face.At(0.5f, 1).Y > face.At(0.5f, 0).Y ? 1f : 0f;
        var dv = MathF.Min(0.3f, 0.7f * k / face.Across);
        var v = upper > 0.5f ? 1 - dv * 0.9f : dv * 0.9f;
        var (v0, v1) = (v - dv / 2, v + dv / 2);
        var n = face.Normal;
        var along = Vector3.Normalize(face.At(1, v) - face.At(0, v));
        var across = Vector3.Normalize(Vector3.Cross(n, along));
        var u = r.Range(0.04f, 0.15f);
        while (u < 0.9f)
        {
            // A group of 3 to 6, then a gap.
            var group = r.Int(3, 6);
            var len = r.Range(0.5f, 1.1f) * k;
            var pitch = len + r.Range(0.25f, 0.6f) * k;
            var height = r.Range(0.25f, 0.5f) * k;
            var depth = dv * face.Across * 0.8f;
            var mat = r.Chance(0.7) ? Roles.DarkMechanics : Roles.HullSecondary;
            for (var i = 0; i < group && u < 0.94f; i++)
            {
                var du = len / face.Along;
                if (face.Free(u, u + du, v0, v1))
                {
                    var c = face.At(u + du / 2, v) + n * (height / 2);
                    var half = new Vector3(len / 2, height / 2, depth / 2);
                    var box = new Aabb(c - new Vector3(len, height, len), c + new Vector3(len, height, len));
                    if (clear(box, own))
                    {
                        Shapes.OBox(m, mat, c, along, n, across, half);
                        face.Use(u, u + du, v0, v1);
                    }
                }
                u += pitch / face.Along;
            }
            u += r.Range(3f, 9f) * k / face.Along;
        }
    }
}

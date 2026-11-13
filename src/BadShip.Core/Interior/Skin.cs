using System.Numerics;
using BadShip.Geometry;

namespace BadShip.Interior;

/// <summary>
/// Hull-shaped interiors. A deck inside a block can follow the block's own shape instead of being a box: its "inner
/// skin" is the block's cross-section moved inward by the wall's thickness, clipped to the deck's band between floor
/// and ceiling. Floors stay flat and walls between rooms stay straight (people and navigation meshes need both), but
/// the outer walls are the skin, the upper corners slant along the block's chamfers, and under a sloped or tapered end
/// the ceiling comes down with the block. Everything here works on sections across the ship (x, y) at a station z.
/// </summary>
public static class Skin
{
    /// <summary>The loft's section at station z, as a convex polygon across the ship (interpolated between rings).</summary>
    public static Vector2[] SectionAt(Loft loft, float z)
    {
        z = Math.Clamp(z, loft.Zs[0], loft.Zs[^1]);
        var s = 0;
        while (s + 1 < loft.Segments && loft.Zs[s + 1] < z) s++;
        var span = loft.Zs[s + 1] - loft.Zs[s];
        var t = span < 1e-4f ? 1 : (z - loft.Zs[s]) / span;
        return [.. loft.Rings[s].Zip(loft.Rings[s + 1], (a, b) => Vector3.Lerp(a, b, t)).Select(v => new Vector2(v.X, v.Y))];
    }

    /// <summary>
    /// A convex polygon moved inward by <paramref name="t"/>: every edge shifted in along its normal, corners where
    /// neighbouring edges meet. Zero-length edges (sharp corners) keep their place in the list, so polygons from
    /// different stations still match point for point.
    /// </summary>
    public static Vector2[] Inset(Vector2[] poly, float t)
    {
        var n = poly.Length;
        var area = 0f;
        for (var i = 0; i < n; i++) area += poly[i].X * poly[(i + 1) % n].Y - poly[(i + 1) % n].X * poly[i].Y;
        var ccw = area > 0;
        // Each real edge as a line (point, direction), shifted inward.
        var lines = new (Vector2 p, Vector2 d)?[n];
        for (var i = 0; i < n; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % n];
            var d = b - a;
            if (d.LengthSquared() < 1e-6f) continue;
            d = Vector2.Normalize(d);
            var inward = ccw ? new Vector2(-d.Y, d.X) : new Vector2(d.Y, -d.X);
            lines[i] = (a + inward * t, d);
        }
        var result = new Vector2[n];
        for (var i = 0; i < n; i++)
        {
            // The corner at vertex i: between the previous real edge and the next real edge.
            int prev = (i - 1 + n) % n, next = i;
            var guard = 0;
            while (lines[prev] is null && guard++ < n) prev = (prev - 1 + n) % n;
            guard = 0;
            while (lines[next] is null && guard++ < n) next = (next + 1) % n;
            if (lines[prev] is not { } lp || lines[next] is not { } ln) { result[i] = poly[i]; continue; }
            result[i] = Intersect(lp.p, lp.d, ln.p, ln.d) ?? ln.p;
        }
        return result;
    }

    private static Vector2? Intersect(Vector2 p, Vector2 d, Vector2 q, Vector2 e)
    {
        var den = d.X * e.Y - d.Y * e.X;
        if (MathF.Abs(den) < 1e-6f) return null;
        var w = q - p;
        var s = (w.X * e.Y - w.Y * e.X) / den;
        return p + d * s;
    }

    /// <summary>A convex polygon cut by a half-plane (keeps the side where <paramref name="keep"/> is ≥ 0).</summary>
    public static List<Vector2> Clip(IReadOnlyList<Vector2> poly, Func<Vector2, float> keep)
    {
        var output = new List<Vector2>();
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            float fa = keep(a), fb = keep(b);
            if (fa >= 0) output.Add(a);
            if (fa >= 0 != fb >= 0) output.Add(a + (b - a) * (fa / (fa - fb)));
        }
        return output;
    }

    /// <summary>The highest point of a convex polygon above x, or NaN when x is outside it.</summary>
    public static float TopAt(IReadOnlyList<Vector2> poly, float x)
    {
        var top = float.NaN;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            if (x < MathF.Min(a.X, b.X) - 1e-5f || x > MathF.Max(a.X, b.X) + 1e-5f) continue;
            var y = MathF.Abs(b.X - a.X) < 1e-6f ? MathF.Max(a.Y, b.Y) : a.Y + (b.Y - a.Y) * (x - a.X) / (b.X - a.X);
            if (float.IsNaN(top) || y > top) top = y;
        }
        return top;
    }

    /// <summary>Where a horizontal line at height y crosses a convex polygon: its left and right ends.</summary>
    public static (float a, float b)? SpanAt(IReadOnlyList<Vector2> poly, float y)
    {
        float lo = float.MaxValue, hi = float.MinValue;
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            if (y < MathF.Min(a.Y, b.Y) - 1e-5f || y > MathF.Max(a.Y, b.Y) + 1e-5f) continue;
            var x = MathF.Abs(b.Y - a.Y) < 1e-6f ? a.X : a.X + (b.X - a.X) * (y - a.Y) / (b.Y - a.Y);
            if (MathF.Abs(b.Y - a.Y) < 1e-6f) { lo = MathF.Min(lo, MathF.Min(a.X, b.X)); hi = MathF.Max(hi, MathF.Max(a.X, b.X)); continue; }
            lo = MathF.Min(lo, x);
            hi = MathF.Max(hi, x);
        }
        return lo < hi ? (lo, hi) : null;
    }

    /// <summary>Stations along [z0, z1]: both ends, every loft ring in between, and enough between those to keep
    /// steps under <paramref name="step"/>.</summary>
    public static List<float> Stations(Loft loft, float z0, float z1, float step = 0.75f, IEnumerable<float>? extra = null)
    {
        var marks = new SortedSet<float> { z0, z1 };
        if (extra != null) foreach (var z in extra) if (z > z0 + 0.01f && z < z1 - 0.01f) marks.Add(z);
        foreach (var z in loft.Zs) if (z > z0 + 0.01f && z < z1 - 0.01f) marks.Add(z);
        var list = marks.ToList();
        var outList = new List<float>();
        for (var i = 0; i + 1 < list.Count; i++)
        {
            var n = Math.Max(1, (int)MathF.Ceiling((list[i + 1] - list[i]) / step));
            for (var k = 0; k < n; k++) outList.Add(list[i] + (list[i + 1] - list[i]) * k / n);
        }
        outList.Add(z1);
        return outList;
    }

    /// <summary>
    /// The skin of one deck between stations z0 and z1: the inset loft surface clipped to the band from floor to
    /// ceiling (into <paramref name="walls"/>, or <paramref name="overhead"/> where it faces down: slanted ceiling
    /// corners and sloped ends), a flat ceiling where the block is taller than the deck, and a floor strip out to the
    /// skin beside the deck's own floor. Faces point into the deck.
    /// </summary>
    public static void Build(Mesh walls, Mesh overhead, string wallMat, string floorMat, Loft loft, float thickness,
        float z0, float z1, float floorY, float ceilY, IReadOnlyList<(int f, float z0, float z1, float v0, float v1)>? holes = null,
        IReadOnlyList<(float x0, float x1, float z0, float z1)>? ceilingHoles = null,
        IReadOnlyList<(float x0, float x1, float z0, float z1)>? floorHoles = null,
        float? clipTop = null, bool flatCeiling = true, bool floorUnderside = false)
    {
        // A deck with another deck right above it in a stack has no ceiling of its own: its skin reaches up to the next
        // floor (clipTop), whose underside is its ceiling. A deck with one below shows its floor's underside to it.
        var top = clipTop ?? ceilY;
        holes ??= [];
        ceilingHoles ??= [];
        floorHoles ??= [];
        var st = Stations(loft, z0, z1, 0.75f, holes.SelectMany(h => new[] { h.z0, h.z1 })
            .Concat(ceilingHoles.SelectMany(h => new[] { h.z0, h.z1 })).Concat(floorHoles.SelectMany(h => new[] { h.z0, h.z1 })));
        Vector2[] Sec(float z) => Inset(SectionAt(loft, z), thickness);
        for (var i = 0; i + 1 < st.Count; i++)
        {
            float za = st[i], zb = st[i + 1];
            var pa = Sec(za);
            var pb = Sec(zb);
            var centre = new Vector3((pa.Average(p => p.X) + pb.Average(p => p.X)) / 2, (floorY + ceilY) / 2, (za + zb) / 2);
            for (var k = 0; k < pa.Length; k++)
            {
                // The face between stations, in pieces round any window cut into it (v runs across the face).
                var cuts = holes.Where(h => h.f == k && h.z0 < zb - 1e-4f && h.z1 > za + 1e-4f).ToList();
                var vs = cuts.SelectMany(h => new[] { h.v0, h.v1 }).Append(0f).Append(1f).Select(v => Math.Clamp(v, 0, 1)).Distinct().OrderBy(v => v).ToList();
                var tris = new List<Vector3[]>();
                for (var j = 0; j + 1 < vs.Count; j++)
                {
                    float va = vs[j], vb = vs[j + 1];
                    if (vb - va < 1e-5f) continue;
                    var cv = (va + vb) / 2;
                    if (cuts.Any(h => cv > h.v0 && cv < h.v1)) continue;
                    Vector3 P(Vector2[] sec, float z, float v) => new(Vector2.Lerp(sec[k], sec[(k + 1) % sec.Length], v), z);
                    var q0 = P(pa, za, va); var q1 = P(pa, za, vb); var q2 = P(pb, zb, vb); var q3 = P(pb, zb, va);
                    tris.Add([q0, q1, q2]);
                    tris.Add([q0, q2, q3]);
                }
                foreach (var tri in tris)
                {
                    // A hair inside the band, so a face lying exactly on the floor or ceiling plane (a block's bottom
                    // face, inset, can land right on the floor) drops out: the floor strip and the flat ceiling, which
                    // have the openings for stairs and ladders, cover those.
                    var poly = ClipY(ClipY(tri.ToList(), floorY + 0.005f, true), top - 0.005f, false);
                    if (poly.Count < 3) continue;
                    var c = poly.Aggregate(Vector3.Zero, (s, v) => s + v) / poly.Count;
                    var inward = centre - c;
                    // Faces whose inward side looks down are overhead (hidden with the ceiling when looking in from above).
                    var n = Vector3.Cross(poly[1] - poly[0], poly[2] - poly[0]);
                    if (Vector3.Dot(n, inward) < 0) n = -n;
                    var target = n.LengthSquared() > 1e-10f && Vector3.Normalize(n).Y < -0.35f ? overhead : walls;
                    target.AddPolygon(wallMat, poly, inward);
                }
            }
            // The flat ceiling, where the block is taller than the deck.
            // Open where a ladder or stairs go up through it (stations line up with the openings' ends).
            if (flatCeiling && SpanAt(pa, ceilY) is { } ca && SpanAt(pb, ceilY) is { } cb)
            {
                var zm = (za + zb) / 2;
                var open = ceilingHoles.Where(h => zm > h.z0 && zm < h.z1).OrderBy(h => h.x0).ToList();
                var lo = MathF.Min(ca.a, cb.a);
                var pieces = new List<(float a, float b)>();
                var cursor = float.MinValue;
                foreach (var h in open)
                {
                    pieces.Add((cursor, h.x0));
                    cursor = h.x1;
                }
                pieces.Add((cursor, float.MaxValue));
                foreach (var (pa0, pb0) in pieces)
                {
                    float xa0 = MathF.Max(ca.a, pa0), xa1 = MathF.Min(ca.b, pb0), xb0 = MathF.Max(cb.a, pa0), xb1 = MathF.Min(cb.b, pb0);
                    if (xa1 - xa0 < 0.01f || xb1 - xb0 < 0.01f) continue;
                    overhead.AddQuad(wallMat, new Vector3(xa0, ceilY, za), new Vector3(xa1, ceilY, za), new Vector3(xb1, ceilY, zb), new Vector3(xb0, ceilY, zb), -Vector3.UnitY);
                }
                _ = lo;
            }
            // The floor out to the skin, a hair under the deck's own floor slab.
            var fy = floorY - 0.006f;
            // ...open, like the slab, wherever stairs or a ladder come up through it.
            if (SpanAt(pa, floorY) is { } fa && SpanAt(pb, floorY) is { } fb)
            {
                var zm = (za + zb) / 2;
                var cursor = float.MinValue;
                var pieces = new List<(float a, float b)>();
                foreach (var h in floorHoles.Where(h => zm > h.z0 && zm < h.z1).OrderBy(h => h.x0))
                {
                    pieces.Add((cursor, h.x0));
                    cursor = h.x1;
                }
                pieces.Add((cursor, float.MaxValue));
                foreach (var (p0, p1) in pieces)
                {
                    float xa0 = MathF.Max(fa.a, p0), xa1 = MathF.Min(fa.b, p1), xb0 = MathF.Max(fb.a, p0), xb1 = MathF.Min(fb.b, p1);
                    if (xa1 - xa0 < 0.01f || xb1 - xb0 < 0.01f) continue;
                    walls.AddQuad(floorMat, new Vector3(xa0, fy, za), new Vector3(xa1, fy, za), new Vector3(xb1, fy, zb), new Vector3(xb0, fy, zb), Vector3.UnitY);
                    if (floorUnderside)
                    {
                        var uy = floorY - Underside;
                        overhead.AddQuad(wallMat, new Vector3(xa0, uy, za), new Vector3(xa1, uy, za), new Vector3(xb1, uy, zb), new Vector3(xb0, uy, zb), -Vector3.UnitY);
                    }
                }
            }
        }
    }

    /// <summary>How far under a floor its underside (the ceiling of the deck below) sits: just under the slab's bottom.</summary>
    public const float Underside = 0.32f;

    /// <summary>A 3D polygon cut by a horizontal plane: keeps y ≥ h (above) or y ≤ h.</summary>
    private static List<Vector3> ClipY(List<Vector3> poly, float h, bool above)
    {
        var output = new List<Vector3>();
        for (var i = 0; i < poly.Count; i++)
        {
            var a = poly[i];
            var b = poly[(i + 1) % poly.Count];
            float fa = above ? a.Y - h : h - a.Y, fb = above ? b.Y - h : h - b.Y;
            if (fa >= 0) output.Add(a);
            if (fa >= 0 != fb >= 0) output.Add(a + (b - a) * (fa / (fa - fb)));
        }
        return output;
    }

    /// <summary>
    /// A wall across the ship at station z (from x0 to x1, y0 to y1), cut to the inset section: its upper corners
    /// follow the skin. Ends at ±∞ reach out to the skin. Extruded ±thickness/2 along z.
    /// </summary>
    public static void WallAcross(Mesh m, string mat, Loft loft, float inset, float z, float x0, float x1, float y0, float y1, float thickness,
        IReadOnlyList<(float x0, float x1, float y0, float y1)>? holes = null)
    {
        // With openings (windows in a front wall): the wall in rectangles round them, each cut to the section.
        if (holes is { Count: > 0 })
        {
            var ys = holes.SelectMany(h => new[] { h.y0, h.y1 }).Append(y0).Append(y1).Where(y => y >= y0 && y <= y1).Distinct().OrderBy(y => y).ToList();
            for (var i = 0; i + 1 < ys.Count; i++)
            {
                float ya = ys[i], yb = ys[i + 1];
                if (yb - ya < 1e-4f) continue;
                var ym = (ya + yb) / 2;
                var cursor = x0;
                foreach (var h in holes.Where(h => ym > h.y0 && ym < h.y1).OrderBy(h => h.x0))
                {
                    if (h.x0 > cursor) WallAcross(m, mat, loft, inset, z, cursor, h.x0, ya, yb, thickness);
                    cursor = MathF.Max(cursor, h.x1);
                }
                if (x1 > cursor) WallAcross(m, mat, loft, inset, z, cursor, x1, ya, yb, thickness);
            }
            return;
        }
        var sec = Inset(SectionAt(loft, z), inset);
        List<Vector2> poly = [new(MathF.Max(x0, -1e4f), y0), new(MathF.Min(x1, 1e4f), y0), new(MathF.Min(x1, 1e4f), y1), new(MathF.Max(x0, -1e4f), y1)];
        // Clip the rectangle by every edge of the (convex) section.
        var area = 0f;
        for (var i = 0; i < sec.Length; i++) area += sec[i].X * sec[(i + 1) % sec.Length].Y - sec[(i + 1) % sec.Length].X * sec[i].Y;
        for (var i = 0; i < sec.Length && poly.Count >= 3; i++)
        {
            var a = sec[i];
            var b = sec[(i + 1) % sec.Length];
            if (Vector2.DistanceSquared(a, b) < 1e-8f) continue;
            var sign = area > 0 ? 1 : -1;
            poly = Clip(poly, p => sign * ((b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X)));
        }
        if (poly.Count < 3) return;
        Extrude(m, mat, [.. poly.Select(p => new Vector3(p.X, p.Y, z - thickness / 2))], new Vector3(0, 0, thickness));
    }

    /// <summary>
    /// A wall along the ship at x (from z0 to z1, y0 to y1), its top cut down to the skin wherever the section is lower
    /// there (a sloped end). Extruded ±thickness/2 along x.
    /// </summary>
    public static void WallAlong(Mesh m, string mat, Loft loft, float inset, float x, float z0, float z1, float y0, float y1, float thickness)
    {
        var st = Stations(loft, z0, z1, 0.5f);
        float Top(float z)
        {
            var sec = Inset(SectionAt(loft, z), inset);
            var t1 = TopAt(sec, x - thickness / 2);
            var t2 = TopAt(sec, x + thickness / 2);
            var t = float.IsNaN(t1) ? t2 : float.IsNaN(t2) ? t1 : MathF.Min(t1, t2);
            return float.IsNaN(t) ? y0 : MathF.Min(y1, t);
        }
        for (var i = 0; i + 1 < st.Count; i++)
        {
            float za = st[i], zb = st[i + 1];
            float ta = Top(za), tb = Top(zb);
            if (ta <= y0 + 0.01f && tb <= y0 + 0.01f) continue;
            ta = MathF.Max(ta, y0); tb = MathF.Max(tb, y0);
            Extrude(m, mat, [new(x - thickness / 2, y0, za), new(x - thickness / 2, y0, zb), new(x - thickness / 2, tb, zb), new(x - thickness / 2, ta, za)], new Vector3(thickness, 0, 0));
        }
    }

    /// <summary>A flat convex polygon pushed along <paramref name="depth"/> into a closed slab.</summary>
    private static void Extrude(Mesh m, string mat, List<Vector3> face, Vector3 depth)
    {
        var centre = face.Aggregate(Vector3.Zero, (s, v) => s + v) / face.Count + depth / 2;
        m.AddPolygon(mat, face, -depth);
        m.AddPolygon(mat, [.. face.Select(p => p + depth)], depth);
        for (var i = 0; i < face.Count; i++)
        {
            var a = face[i];
            var b = face[(i + 1) % face.Count];
            var mid = (a + b) / 2 + depth / 2;
            m.AddQuad(mat, a, b, b + depth, a + depth, mid - centre);
        }
    }
}

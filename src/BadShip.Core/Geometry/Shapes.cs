using System.Numerics;

namespace BadShip.Geometry;

/// <summary>Primitive builders: boxes, beams, prisms (cylinders, frusta), geodesic domes and surface slabs.</summary>
public static class Shapes
{
    public static void Box(Mesh m, string mat, Vector3 center, Vector3 size) =>
        OBox(m, mat, center, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ, size * 0.5f);

    public static void BoxMinMax(Mesh m, string mat, Vector3 min, Vector3 max) => Box(m, mat, (min + max) * 0.5f, max - min);

    /// <summary>An oriented box with unit axes <paramref name="ax"/>, <paramref name="ay"/>, <paramref name="az"/> and half-sizes.</summary>
    public static void OBox(Mesh m, string mat, Vector3 c, Vector3 ax, Vector3 ay, Vector3 az, Vector3 half)
    {
        Vector3 P(int sx, int sy, int sz) => c + ax * (half.X * sx) + ay * (half.Y * sy) + az * (half.Z * sz);
        m.AddPolygon(mat, [P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1)], ax);
        m.AddPolygon(mat, [P(-1, -1, -1), P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1)], -ax);
        m.AddPolygon(mat, [P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1)], ay);
        m.AddPolygon(mat, [P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), P(-1, -1, 1)], -ay);
        m.AddPolygon(mat, [P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1)], az);
        m.AddPolygon(mat, [P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1)], -az);
    }

    /// <summary>A square beam (girder, strut, mast leg) from a to b.</summary>
    public static void Beam(Mesh m, string mat, Vector3 a, Vector3 b, float thickness) =>
        Prism(m, mat, a, b, thickness * 0.7071f, thickness * 0.7071f, 4, MathF.PI / 4);

    /// <summary>
    /// An n-sided prism or frustum from a (radius ra) to b (radius rb). With 8 sides and rotation π/8 the flats face
    /// up, down and sideways: the chunky octagonal look of the engine pods and tanks.
    /// </summary>
    public static void Prism(Mesh m, string mat, Vector3 a, Vector3 b, float ra, float rb, int sides, float rotation = 0,
        bool capA = true, bool capB = true, string? capAMat = null, string? capBMat = null)
    {
        var axis = b - a;
        var len = axis.Length();
        if (len < 1e-5f || sides < 3) return;
        axis /= len;
        var reference = MathF.Abs(axis.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX;
        var u = Vector3.Normalize(Vector3.Cross(reference, axis));
        var v = Vector3.Cross(axis, u);
        var ringA = new Vector3[sides];
        var ringB = new Vector3[sides];
        for (var i = 0; i < sides; i++)
        {
            var ang = rotation + i * MathF.Tau / sides;
            var dir = u * MathF.Cos(ang) + v * MathF.Sin(ang);
            ringA[i] = a + dir * ra;
            ringB[i] = b + dir * rb;
        }
        for (var i = 0; i < sides; i++)
        {
            var j = (i + 1) % sides;
            var mid = rotation + (i + 0.5f) * MathF.Tau / sides;
            var hint = u * MathF.Cos(mid) + v * MathF.Sin(mid);
            m.AddQuad(mat, ringA[i], ringA[j], ringB[j], ringB[i], hint);
        }
        if (capA && ra > 1e-5f) m.AddPolygon(capAMat ?? mat, ringA, -axis);
        if (capB && rb > 1e-5f) m.AddPolygon(capBMat ?? mat, ringB, axis);
    }

    /// <summary>
    /// A flat-shaded geodesic sphere (radomes). Only triangles whose centre is above <paramref name="minY"/> (in unit-sphere
    /// terms, −1..1) are kept, so a dome can sit on a base without hidden faces below it.
    /// </summary>
    public static void Geodesic(Mesh m, string mat, Vector3 center, float radius, int subdivisions, float minY = -1f)
    {
        var t = (1f + MathF.Sqrt(5f)) / 2f;
        var verts = new List<Vector3>
        {
            new(-1, t, 0), new(1, t, 0), new(-1, -t, 0), new(1, -t, 0),
            new(0, -1, t), new(0, 1, t), new(0, -1, -t), new(0, 1, -t),
            new(t, 0, -1), new(t, 0, 1), new(-t, 0, -1), new(-t, 0, 1),
        };
        for (var i = 0; i < verts.Count; i++) verts[i] = Vector3.Normalize(verts[i]);
        var faces = new List<(int, int, int)>
        {
            (0, 11, 5), (0, 5, 1), (0, 1, 7), (0, 7, 10), (0, 10, 11), (1, 5, 9), (5, 11, 4), (11, 10, 2), (10, 7, 6), (7, 1, 8),
            (3, 9, 4), (3, 4, 2), (3, 2, 6), (3, 6, 8), (3, 8, 9), (4, 9, 5), (2, 4, 11), (6, 2, 10), (8, 6, 7), (9, 8, 1),
        };
        for (var s = 0; s < subdivisions; s++)
        {
            var cache = new Dictionary<(int, int), int>();
            int Mid(int a, int b)
            {
                var key = a < b ? (a, b) : (b, a);
                if (cache.TryGetValue(key, out var idx)) return idx;
                verts.Add(Vector3.Normalize((verts[a] + verts[b]) * 0.5f));
                return cache[key] = verts.Count - 1;
            }
            var next = new List<(int, int, int)>(faces.Count * 4);
            foreach (var (a, b, c) in faces)
            {
                var ab = Mid(a, b);
                var bc = Mid(b, c);
                var ca = Mid(c, a);
                next.Add((a, ab, ca));
                next.Add((b, bc, ab));
                next.Add((c, ca, bc));
                next.Add((ab, bc, ca));
            }
            faces = next;
        }
        foreach (var (a, b, c) in faces)
        {
            var centroid = (verts[a] + verts[b] + verts[c]) / 3f;
            if (centroid.Y < minY) continue;
            m.AddPolygon(mat, [center + verts[a] * radius, center + verts[b] * radius, center + verts[c] * radius], centroid);
        }
    }

    /// <summary>
    /// A thin raised slab on a surface: the quad a-b-c-d pushed out along <paramref name="n"/> by <paramref name="depth"/>,
    /// sunk slightly into the surface so it never floats. Windows, armour plates, hatches and stripes are slabs.
    /// </summary>
    public static void Slab(Mesh m, string mat, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 n, float depth, float sink = 0.04f)
    {
        var lo = -n * sink;
        var hi = n * depth;
        Vector3[] baseQ = [a + lo, b + lo, c + lo, d + lo];
        Vector3[] top = [a + hi, b + hi, c + hi, d + hi];
        m.AddPolygon(mat, top, n);
        var center = (a + b + c + d) * 0.25f;
        for (var i = 0; i < 4; i++)
        {
            var j = (i + 1) % 4;
            m.AddPolygon(mat, [baseQ[i], baseQ[j], top[j], top[i]], (baseQ[i] + baseQ[j]) * 0.5f - (center + lo));
        }
    }

    /// <summary>A slab on face <paramref name="f"/> of loft segment <paramref name="s"/>, over the given (u, v) rectangle.</summary>
    public static bool Patch(Mesh m, string mat, Loft loft, int s, int f, float u0, float u1, float v0, float v1, float depth)
    {
        var n = loft.FaceNormal(s, f);
        if (n == Vector3.Zero) return false;
        Slab(m, mat, loft.FacePoint(s, f, u0, v0), loft.FacePoint(s, f, u0, v1), loft.FacePoint(s, f, u1, v1), loft.FacePoint(s, f, u1, v0), n, depth);
        return true;
    }

    /// <summary>
    /// A ring around an axis parallel to Z through (cx, cy): a chamfered rectangular cross-section (radially from
    /// <paramref name="rIn"/> to <paramref name="rOut"/>, along Z from z0 to z1) swept round in flat segments. Every
    /// <paramref name="altEvery"/>-th segment uses <paramref name="altMat"/>, giving the panel bands of a hard-surface ring.
    /// </summary>
    public static void Ring(Mesh m, string mat, float cx, float cy, float rIn, float rOut, float z0, float z1, int segments,
        float chamfer, string? altMat = null, int altEvery = 0, float rotation = 0)
    {
        var c = MathF.Min(chamfer, MathF.Min(rOut - rIn, z1 - z0) * 0.45f);
        (float r, float z)[] sec =
        [
            (rIn, z0 + c), (rIn + c, z0), (rOut - c, z0), (rOut, z0 + c),
            (rOut, z1 - c), (rOut - c, z1), (rIn + c, z1), (rIn, z1 - c),
        ];
        var rm = (rIn + rOut) / 2;
        var zm = (z0 + z1) / 2;
        Vector3 P(float r, float z, float a) => new(cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r, z);
        for (var i = 0; i < segments; i++)
        {
            var a0 = rotation + i * MathF.Tau / segments;
            var a1 = rotation + (i + 1) * MathF.Tau / segments;
            var am = (a0 + a1) / 2;
            var segMat = altMat != null && altEvery > 0 && i % altEvery == 0 ? altMat : mat;
            for (var j = 0; j < sec.Length; j++)
            {
                var (ra, za) = sec[j];
                var (rb, zb) = sec[(j + 1) % sec.Length];
                // Outward in the (r, z) plane, then turned to the segment's angle.
                var nr = (ra + rb) / 2 - rm;
                var nz = (za + zb) / 2 - zm;
                var hint = new Vector3(MathF.Cos(am) * nr, MathF.Sin(am) * nr, nz);
                m.AddQuad(segMat, P(ra, za, a0), P(rb, zb, a0), P(rb, zb, a1), P(ra, za, a1), hint);
            }
        }
    }
}

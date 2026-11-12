using System.Numerics;

namespace BadShip.Geometry;

/// <summary>
/// A flat-shaded triangle mesh split into groups by material role. Every polygon gets its own vertices and face normal
/// (hard-surface look, no smoothing across edges). UVs are box-projected in world space at <see cref="UvScale"/>
/// repeats per metre, so tiling materials keep a consistent texel density on every part.
/// </summary>
public sealed class Mesh
{
    /// <summary>UV repeats per metre (one tile every four metres).</summary>
    public const float UvScale = 0.25f;

    public List<Vector3> Positions { get; } = [];
    public List<Vector3> Normals { get; } = [];
    public List<Vector2> Uvs { get; } = [];

    /// <summary>Triangle indices per material role, ordered by role name so output is deterministic.</summary>
    public SortedDictionary<string, List<int>> Groups { get; } = new(StringComparer.Ordinal);

    public bool IsEmpty => Positions.Count == 0;

    public int TriangleCount
    {
        get
        {
            var n = 0;
            foreach (var g in Groups.Values) n += g.Count;
            return n / 3;
        }
    }

    public Aabb Bounds()
    {
        var b = Aabb.Empty;
        foreach (var p in Positions) b = b.Include(p);
        return b;
    }

    /// <summary>
    /// Adds a convex planar polygon. Duplicate and collinear points are dropped; a polygon that collapses to nothing is
    /// skipped. When <paramref name="outward"/> is given, the winding is flipped if needed so the face points that way.
    /// </summary>
    public void AddPolygon(string material, IReadOnlyList<Vector3> input, Vector3? outward = null)
    {
        var pts = Clean(input);
        if (pts.Count < 3) return;
        var n = Newell(pts);
        var len = n.Length();
        if (len < 1e-7f) return;
        n /= len;
        if (outward is { } o && Vector3.Dot(n, o) < 0)
        {
            pts.Reverse();
            n = -n;
        }
        var start = Positions.Count;
        foreach (var p in pts)
        {
            Positions.Add(p);
            Normals.Add(n);
            Uvs.Add(BoxUv(p, n));
        }
        if (!Groups.TryGetValue(material, out var idx)) Groups[material] = idx = [];
        for (var i = 1; i + 1 < pts.Count; i++)
        {
            idx.Add(start);
            idx.Add(start + i);
            idx.Add(start + i + 1);
        }
    }

    /// <summary>Adds a quad (a, b, c, d in order around it); a noticeably non-planar quad becomes two triangles.</summary>
    public void AddQuad(string material, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3? outward = null)
    {
        var n = Vector3.Cross(b - a, c - a);
        var nl = n.Length();
        var size = MathF.Max(Vector3.Distance(a, c), Vector3.Distance(b, d));
        if (nl > 1e-7f && MathF.Abs(Vector3.Dot(d - a, n / nl)) > 0.002f * MathF.Max(size, 1f))
        {
            AddPolygon(material, [a, b, c], outward);
            AddPolygon(material, [a, c, d], outward);
            return;
        }
        AddPolygon(material, [a, b, c, d], outward);
    }

    /// <summary>Appends another mesh, optionally offset.</summary>
    public void Append(Mesh other, Vector3 offset = default)
    {
        var start = Positions.Count;
        foreach (var p in other.Positions) Positions.Add(p + offset);
        Normals.AddRange(other.Normals);
        Uvs.AddRange(other.Uvs);
        foreach (var (mat, src) in other.Groups)
        {
            if (!Groups.TryGetValue(mat, out var idx)) Groups[mat] = idx = [];
            foreach (var i in src) idx.Add(i + start);
        }
    }

    private static List<Vector3> Clean(IReadOnlyList<Vector3> input)
    {
        var pts = new List<Vector3>(input.Count);
        foreach (var p in input)
            if (pts.Count == 0 || Vector3.DistanceSquared(pts[^1], p) > 1e-8f) pts.Add(p);
        while (pts.Count > 1 && Vector3.DistanceSquared(pts[0], pts[^1]) <= 1e-8f) pts.RemoveAt(pts.Count - 1);
        // Drop points that lie on the line between their neighbours (zero-area fan triangles otherwise).
        var changed = true;
        while (changed && pts.Count >= 3)
        {
            changed = false;
            for (var i = 0; i < pts.Count && pts.Count >= 3; i++)
            {
                var prev = pts[(i + pts.Count - 1) % pts.Count];
                var next = pts[(i + 1) % pts.Count];
                var e1 = pts[i] - prev;
                var e2 = next - pts[i];
                var cross = Vector3.Cross(e1, e2).Length();
                if (cross <= 1e-6f * MathF.Max(e1.Length() * e2.Length(), 1e-6f))
                {
                    pts.RemoveAt(i);
                    changed = true;
                    break;
                }
            }
        }
        return pts;
    }

    private static Vector3 Newell(List<Vector3> p)
    {
        var n = Vector3.Zero;
        for (var i = 0; i < p.Count; i++)
        {
            var a = p[i];
            var b = p[(i + 1) % p.Count];
            n.X += (a.Y - b.Y) * (a.Z + b.Z);
            n.Y += (a.Z - b.Z) * (a.X + b.X);
            n.Z += (a.X - b.X) * (a.Y + b.Y);
        }
        return n;
    }

    private static Vector2 BoxUv(Vector3 p, Vector3 n)
    {
        var a = Vector3.Abs(n);
        if (a.X >= a.Y && a.X >= a.Z) return new Vector2(p.Z * UvScale, p.Y * UvScale);
        if (a.Y >= a.Z) return new Vector2(p.X * UvScale, p.Z * UvScale);
        return new Vector2(p.X * UvScale, p.Y * UvScale);
    }
}

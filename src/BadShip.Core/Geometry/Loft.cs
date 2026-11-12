using System.Numerics;

namespace BadShip.Geometry;

/// <summary>
/// A hull cross-section: a rectangle, optionally narrower at the bottom than the top (a trapezoid), with its top and
/// bottom corners chamfered. Lofting a few of these along Z makes every blocky hull form the generator needs:
/// boxes, chamfered boxes, trapezoidal prisms, wedges, sloped prows and stepped decks.
/// </summary>
/// <remarks>
/// Points go round in a fixed order, so the 8 faces of a loft segment are always the same faces:
/// 0 bottom, 1 bottom +X chamfer, 2 +X side, 3 top +X chamfer, 4 top, 5 top −X chamfer, 6 −X side, 7 bottom −X chamfer.
/// </remarks>
public readonly record struct Profile(float Cx, float Cy, float WBottom, float WTop, float H, float ChamferTop, float ChamferBottom)
{
    public const int FaceBottom = 0, FaceBottomPosX = 1, FacePosX = 2, FaceTopPosX = 3, FaceTop = 4, FaceTopNegX = 5, FaceNegX = 6, FaceBottomNegX = 7;

    public static Profile Box(float cx, float cy, float w, float h, float chamfer = 0) => new(cx, cy, w, w, h, chamfer, chamfer);

    private readonly float _topRun;

    /// <summary>
    /// How far the top chamfer runs inwards per unit it drops (1 is 45°). Values of 2–3 give the wide, shallow top
    /// facets and sharp shoulder crease of the Thalassa reference.
    /// </summary>
    public float TopRun { get => _topRun <= 0 ? 1f : _topRun; init => _topRun = value; }

    public float Top => Cy + H / 2;
    public float Bottom => Cy - H / 2;
    public float Area => (WBottom + WTop) * 0.5f * H;

    /// <summary>The flat part of the top face, half its width.</summary>
    public float TopFlatHalf => MathF.Max(0, WTop / 2 - ClampedChamfers().ct * TopRun);

    public (float ct, float cb) ClampedChamfers()
    {
        var h = MathF.Max(H, 0);
        var ct = Math.Clamp(ChamferTop, 0, MathF.Min(WTop * 0.49f / TopRun, h * 0.6f));
        var cb = Math.Clamp(ChamferBottom, 0, MathF.Min(WBottom * 0.49f, h * 0.6f));
        if (ct + cb > h * 0.96f)
        {
            var s = h * 0.96f / MathF.Max(ct + cb, 1e-6f);
            ct *= s;
            cb *= s;
        }
        return (ct, cb);
    }

    public Vector2[] Points()
    {
        var h = MathF.Max(H, 0);
        var (ct, cb) = ClampedChamfers();
        var y0 = Cy - h / 2;
        var y1 = Cy + h / 2;
        var hb = MathF.Max(WBottom, 0) / 2;
        var ht = MathF.Max(WTop, 0) / 2;
        float SideX(float y) => h <= 1e-6f ? ht : hb + (ht - hb) * (y - y0) / h;
        return
        [
            new(Cx - hb + cb, y0),
            new(Cx + hb - cb, y0),
            new(Cx + SideX(y0 + cb), y0 + cb),
            new(Cx + SideX(y1 - ct), y1 - ct),
            new(Cx + ht - ct * TopRun, y1),
            new(Cx - ht + ct * TopRun, y1),
            new(Cx - SideX(y1 - ct), y1 - ct),
            new(Cx - SideX(y0 + cb), y0 + cb),
        ];
    }

    /// <summary>Half the width of the outline at height <paramref name="y"/> (chamfers and taper included), measured from Cx.</summary>
    public float HalfWidthAt(float y)
    {
        var pts = Points();
        // The +X side runs p1 (bottom) → p2 → p3 → p4 (top).
        if (y <= pts[1].Y) return pts[1].X - Cx;
        if (y >= pts[4].Y) return pts[4].X - Cx;
        for (var i = 1; i < 4; i++)
        {
            var a = pts[i];
            var b = pts[i + 1];
            if (y < a.Y || y > b.Y) continue;
            var t = b.Y - a.Y < 1e-6f ? 1 : (y - a.Y) / (b.Y - a.Y);
            return float.Lerp(a.X, b.X, t) - Cx;
        }
        return pts[3].X - Cx;
    }

    /// <summary>Scales width and height about the centre.</summary>
    public Profile Scaled(float sw, float sh) => this with
    {
        WBottom = WBottom * sw, WTop = WTop * sw, H = H * sh,
        ChamferTop = ChamferTop * MathF.Min(sw, sh), ChamferBottom = ChamferBottom * MathF.Min(sw, sh),
    };

    /// <summary>The flat part of the bottom face, half its width.</summary>
    public float BottomFlatHalf => MathF.Max(0, WBottom / 2 - ClampedChamfers().cb);

    /// <summary>Scales width and height keeping the top where it is (a block that hangs below a hull).</summary>
    public Profile ScaledOnTop(float sw, float sh)
    {
        var s = Scaled(sw, sh);
        return s with { Cy = Top - s.H / 2 };
    }

    /// <summary>Scales width and height keeping the bottom where it is (a block that sits on a deck).</summary>
    public Profile ScaledOnBase(float sw, float sh)
    {
        var s = Scaled(sw, sh);
        return s with { Cy = Bottom + s.H / 2 };
    }
}

/// <summary>Cross-sections placed along +Z (ascending) and skinned into a closed, flat-shaded solid.</summary>
public sealed class Loft
{
    public List<float> Zs { get; } = [];
    public List<Profile> Profiles { get; } = [];
    public List<Vector3[]> Rings { get; } = [];

    public Loft(IEnumerable<(float z, Profile p)> rings)
    {
        foreach (var (z, p) in rings)
        {
            if (Zs.Count > 0 && z < Zs[^1] - 1e-4f) throw new ArgumentException("Loft rings must go along +Z.");
            Zs.Add(z);
            Profiles.Add(p);
            Rings.Add(p.Points().Select(q => new Vector3(q.X, q.Y, z)).ToArray());
        }
        if (Rings.Count < 2) throw new ArgumentException("A loft needs at least two rings.");
    }

    public int Segments => Rings.Count - 1;
    public float ZMin => Zs[0];
    public float ZMax => Zs[^1];

    public void Build(Mesh m, string material, bool capStart = true, bool capEnd = true)
    {
        for (var s = 0; s < Segments; s++)
            for (var f = 0; f < 8; f++)
            {
                var q = Face(s, f);
                m.AddQuad(material, q[0], q[1], q[2], q[3], OutwardHint(s, f));
            }
        if (capStart) m.AddPolygon(material, Rings[0], -Vector3.UnitZ);
        if (capEnd) m.AddPolygon(material, Rings[^1], Vector3.UnitZ);
    }

    /// <summary>The quad of face <paramref name="f"/> in segment <paramref name="s"/>: a, b on the rear ring, c, d on the front.</summary>
    public Vector3[] Face(int s, int f)
    {
        var a = Rings[s];
        var b = Rings[s + 1];
        return [a[f], a[(f + 1) % 8], b[(f + 1) % 8], b[f]];
    }

    public bool IsStep(int s) => MathF.Abs(Zs[s + 1] - Zs[s]) < 1e-4f;

    /// <summary>A point on a face: u runs rear→front (0..1), v runs along the edge from point f to point f+1.</summary>
    public Vector3 FacePoint(int s, int f, float u, float v)
    {
        var q = Face(s, f);
        var rear = Vector3.Lerp(q[0], q[1], v);
        var front = Vector3.Lerp(q[3], q[2], v);
        return Vector3.Lerp(rear, front, u);
    }

    /// <summary>The face's outward unit normal, or zero for a face that has collapsed.</summary>
    public Vector3 FaceNormal(int s, int f)
    {
        var q = Face(s, f);
        var n = Vector3.Cross(q[2] - q[0], q[3] - q[1]);
        var len = n.Length();
        if (len < 1e-6f) return Vector3.Zero;
        n /= len;
        return Vector3.Dot(n, OutwardHint(s, f)) < 0 ? -n : n;
    }

    /// <summary>Face length along the loft (u) and across it (v), in metres, measured at the face's middle.</summary>
    public (float along, float across) FaceSize(int s, int f)
    {
        var along = Vector3.Distance(FacePoint(s, f, 0, 0.5f), FacePoint(s, f, 1, 0.5f));
        var across = Vector3.Distance(FacePoint(s, f, 0.5f, 0), FacePoint(s, f, 0.5f, 1));
        return (along, across);
    }

    public Vector3 OutwardHint(int s, int f)
    {
        if (IsStep(s)) return Profiles[s + 1].Area < Profiles[s].Area ? Vector3.UnitZ : -Vector3.UnitZ;
        var q = Face(s, f);
        var c = (q[0] + q[1] + q[2] + q[3]) * 0.25f;
        var t = Math.Clamp((c.Z - Zs[s]) / (Zs[s + 1] - Zs[s]), 0, 1);
        var axis = new Vector3(
            float.Lerp(Profiles[s].Cx, Profiles[s + 1].Cx, t),
            float.Lerp(Profiles[s].Cy, Profiles[s + 1].Cy, t),
            c.Z);
        var h = c - axis;
        return h.LengthSquared() < 1e-10f ? Vector3.UnitY : h;
    }
}

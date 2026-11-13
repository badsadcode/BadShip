using System.Numerics;

namespace BadShip.Geometry;

/// <summary>An axis-aligned box: the coarse volume used for placement, attachment and clearance tests.</summary>
public readonly record struct Aabb(Vector3 Min, Vector3 Max)
{
    public static readonly Aabb Empty = new(new Vector3(float.MaxValue), new Vector3(float.MinValue));

    public bool IsEmpty => Min.X > Max.X || Min.Y > Max.Y || Min.Z > Max.Z;
    public Vector3 Center => (Min + Max) * 0.5f;
    public Vector3 Size => IsEmpty ? Vector3.Zero : Max - Min;

    public static Aabb FromCenterSize(Vector3 center, Vector3 size) => new(center - size * 0.5f, center + size * 0.5f);

    public static Aabb FromPoints(Vector3 a, Vector3 b) => new(Vector3.Min(a, b), Vector3.Max(a, b));

    public Aabb Include(Vector3 p) => new(Vector3.Min(Min, p), Vector3.Max(Max, p));

    public Aabb Union(Aabb o) => IsEmpty ? o : o.IsEmpty ? this : new(Vector3.Min(Min, o.Min), Vector3.Max(Max, o.Max));

    public Aabb Expand(float e) => IsEmpty ? this : new(Min - new Vector3(e), Max + new Vector3(e));

    /// <summary>True when the boxes touch or overlap, allowing a gap of up to <paramref name="tolerance"/>.</summary>
    public bool Touches(Aabb o, float tolerance = 0.05f) =>
        !IsEmpty && !o.IsEmpty &&
        Min.X - tolerance <= o.Max.X && Max.X + tolerance >= o.Min.X &&
        Min.Y - tolerance <= o.Max.Y && Max.Y + tolerance >= o.Min.Y &&
        Min.Z - tolerance <= o.Max.Z && Max.Z + tolerance >= o.Min.Z;

    /// <summary>The smallest of the three overlap depths (negative when apart): how deeply the boxes interpenetrate.</summary>
    public float Penetration(Aabb o)
    {
        if (IsEmpty || o.IsEmpty) return float.NegativeInfinity;
        var dx = MathF.Min(Max.X, o.Max.X) - MathF.Max(Min.X, o.Min.X);
        var dy = MathF.Min(Max.Y, o.Max.Y) - MathF.Max(Min.Y, o.Min.Y);
        var dz = MathF.Min(Max.Z, o.Max.Z) - MathF.Max(Min.Z, o.Min.Z);
        return MathF.Min(dx, MathF.Min(dy, dz));
    }

    public float[] MinArray() => [Min.X, Min.Y, Min.Z];
    public float[] MaxArray() => [Max.X, Max.Y, Max.Z];
}

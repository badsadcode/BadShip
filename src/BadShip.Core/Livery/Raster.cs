using System.IO.Compression;

namespace BadShip.Livery;

/// <summary>
/// An RGBA picture in memory with just enough drawing for decal sheets: filled rectangles and convex or concave
/// polygons, antialiased by 4×4 supersampling, and PNG output. No dependencies beyond the base library.
/// </summary>
public sealed class Raster
{
    public int Width { get; }
    public int Height { get; }
    public byte[] Rgba { get; }

    public Raster(int width, int height)
    {
        Width = width;
        Height = height;
        Rgba = new byte[width * height * 4];
    }

    /// <summary>A colour as 0..1 RGBA.</summary>
    public readonly record struct Color(float R, float G, float B, float A = 1)
    {
        public static Color Hex(string hex, float a = 1)
        {
            var v = Convert.ToInt32(hex.TrimStart('#'), 16);
            return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, a);
        }
    }

    /// <summary>Blends <paramref name="c"/> into a pixel with coverage <paramref name="cover"/> (0..1).</summary>
    private void Blend(int x, int y, Color c, float cover)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height || cover <= 0) return;
        var i = (y * Width + x) * 4;
        var a = c.A * Math.Min(cover, 1);
        var da = Rgba[i + 3] / 255f;
        var oa = a + da * (1 - a);
        if (oa <= 0) return;
        float Mix(float s, byte d) => (s * a + d / 255f * da * (1 - a)) / oa;
        Rgba[i] = (byte)Math.Round(Mix(c.R, Rgba[i]) * 255);
        Rgba[i + 1] = (byte)Math.Round(Mix(c.G, Rgba[i + 1]) * 255);
        Rgba[i + 2] = (byte)Math.Round(Mix(c.B, Rgba[i + 2]) * 255);
        Rgba[i + 3] = (byte)Math.Round(oa * 255);
    }

    /// <summary>An axis-aligned rectangle with exact (area) antialiasing: the stencil letters are made of these.</summary>
    public void FillRect(float x0, float y0, float x1, float y1, Color c)
    {
        if (x1 < x0) (x0, x1) = (x1, x0);
        if (y1 < y0) (y0, y1) = (y1, y0);
        var px0 = Math.Max(0, (int)MathF.Floor(x0));
        var px1 = Math.Min(Width - 1, (int)MathF.Ceiling(x1) - 1);
        var py0 = Math.Max(0, (int)MathF.Floor(y0));
        var py1 = Math.Min(Height - 1, (int)MathF.Ceiling(y1) - 1);
        for (var y = py0; y <= py1; y++)
        {
            var cy = MathF.Min(y + 1, y1) - MathF.Max(y, y0);
            for (var x = px0; x <= px1; x++)
            {
                var cx = MathF.Min(x + 1, x1) - MathF.Max(x, x0);
                Blend(x, y, c, cx * cy);
            }
        }
    }

    /// <summary>Fills a polygon (even-odd rule) with 16 samples per pixel.</summary>
    public void FillPolygon(IReadOnlyList<(float x, float y)> pts, Color c)
    {
        if (pts.Count < 3) return;
        var minY = Math.Max(0, (int)Math.Floor(pts.Min(p => p.y)));
        var maxY = Math.Min(Height - 1, (int)Math.Ceiling(pts.Max(p => p.y)));
        var minX = Math.Max(0, (int)Math.Floor(pts.Min(p => p.x)));
        var maxX = Math.Min(Width - 1, (int)Math.Ceiling(pts.Max(p => p.x)));
        if (minX > maxX || minY > maxY) return;
        const int S = 4;
        var cover = new float[maxX - minX + 1];
        var xs = new List<float>();
        for (var y = minY; y <= maxY; y++)
        {
            Array.Clear(cover);
            for (var sy = 0; sy < S; sy++)
            {
                var yy = y + (sy + 0.5f) / S;
                xs.Clear();
                for (var i = 0; i < pts.Count; i++)
                {
                    var (ax, ay) = pts[i];
                    var (bx, by) = pts[(i + 1) % pts.Count];
                    if ((ay <= yy && by > yy) || (by <= yy && ay > yy))
                        xs.Add(ax + (yy - ay) / (by - ay) * (bx - ax));
                }
                xs.Sort();
                for (var k = 0; k + 1 < xs.Count; k += 2)
                {
                    // Only the pixels the span touches.
                    var a = Math.Max(minX, (int)MathF.Floor(xs[k]));
                    var b = Math.Min(maxX, (int)MathF.Ceiling(xs[k + 1]));
                    for (var x = a; x <= b; x++)
                        for (var sx = 0; sx < S; sx++)
                        {
                            var xx = x + (sx + 0.5f) / S;
                            if (xx >= xs[k] && xx < xs[k + 1]) cover[x - minX] += 1f / (S * S);
                        }
                }
            }
            for (var x = minX; x <= maxX; x++) Blend(x, y, c, cover[x - minX]);
        }
    }

    public void FillCircle(float cx, float cy, float r, Color c, int segments = 48)
    {
        var pts = new List<(float, float)>();
        for (var i = 0; i < segments; i++)
        {
            var a = i * MathF.Tau / segments;
            pts.Add((cx + MathF.Cos(a) * r, cy + MathF.Sin(a) * r));
        }
        FillPolygon(pts, c);
    }

    /// <summary>A ring (annulus) as one even-odd polygon: outer circle plus inner circle.</summary>
    public void FillRing(float cx, float cy, float r0, float r1, Color c, int segments = 64)
    {
        // Two concentric circles joined by a seam give an even-odd ring.
        var pts = new List<(float, float)>();
        for (var i = 0; i <= segments; i++) { var a = i * MathF.Tau / segments; pts.Add((cx + MathF.Cos(a) * r1, cy + MathF.Sin(a) * r1)); }
        for (var i = segments; i >= 0; i--) { var a = i * MathF.Tau / segments; pts.Add((cx + MathF.Cos(a) * r0, cy + MathF.Sin(a) * r0)); }
        FillPolygon(pts, c);
    }

    // ------------------------------------------------------------------ PNG

    public byte[] Png()
    {
        using var ms = new MemoryStream();
        ms.Write([137, 80, 78, 71, 13, 10, 26, 10]);
        var ihdr = new byte[13];
        WriteBE(ihdr, 0, (uint)Width);
        WriteBE(ihdr, 4, (uint)Height);
        ihdr[8] = 8; // bit depth
        ihdr[9] = 6; // RGBA
        Chunk(ms, "IHDR", ihdr);
        var raw = new byte[(Width * 4 + 1) * Height];
        for (var y = 0; y < Height; y++)
        {
            raw[y * (Width * 4 + 1)] = 0; // no filter
            Buffer.BlockCopy(Rgba, y * Width * 4, raw, y * (Width * 4 + 1) + 1, Width * 4);
        }
        using (var z = new MemoryStream())
        {
            using (var zl = new ZLibStream(z, CompressionLevel.Fastest, leaveOpen: true)) zl.Write(raw);
            Chunk(ms, "IDAT", z.ToArray());
        }
        Chunk(ms, "IEND", []);
        return ms.ToArray();
    }

    private static void Chunk(Stream s, string type, byte[] data)
    {
        var len = new byte[4];
        WriteBE(len, 0, (uint)data.Length);
        s.Write(len);
        var t = System.Text.Encoding.ASCII.GetBytes(type);
        s.Write(t);
        s.Write(data);
        var crc = Crc32(t, data);
        var c = new byte[4];
        WriteBE(c, 0, crc);
        s.Write(c);
    }

    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint Crc32(byte[] a, byte[] b)
    {
        var c = 0xFFFFFFFFu;
        foreach (var x in a) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        foreach (var x in b) c = CrcTable[(c ^ x) & 0xFF] ^ (c >> 8);
        return c ^ 0xFFFFFFFFu;
    }

    private static void WriteBE(byte[] b, int o, uint v)
    {
        b[o] = (byte)(v >> 24);
        b[o + 1] = (byte)(v >> 16);
        b[o + 2] = (byte)(v >> 8);
        b[o + 3] = (byte)v;
    }
}

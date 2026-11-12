namespace BadShip.Livery;

/// <summary>
/// A blocky stencil font drawn from rectangles on a 4×6 grid (with the small gaps stencil letters have), in keeping
/// with the industrial look: hull names, registrations and functional markings. Covers A–Z, 0–9, '-', '.', '/', ' '.
/// </summary>
public static class StencilFont
{
    // Each glyph: rectangles (x0, y0, x1, y1) on a 4-wide, 6-tall grid, y down.
    private static readonly Dictionary<char, (float, float, float, float)[]> Glyphs = new()
    {
        ['A'] = [(0, 1, 1, 6), (3, 1, 4, 6), (0.6f, 0, 3.4f, 1), (1.2f, 3, 2.8f, 4)],
        ['B'] = [(0, 0, 1, 6), (1.2f, 0, 3.3f, 1), (1.2f, 2.5f, 3.3f, 3.5f), (1.2f, 5, 3.3f, 6), (3, 0.6f, 4, 2.4f), (3, 3.6f, 4, 5.4f)],
        ['C'] = [(0, 0.6f, 1, 5.4f), (1.2f, 0, 4, 1), (1.2f, 5, 4, 6)],
        ['D'] = [(0, 0, 1, 6), (1.2f, 0, 3, 1), (1.2f, 5, 3, 6), (3, 0.8f, 4, 5.2f)],
        ['E'] = [(0, 0, 1, 6), (1.2f, 0, 4, 1), (1.2f, 2.5f, 3.2f, 3.5f), (1.2f, 5, 4, 6)],
        ['F'] = [(0, 0, 1, 6), (1.2f, 0, 4, 1), (1.2f, 2.5f, 3.2f, 3.5f)],
        ['G'] = [(0, 0.6f, 1, 5.4f), (1.2f, 0, 4, 1), (1.2f, 5, 3.4f, 6), (3, 3, 4, 5.4f), (2, 3, 2.8f, 4)],
        ['H'] = [(0, 0, 1, 6), (3, 0, 4, 6), (1.2f, 2.5f, 2.8f, 3.5f)],
        ['I'] = [(1.5f, 0, 2.5f, 6), (0.5f, 0, 1.3f, 1), (2.7f, 0, 3.5f, 1), (0.5f, 5, 1.3f, 6), (2.7f, 5, 3.5f, 6)],
        ['J'] = [(3, 0, 4, 5.4f), (0.6f, 5, 2.8f, 6), (0, 3.5f, 1, 4.8f)],
        ['K'] = [(0, 0, 1, 6), (1.2f, 2.5f, 2.4f, 3.5f), (2.4f, 0, 3.4f, 2.3f), (2.4f, 3.7f, 3.4f, 6)],
        ['L'] = [(0, 0, 1, 6), (1.2f, 5, 4, 6)],
        ['M'] = [(0, 0, 1, 6), (3, 0, 4, 6), (1.2f, 0.8f, 1.8f, 2.8f), (2.2f, 0.8f, 2.8f, 2.8f)],
        ['N'] = [(0, 0, 1, 6), (3, 0, 4, 6), (1.2f, 1, 1.9f, 3), (2.1f, 3, 2.8f, 5)],
        ['O'] = [(0, 0.6f, 1, 5.4f), (3, 0.6f, 4, 5.4f), (1.2f, 0, 2.8f, 1), (1.2f, 5, 2.8f, 6)],
        ['P'] = [(0, 0, 1, 6), (1.2f, 0, 3.3f, 1), (1.2f, 3, 3.3f, 4), (3, 0.6f, 4, 3.4f)],
        ['Q'] = [(0, 0.6f, 1, 5.4f), (3, 0.6f, 4, 4.6f), (1.2f, 0, 2.8f, 1), (1.2f, 5, 2.6f, 6), (2.8f, 4.8f, 4, 6)],
        ['R'] = [(0, 0, 1, 6), (1.2f, 0, 3.3f, 1), (1.2f, 2.8f, 2.6f, 3.8f), (3, 0.6f, 4, 2.6f), (2.8f, 4, 3.8f, 6)],
        ['S'] = [(1, 0, 4, 1), (0, 0.6f, 1, 2.6f), (1.2f, 2.5f, 2.8f, 3.5f), (3, 3.4f, 4, 5.4f), (0, 5, 3, 6)],
        ['T'] = [(0, 0, 1.8f, 1), (2.2f, 0, 4, 1), (1.5f, 1.2f, 2.5f, 6)],
        ['U'] = [(0, 0, 1, 5.4f), (3, 0, 4, 5.4f), (1.2f, 5, 2.8f, 6)],
        ['V'] = [(0, 0, 1, 4), (3, 0, 4, 4), (0.8f, 4.2f, 1.8f, 5.6f), (2.2f, 4.2f, 3.2f, 5.6f)],
        ['W'] = [(0, 0, 1, 6), (3, 0, 4, 6), (1.2f, 3.2f, 1.8f, 5.2f), (2.2f, 3.2f, 2.8f, 5.2f)],
        ['X'] = [(0, 0, 1, 2.2f), (3, 0, 4, 2.2f), (1.2f, 2.4f, 2.8f, 3.6f), (0, 3.8f, 1, 6), (3, 3.8f, 4, 6)],
        ['Y'] = [(0, 0, 1, 2.6f), (3, 0, 4, 2.6f), (1.5f, 2.8f, 2.5f, 6), (1.1f, 2.4f, 2.9f, 3.2f)],
        ['Z'] = [(0, 0, 4, 1), (2.4f, 1.2f, 3.4f, 2.6f), (0.6f, 3.4f, 1.6f, 4.8f), (0, 5, 4, 6)],
        ['0'] = [(0, 0.6f, 1, 5.4f), (3, 0.6f, 4, 5.4f), (1.2f, 0, 2.8f, 1), (1.2f, 5, 2.8f, 6), (1.6f, 2.4f, 2.4f, 3.6f)],
        ['1'] = [(1.5f, 0, 2.5f, 6), (0.6f, 0.6f, 1.3f, 1.6f), (0.6f, 5, 3.4f, 6)],
        ['2'] = [(0, 0, 3, 1), (3, 0.6f, 4, 2.6f), (1, 2.5f, 3, 3.5f), (0, 3.2f, 1, 5), (0, 5, 4, 6)],
        ['3'] = [(0, 0, 3.2f, 1), (3, 0.6f, 4, 2.6f), (1, 2.5f, 2.8f, 3.5f), (3, 3.4f, 4, 5.4f), (0, 5, 3.2f, 6)],
        ['4'] = [(0, 0, 1, 3.5f), (3, 0, 4, 6), (1.2f, 2.5f, 2.8f, 3.5f)],
        ['5'] = [(0, 0, 4, 1), (0, 1.2f, 1, 3), (0, 2.5f, 3, 3.5f), (3, 3.2f, 4, 5.4f), (0, 5, 3.2f, 6)],
        ['6'] = [(0, 0.6f, 1, 5.4f), (1.2f, 0, 4, 1), (1.2f, 2.5f, 3.2f, 3.5f), (3, 3.2f, 4, 5.4f), (1.2f, 5, 2.8f, 6)],
        ['7'] = [(0, 0, 4, 1), (3, 1.2f, 4, 2.8f), (2, 3, 3, 6)],
        ['8'] = [(0, 0.6f, 1, 2.4f), (3, 0.6f, 4, 2.4f), (0, 3.6f, 1, 5.4f), (3, 3.6f, 4, 5.4f), (1.2f, 0, 2.8f, 1), (1.2f, 2.5f, 2.8f, 3.5f), (1.2f, 5, 2.8f, 6)],
        ['9'] = [(0, 0.6f, 1, 2.8f), (3, 0.6f, 4, 5.4f), (1.2f, 0, 2.8f, 1), (0.8f, 2.5f, 2.8f, 3.5f), (0, 5, 2.8f, 6)],
        ['-'] = [(0.4f, 2.5f, 3.6f, 3.5f)],
        ['.'] = [(1.5f, 5, 2.5f, 6)],
        ['/'] = [(2.6f, 0, 3.6f, 2), (1.5f, 2, 2.5f, 4), (0.4f, 4, 1.4f, 6)],
        [' '] = [],
    };

    /// <summary>Width of text in cells for a given letter height (letters are 4/6 as wide as tall, plus spacing).</summary>
    public static float Measure(string text, float height) => text.Length * height * (4f / 6f + 0.22f) - height * 0.22f;

    /// <summary>Draws upper-case text with its top-left at (x, y), letters <paramref name="height"/> pixels tall.</summary>
    public static void Draw(Raster r, string text, float x, float y, float height, Raster.Color color)
    {
        var u = height / 6f;
        foreach (var ch in text.ToUpperInvariant())
        {
            if (Glyphs.TryGetValue(ch, out var rects))
                foreach (var (x0, y0, x1, y1) in rects)
                    r.FillRect(x + x0 * u, y + y0 * u, x + x1 * u, y + y1 * u, color);
            x += height * (4f / 6f + 0.22f);
        }
    }

    public static bool Supports(char ch) => Glyphs.ContainsKey(char.ToUpperInvariant(ch));
}

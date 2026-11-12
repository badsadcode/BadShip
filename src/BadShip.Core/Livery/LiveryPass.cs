using System.Numerics;
using BadShip.Generation;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Livery;

/// <summary>
/// Optional markings: the ship's name in stencil letters, its registration, the operator's emblem, the yard's mark and
/// hazard chevrons on the engines. Everything is drawn into one decal sheet (a PNG inside the GLB) and placed as thin
/// alpha-cutout quads on flat hull faces, reading left to right from whichever side you look. It runs after the
/// equipment (so it can avoid windows, bays and collars) and before the surface plates (so they avoid it). It has its
/// own random stream: names and numbers never move the hull. recipe.Livery = "none" leaves the ship unmarked.
/// </summary>
public static class LiveryPass
{
    public const int SheetW = 1024, SheetH = 512;

    // Regions of the decal sheet, in pixels: x, y, w, h.
    private static readonly (int x, int y, int w, int h) NameRect = (0, 0, 1024, 128);
    private static readonly (int x, int y, int w, int h) RegRect = (0, 136, 640, 96);
    private static readonly (int x, int y, int w, int h) EmblemRect = (648, 136, 120, 120);
    private static readonly (int x, int y, int w, int h) MarkRect = (776, 136, 120, 120);
    private static readonly (int x, int y, int w, int h) ChevronRect = (0, 264, 512, 64);

    private static readonly Dictionary<string, string[]> Names = new()
    {
        ["freighter"] = ["Stubborn Ox", "Iron Mule", "Long Haul", "Ore Queen", "Packhorse", "Big Margaret", "Steady Hand", "Bulk Venture", "Old Reliable", "Cargo Cult", "Heavy Lifter", "Slow Boat", "Good Fortune", "Deep Pockets", "Brass Monkey", "Second Wind"],
        ["research"] = ["Halcyon", "Perihelion", "Albatross", "Cartographer", "Lantern", "Vesper", "Tern", "Long Sight", "Aurora Deep", "Wayfinder", "Patient Eye", "Far Shore", "Sounding Line", "Kestrel", "Pathfinder", "Quiet Tide"],
        ["patrol"] = ["Vigilant", "Bulwark", "Sentinel", "Picket", "Warden", "Bastion", "Resolute", "Steadfast", "Iron Gate", "Watchful", "Rampart", "Lookout", "Defiant", "Tenacious", "Gatekeeper", "Shield Wall"],
    };

    private static readonly Dictionary<string, string> Prefixes = new()
    {
        ["haulers"] = "FHG", ["survey"] = "OSD", ["patrol"] = "FPC", ["miners"] = "KBM", ["independent"] = "IND",
    };

    private static List<(string Id, Aabb Bounds)> _obstacles = [];

    public static void Apply(BuildContext c)
    {
        var p = c.Plan;
        var r = Rng.Stream(c.Recipe.StageSeeds?.GetValueOrDefault("livery") is { } liverySeed and > 0 ? liverySeed : c.Recipe.Seed, "livery");
        p.ShipName = !string.IsNullOrWhiteSpace(c.Recipe.Name) ? Clean(c.Recipe.Name) : r.Pick(Names[p.Archetype]).ToUpperInvariant();
        var prefix = Prefixes.GetValueOrDefault(p.Faction) ?? Initials(p.FactionName);
        p.Registration = $"{prefix}-{r.Int(100, 9999)}";
        p.Livery = c.Recipe.Livery is "none" ? "none" : "standard";
        if (p.Livery == "none") return;

        // Colours: letters that read on the hull, emblems in the operator's accent.
        var hull = c.Ship.Materials[Roles.HullMain].Color;
        var lum = 0.2126f * hull.X + 0.7152f * hull.Y + 0.0722f * hull.Z;
        var ink = lum > 0.2f ? Raster.Color.Hex("#1e2227") : Raster.Color.Hex("#e9e4d8");
        var acc = c.Ship.Materials[Roles.Accent].Color;
        var accent = new Raster.Color(Srgb(acc.X), Srgb(acc.Y), Srgb(acc.Z));

        var sheet = new Raster(SheetW, SheetH);
        DrawText(sheet, NameRect, p.ShipName, ink, 0.78f);
        DrawText(sheet, RegRect, p.Registration, ink, 0.7f);
        Emblem(sheet, EmblemRect, p.Faction, accent, ink);
        Mark(sheet, MarkRect, p.Manufacturer, p.ManufacturerName, ink, accent);
        Chevrons(sheet, ChevronRect, Raster.Color.Hex("#d9b21c"), Raster.Color.Hex("#1e2227"));

        c.Ship.Materials[Roles.Livery] = new MaterialDef(Roles.Livery, Vector3.One, 0.55f, 0.1f, Vector3.Zero, 1) { Png = sheet.Png(), Cutout = true };

        // Bounds once, up front: every placement attempt tests against them.
        _obstacles = c.Ship.Modules.Where(m => m.Tier != ModuleTier.Tertiary).Select(m => { m.UpdateBounds(); return (m.Id, m.Bounds); }).ToList();

        // Where the markings go: side faces of the hull masses and superstructure (prow facets included), biggest
        // first; each marking takes the first face where it fits without covering anything.
        var faces = new List<(LoftPart part, int s, int f, float along, float across)>();
        foreach (var part in c.Lofts.Where(l => l.Kind is "hull" or "tier"))
            for (var s = 0; s < part.Loft.Segments; s++)
            {
                if (part.Loft.IsStep(s)) continue;
                foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX })
                {
                    var (along, across) = part.Loft.FaceSize(s, f);
                    if (along > 4 && across > 1.2f) faces.Add((part, s, f, along, across));
                }
            }
        var nameH = p.Length * (p.Size == "small" ? 0.05f : 0.03f);
        foreach (var side in new[] { Profile.FacePosX, Profile.FaceNegX })
        {
            var mine = faces.Where(x => x.f == side).OrderByDescending(x => x.part.Kind == "hull").ThenByDescending(x => x.along * x.across).ToList();
            bool PlaceAny((int x, int y, int w, int h) rect, float scale, float[] us, float up)
            {
                // Full size on the best faces first, then a little smaller, never below a readable minimum.
                var minH = MathF.Max(0.6f, nameH * scale * 0.4f);
                foreach (var shrink in new[] { 1f, 0.75f, 0.55f })
                    foreach (var (part, seg, face, along, across) in mine)
                    {
                        var h = MathF.Min(across * 0.4f, nameH) * scale * shrink;
                        if (h >= minH && Place(c, c.LiveryOf(part.Module).Mesh, part, seg, face, along, across, rect, h, us, up))
                            return true;
                    }
                return false;
            }
            PlaceAny(NameRect, 1f, [0.5f, 0.62f, 0.4f, 0.72f, 0.3f, 0.82f], 0.7f);
            PlaceAny(RegRect, 0.5f, [0.85f, 0.2f, 0.7f, 0.12f], 0.3f);
            PlaceAny(EmblemRect, 1.1f, [0.3f, 0.22f, 0.6f, 0.4f], 0.6f);
            PlaceAny(MarkRect, 0.7f, [0.1f, 0.16f, 0.9f, 0.06f], 0.62f);
        }
        // Hazard chevrons on the sides of engine pods: the danger zone behind them.
        foreach (var part in c.Lofts.Where(l => l.Kind == "pod" && l.Module.Type == "engine"))
            foreach (var f in new[] { Profile.FacePosX, Profile.FaceNegX })
            {
                var (along, across) = part.Loft.FaceSize(1, f);
                if (along < 1.5f || across < 0.8f) continue;
                Place(c, c.LiveryOf(part.Module).Mesh, part, 1, f, along, across, ChevronRect, across * 0.28f, [0.2f, 0.5f], 0.5f, stretch: MathF.Min(along * 0.8f, across * 2.5f));
            }
    }

    /// <summary>
    /// Puts one sheet region on a face as a quad of height <paramref name="h"/> (width from the region's shape, or
    /// <paramref name="stretch"/>), trying the given positions along the face until one is free.
    /// </summary>
    private static bool Place(BuildContext c, Mesh mesh, LoftPart part, int s, int f, float along, float across,
        (int x, int y, int w, int h) rect, float h, float[] us, float up, float? stretch = null)
    {
        var loft = part.Loft;
        var w = stretch ?? h * rect.w / rect.h;
        if (w > along * 0.9f) { h *= along * 0.9f / w; w = along * 0.9f; }
        if (h < 0.4f || h > across * 0.9f) return false;
        var du = w / along;
        var dv = h / across;
        var key = BuildContext.FaceKey(part.Module, s, f);
        var n = loft.FaceNormal(s, f);
        var upDir = Vector3.Normalize(Vector3.UnitY - n * Vector3.Dot(Vector3.UnitY, n));
        var right = Vector3.Normalize(Vector3.Cross(-n, upDir));
        foreach (var vTry in new[] { up, up - 0.2f, up + 0.18f })
            foreach (var u in us)
            {
                var u0 = Math.Clamp(u - du / 2, 0.02f, 0.98f - du);
                var vUp = Math.Clamp(vTry, dv / 2 + 0.04f, 0.96f - dv / 2);
                var a = Parts.UpV(f, vUp - dv / 2);
                var b = Parts.UpV(f, vUp + dv / 2);
                var (v0, v1) = a < b ? (a, b) : (b, a);
                if (!c.SurfaceFree(key, u0 - 0.01f, u0 + du + 0.01f, v0 - 0.02f, v1 + 0.02f)) continue;
                var center = loft.FacePoint(s, f, u0 + du / 2, (v0 + v1) / 2) + n * (loft.Profiles[s] == loft.Profiles[s + 1] ? 0.04f : 0.1f);
                // Nothing may stand in front of it (side modules, pods, collars bolted onto this face).
                var front = new Aabb(center, center).Include(center + n * 1.5f)
                    .Include(center + right * (w / 2) + upDir * (h / 2)).Include(center - right * (w / 2) - upDir * (h / 2))
                    .Include(center + right * (w / 2) - upDir * (h / 2)).Include(center - right * (w / 2) + upDir * (h / 2));
                if (_obstacles.Any(o => o.Id != part.Module.Id && o.Bounds.Penetration(front) > 0.05f)) continue;
                c.UseSurface(key, u0 - 0.01f, u0 + du + 0.01f, v0 - 0.02f, v1 + 0.02f);
                // Upright on the face, reading left to right for someone looking at it.
                Quad(mesh, center, right, upDir, n, w, h, rect);
                return true;
            }
        return false;
    }

    private static void Quad(Mesh mesh, Vector3 c, Vector3 right, Vector3 up, Vector3 n, float w, float h, (int x, int y, int w, int h) rect)
    {
        var tl = c - right * (w / 2) + up * (h / 2);
        var tr = c + right * (w / 2) + up * (h / 2);
        var br = c + right * (w / 2) - up * (h / 2);
        var bl = c - right * (w / 2) - up * (h / 2);
        float U(float px) => px / SheetW;
        float V(float py) => py / SheetH;
        mesh.AddPolygonUv(Roles.Livery, [tl, tr, br, bl],
            [new(U(rect.x), V(rect.y)), new(U(rect.x + rect.w), V(rect.y)), new(U(rect.x + rect.w), V(rect.y + rect.h)), new(U(rect.x), V(rect.y + rect.h))], n);
    }

    // ------------------------------------------------------------------ drawing the sheet

    private static void DrawText(Raster r, (int x, int y, int w, int h) rect, string text, Raster.Color ink, float fill)
    {
        var height = rect.h * fill;
        var width = StencilFont.Measure(text, height);
        if (width > rect.w * 0.96f) { height *= rect.w * 0.96f / width; width = StencilFont.Measure(text, height); }
        StencilFont.Draw(r, text, rect.x + (rect.w - width) / 2, rect.y + (rect.h - height) / 2, height, ink);
    }

    /// <summary>The operator's emblem: a ring with a shape chosen from the faction name, in its accent colour.</summary>
    private static void Emblem(Raster r, (int x, int y, int w, int h) rect, string faction, Raster.Color accent, Raster.Color ink)
    {
        var cx = rect.x + rect.w / 2f;
        var cy = rect.y + rect.h / 2f;
        var R = rect.w * 0.46f;
        r.FillCircle(cx, cy, R, accent);
        r.FillRing(cx, cy, R * 0.8f, R * 0.88f, ink);
        var shape = (int)(Rng.Fnv1a(faction) % 4);
        var k = R * 0.55f;
        switch (shape)
        {
            case 0: // chevron
                r.FillPolygon([(cx - k, cy - k * 0.2f), (cx, cy - k * 0.9f), (cx + k, cy - k * 0.2f), (cx + k, cy + k * 0.3f), (cx, cy - k * 0.4f), (cx - k, cy + k * 0.3f)], ink);
                r.FillPolygon([(cx - k, cy + k * 0.5f), (cx, cy - k * 0.2f), (cx + k, cy + k * 0.5f), (cx + k, cy + k), (cx, cy + k * 0.3f), (cx - k, cy + k)], ink);
                break;
            case 1: // star
                {
                    var pts = new List<(float, float)>();
                    for (var i = 0; i < 10; i++)
                    {
                        var a = -MathF.PI / 2 + i * MathF.PI / 5;
                        var rr = i % 2 == 0 ? k : k * 0.42f;
                        pts.Add((cx + MathF.Cos(a) * rr, cy + MathF.Sin(a) * rr));
                    }
                    r.FillPolygon(pts, ink);
                    break;
                }
            case 2: // three bars
                for (var i = -1; i <= 1; i++) r.FillRect(cx - k, cy + i * k * 0.6f - k * 0.18f, cx + k, cy + i * k * 0.6f + k * 0.18f, ink);
                break;
            default: // diamond with core
                r.FillPolygon([(cx, cy - k), (cx + k, cy), (cx, cy + k), (cx - k, cy)], ink);
                r.FillCircle(cx, cy, k * 0.35f, accent);
                break;
        }
    }

    /// <summary>The yard's mark: each preset house has its own sign; procedural yards get their initial in a hexagon.</summary>
    private static void Mark(Raster r, (int x, int y, int w, int h) rect, string key, string name, Raster.Color ink, Raster.Color accent)
    {
        float x0 = rect.x + 6, y0 = rect.y + 6, x1 = rect.x + rect.w - 6, y1 = rect.y + rect.h - 6;
        var cx = (x0 + x1) / 2;
        var cy = (y0 + y1) / 2;
        var s = (x1 - x0) / 2;
        switch (key)
        {
            case "halvorsen": // a square frame around an H
                r.FillRect(x0, y0, x1, y1, ink);
                ClearRect(r, x0 + s * 0.16f, y0 + s * 0.16f, x1 - s * 0.16f, y1 - s * 0.16f);
                StencilFont.Draw(r, "H", cx - s * 0.45f, cy - s * 0.68f, s * 1.36f, ink);
                break;
            case "meridian": // a rising arc over a horizon line
                r.FillRing(cx, cy + s * 0.25f, s * 0.6f, s * 0.82f, ink);
                ClearRect(r, x0, cy + s * 0.25f, x1, y1);
                r.FillRect(x0, cy + s * 0.32f, x1, cy + s * 0.52f, ink);
                r.FillCircle(cx, cy + s * 0.05f, s * 0.2f, accent);
                break;
            case "korsak": // a downward wedge with a notch
                r.FillPolygon([(x0, y0 + s * 0.1f), (x1, y0 + s * 0.1f), (cx, y1), (cx, y0 + s * 0.9f), (cx - s * 0.2f, y0 + s * 0.6f)], ink);
                break;
            case "tanaka": // two slanted bars
                r.FillPolygon([(x0 + s * 0.2f, y1), (x0 + s * 0.7f, y0), (x0 + s * 1.05f, y0), (x0 + s * 0.55f, y1)], ink);
                r.FillPolygon([(x0 + s * 0.85f, y1), (x0 + s * 1.35f, y0), (x0 + s * 1.7f, y0), (x0 + s * 1.2f, y1)], ink);
                break;
            case "brightwater": // twin waves (two hulls)
                for (var i = 0; i < 2; i++)
                {
                    var yy = cy - s * 0.35f + i * s * 0.6f;
                    r.FillPolygon([(x0, yy + s * 0.2f), (cx - s * 0.5f, yy - s * 0.15f), (cx, yy + s * 0.2f), (cx + s * 0.5f, yy - s * 0.15f), (x1, yy + s * 0.2f),
                        (x1, yy + s * 0.42f), (cx + s * 0.5f, yy + s * 0.07f), (cx, yy + s * 0.42f), (cx - s * 0.5f, yy + s * 0.07f), (x0, yy + s * 0.42f)], ink);
                }
                break;
            default: // procedural yard: initial in a hexagon
                {
                    var pts = new List<(float, float)>();
                    for (var i = 0; i < 6; i++) { var a = MathF.PI / 6 + i * MathF.PI / 3; pts.Add((cx + MathF.Cos(a) * s, cy + MathF.Sin(a) * s)); }
                    r.FillPolygon(pts, ink);
                    var initial = name.Length > 0 && StencilFont.Supports(name[0]) ? name[..1] : "Y";
                    StencilFont.Draw(r, initial, cx - s * 0.33f, cy - s * 0.5f, s, accent);
                    break;
                }
        }
    }

    private static void ClearRect(Raster r, float x0, float y0, float x1, float y1)
    {
        for (var y = Math.Max(0, (int)y0); y < Math.Min(r.Height, (int)Math.Ceiling(y1)); y++)
            for (var x = Math.Max(0, (int)x0); x < Math.Min(r.Width, (int)Math.Ceiling(x1)); x++)
                Array.Clear(r.Rgba, (y * r.Width + x) * 4, 4);
    }

    private static void Chevrons(Raster r, (int x, int y, int w, int h) rect, Raster.Color yellow, Raster.Color black)
    {
        r.FillRect(rect.x, rect.y, rect.x + rect.w, rect.y + rect.h, yellow);
        var step = rect.h * 1.2f;
        for (float x = rect.x - rect.h; x < rect.x + rect.w; x += step)
            r.FillPolygon([(x, rect.y + rect.h), (x + rect.h * 0.55f, rect.y + rect.h), (x + rect.h * 0.55f + rect.h, rect.y), (x + rect.h, rect.y)], black);
        ClearRect(r, rect.x + rect.w, rect.y, rect.x + rect.w + rect.h * 2, rect.y + rect.h);
        ClearRect(r, rect.x - rect.h * 2, rect.y, rect.x, rect.y + rect.h);
    }

    private static string Clean(string name)
    {
        var s = new string(name.ToUpperInvariant().Where(StencilFont.Supports).ToArray()).Trim();
        return s.Length == 0 ? "UNNAMED" : s.Length > 18 ? s[..18] : s;
    }

    private static string Initials(string name) => new string(name.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => char.ToUpperInvariant(w[0])).Take(3).ToArray()).PadRight(3, 'X');

    private static float Srgb(float l) => l <= 0.0031308f ? l * 12.92f : 1.055f * MathF.Pow(l, 1 / 2.4f) - 0.055f;
}

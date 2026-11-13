using System.Numerics;
using BadShip.Generation;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Interior;

/// <summary>
/// Furniture for the rooms: bunks in crew quarters, consoles on the bridge, tables in the mess, crates in the holds.
/// Every piece stands against a wall and is its own node (type "furniture", with its <c>kind</c>, room, size and the
/// way it faces in the extras), so a game can collide with it, use it, or swap it for a proper asset by kind.
/// Nothing goes where people need to walk: not in front of a door, not on the way from a door to the room's middle,
/// not on a stair or ladder landing, an elevator stop or over a hole in the floor. On a shaped deck every piece has to
/// fit inside the skin, and tall pieces keep clear of windows.
/// </summary>
public static class Furniture
{
    public const string Frame = "furniture_frame", Soft = "furniture_soft", Top = "furniture_top";

    /// <summary>A piece: width along the wall, depth into the room, height.</summary>
    private sealed record Piece(string Kind, float W, float D, float H);

    private static readonly Dictionary<string, Piece> Pieces = new Piece[]
    {
        new("bunk", 2.0f, 0.95f, 1.75f), new("bed", 2.0f, 1.0f, 0.6f), new("med_bed", 2.0f, 0.9f, 0.9f),
        new("locker", 0.6f, 0.5f, 1.9f), new("console", 1.4f, 0.75f, 1.15f), new("desk", 1.6f, 1.35f, 0.8f),
        new("mess_table", 1.9f, 2.0f, 0.75f), new("counter", 2.4f, 0.7f, 0.95f), new("crates", 1.3f, 1.3f, 1.25f),
        new("workbench", 2.0f, 0.8f, 1.7f), new("rack", 1.6f, 0.5f, 2.0f), new("machine", 1.6f, 1.4f, 2.2f),
        new("sofa", 2.2f, 0.9f, 0.85f), new("shelf", 2.0f, 0.5f, 2.0f), new("bench", 1.8f, 0.5f, 0.45f),
    }.ToDictionary(p => p.Kind);

    /// <summary>What each kind of room is furnished with, in order of importance (pieces that don't fit are skipped).</summary>
    private static readonly Dictionary<string, string[]> Programs = new()
    {
        ["crew_quarters"] = ["bunk", "bunk", "locker", "locker", "bunk", "locker"],
        ["bunk_room"] = ["bunk", "bunk", "bunk", "bunk", "locker", "locker", "locker"],
        ["passenger_cabin"] = ["bed", "locker", "desk", "sofa"],
        ["captain_cabin"] = ["bed", "desk", "locker", "sofa"],
        ["captain_office"] = ["desk", "shelf", "sofa", "console"],
        ["bridge"] = ["console", "console", "console", "console", "console", "console"],
        ["cockpit"] = ["console", "console"],
        ["comms"] = ["console", "console", "console", "rack"],
        ["operations"] = ["console", "console", "console", "console", "desk"],
        ["briefing"] = ["desk", "desk", "console", "shelf"],
        ["mess"] = ["mess_table", "mess_table", "mess_table", "mess_table", "counter"],
        ["galley"] = ["counter", "counter", "counter", "locker", "shelf"],
        ["lounge"] = ["sofa", "sofa", "sofa", "counter"],
        ["suite_lounge"] = ["sofa", "sofa", "desk"],
        ["observation"] = ["sofa", "sofa", "sofa"],
        ["commons"] = ["sofa", "sofa", "mess_table", "mess_table", "counter"],
        ["medbay"] = ["med_bed", "med_bed", "console", "locker", "shelf"],
        ["ward"] = ["med_bed", "med_bed", "med_bed", "med_bed", "locker"],
        ["lab"] = ["workbench", "workbench", "console", "shelf"],
        ["clean_room"] = ["workbench", "workbench", "locker"],
        ["workshop"] = ["workbench", "workbench", "rack", "shelf", "crates"],
        ["engineering"] = ["machine", "machine", "console", "shelf", "machine"],
        ["engine_room"] = ["machine", "machine", "console"],
        ["reactor"] = ["machine", "machine", "console"],
        ["life_support"] = ["machine", "machine", "shelf"],
        ["armoury"] = ["rack", "rack", "rack", "rack", "locker"],
        ["brig"] = ["bench", "bench"],
        ["storage"] = ["shelf", "shelf", "crates", "crates", "shelf"],
        ["cargo_hold"] = ["crates", "crates", "crates", "crates", "crates", "crates", "shelf"],
        ["cargo_bay"] = ["crates", "crates", "crates", "crates", "crates", "crates"],
        ["hangar"] = ["crates", "crates", "rack", "crates"],
    };

    public static IEnumerable<string> Kinds => Pieces.Keys;

    private readonly record struct Rect(float X0, float X1, float Z0, float Z1)
    {
        public bool Overlaps(Rect o) => X0 < o.X1 && o.X0 < X1 && Z0 < o.Z1 && o.Z0 < Z1;
        public Rect Grow(float m) => new(X0 - m, X1 + m, Z0 - m, Z1 + m);
        public static Rect Around(float x, float z, float r) => new(x - r, x + r, z - r, z + r);
        public static Rect Span(float xa, float za, float xb, float zb, float r) =>
            new(MathF.Min(xa, xb) - r, MathF.Max(xa, xb) + r, MathF.Min(za, zb) - r, MathF.Max(za, zb) + r);
    }

    /// <summary>Furnishes every room. <paramref name="shells"/> are the shaped decks' lofts (by deck), seen through
    /// an inset of <paramref name="inset"/>; <paramref name="holes"/> the openings in each deck's floor.</summary>
    public static void Place(Ship model, ShipInterior interior, IReadOnlyDictionary<int, Loft> shells, float inset,
        IReadOnlyDictionary<int, List<(float x0, float x1, float z0, float z1)>> holes, IReadOnlyDictionary<string, Vector3> markers)
    {
        if (!model.Materials.ContainsKey(Frame))
        {
            model.Materials[Frame] = new MaterialDef(Frame, Palettes.Linear("#5d666f"), 0.6f, 0.4f, Vector3.Zero, 1);
            model.Materials[Soft] = new MaterialDef(Soft, Palettes.Linear("#6f7f8c"), 0.95f, 0f, Vector3.Zero, 1);
            model.Materials[Top] = new MaterialDef(Top, Palettes.Linear("#b9bcb8"), 0.5f, 0.1f, Vector3.Zero, 1);
        }
        var r = Rng.Stream(model.Recipe.Seed, "furniture");
        foreach (var room in interior.Rooms)
        {
            if (!Programs.TryGetValue(room.Kind, out var program)) continue;
            var d = interior.Decks[room.Deck];
            if (!markers.TryGetValue(room.Id, out var mark)) continue;
            // The floor inside the walls.
            var area = new Rect(room.Min[0] + 0.15f, room.Max[0] - 0.15f, room.Min[2] + 0.15f, room.Max[2] - 0.15f);
            if (area.X1 - area.X0 < 1.6f || area.Z1 - area.Z0 < 1.6f) continue;
            var keep = new List<Rect> { Rect.Around(mark.X, mark.Z, 0.8f) };
            // Doors in this room's walls: the space in front of each, and a path from it to the room's middle.
            foreach (var door in interior.Doors.Where(x => x.Deck == room.Deck))
            {
                var (cx, cz) = (door.Center[0], door.Center[2]);
                if (cx < room.Min[0] - 0.2f || cx > room.Max[0] + 0.2f || cz < room.Min[2] - 0.2f || cz > room.Max[2] + 0.2f) continue;
                keep.Add(Rect.Around(cx, cz, DoorClear));
                // Into the room square to the wall, then along to the middle.
                var acrossZ = MathF.Abs(cx - room.Min[0]) < 0.25f || MathF.Abs(cx - room.Max[0]) < 0.25f;
                var (kx, kz) = acrossZ ? (mark.X, cz) : (cx, mark.Z);
                keep.Add(Rect.Span(cx, cz, kx, kz, Aisle));
                keep.Add(Rect.Span(kx, kz, mark.X, mark.Z, Aisle));
            }
            // Stairs (and the floor round both ends), ladders, elevators and holes in the floor.
            foreach (var st in interior.Stairs.Where(x => x.FromDeck == room.Deck || x.ToDeck == room.Deck))
                keep.Add(new Rect(st.Min[0] - 0.4f, st.Max[0] + 0.4f, st.Min[2] - 1.3f, st.Max[2] + 1.3f));
            foreach (var l in interior.Lifts.Where(x => x.Decks.Contains(room.Deck)))
            {
                keep.Add(new Rect(l.Shaft[0], l.Shaft[1], l.Shaft[2], l.Shaft[3]).Grow(0.4f));
                foreach (var a in l.Access) if (MathF.Abs(a[1] - d.FloorY) < 1f) keep.Add(Rect.Around(a[0], a[2], 1.1f));
            }
            if (holes.TryGetValue(room.Deck, out var hs))
                foreach (var h in hs) keep.Add(new Rect(h.x0, h.x1, h.z0, h.z1).Grow(0.4f));
            var windows = interior.Windows.Where(w => w.Room == room.Id).ToList();
            shells.TryGetValue(room.Deck, out var shell);

            var parent = $"room_{room.Id}";
            var placed = 0;
            // The list goes round again while pieces still fit, up to about one piece per 7 m² of floor.
            var maxPieces = Math.Max(1, (int)((area.X1 - area.X0) * (area.Z1 - area.Z0) / 7f));
            var front = room.Kind is "bridge" or "cockpit" or "observation";
            for (var pass = 0; placed < maxPieces && pass < 6; pass++)
            {
                var before = placed;
                foreach (var kind in program)
                {
                    if (placed >= maxPieces) break;
                    var p = Pieces[kind];
                    if (pass > 0 && p.Kind is "desk" or "bed" or "sofa" && room.Kind is "passenger_cabin" or "captain_cabin" or "captain_office") continue;
                    if (TryPlace(p, area, keep, windows, shell, inset, d, r, front && p.Kind is "console" or "sofa", out var spot))
                    {
                        keep.Add(spot.rect.Grow(0.15f));
                        Build(model, parent, room, p, spot.rect, spot.facing, d.FloorY, placed);
                        placed++;
                    }
                }
                if (placed == before) break;
            }
        }
    }

    private const float DoorClear = 1.4f, Aisle = 0.65f;

    /// <summary>Finds a place against a wall: tries the four walls in a random order, sliding along each.</summary>
    private static bool TryPlace(Piece p, Rect area, List<Rect> keep, List<WindowInfo> windows, Loft? shell, float inset,
        DeckInfo d, Rng r, bool lookForward, out (Rect rect, Vector3 facing) spot)
    {
        // Facing: the direction from the wall into the room. The long walls first (in a random order), or, for a
        // bridge's consoles, the forward wall first, so they face the front glass.
        var lenZ = area.Z1 - area.Z0;
        var lenX = area.X1 - area.X0;
        var flip = r.Sign();
        var walls = new List<Vector3> { Vector3.UnitX * flip, -Vector3.UnitX * flip, Vector3.UnitZ * flip, -Vector3.UnitZ * flip }
            .OrderBy(f => lookForward && f.Z < 0 ? 0 : 1).ThenByDescending(f => f.X != 0 ? lenZ : lenX).ToList();
        var fromStart = r.Chance(0.5);
        foreach (var f in walls)
        {
            var alongZ = f.X != 0; // a wall at constant x runs along z
            var (lo, hi) = alongZ ? (area.Z0, area.Z1) : (area.X0, area.X1);
            if (hi - lo < p.W) continue;
            for (var k = 0; ; k++)
            {
                var s = fromStart ? lo + k * 0.25f : hi - p.W - k * 0.25f;
                if (s < lo - 1e-4f || s + p.W > hi + 1e-4f) break;
                Rect rect;
                if (alongZ)
                {
                    var x0 = f.X > 0 ? area.X0 : area.X1 - p.D;
                    rect = new Rect(x0, x0 + p.D, s, s + p.W);
                }
                else
                {
                    var z0 = f.Z > 0 ? area.Z0 : area.Z1 - p.D;
                    rect = new Rect(s, s + p.W, z0, z0 + p.D);
                }
                if (rect.X0 < area.X0 - 1e-4f || rect.X1 > area.X1 + 1e-4f || rect.Z0 < area.Z0 - 1e-4f || rect.Z1 > area.Z1 + 1e-4f) continue;
                if (keep.Any(x => x.Overlaps(rect))) continue;
                if (p.Kind != "console" && p.H > InteriorGenerator.Sill + 0.1f && windows.Any(w => BlocksWindow(w, rect))) continue;
                if (!Fits(rect, p.H, shell, inset, d)) continue;
                spot = (rect, f);
                return true;
            }
        }
        spot = default;
        return false;
    }

    /// <summary>A tall piece in front of a window (within a metre of the wall it is in, across its width).</summary>
    private static bool BlocksWindow(WindowInfo w, Rect rect)
    {
        if (w.F == 8) return rect.Z1 > w.Z0 - 1.5f; // the front glass: keep the forward end clear
        if (w.Side == 0) return false;               // overhead
        var near = w.Side > 0 ? rect.X1 > w.WallX - 1.2f : rect.X0 < w.WallX + 1.2f;
        return near && rect.Z0 < w.Z1 + 0.2f && w.Z0 - 0.2f < rect.Z1;
    }

    /// <summary>Inside the deck's skin at both ends and at the top (and under a sloped ceiling).</summary>
    private static bool Fits(Rect rect, float h, Loft? shell, float inset, DeckInfo d)
    {
        var top = d.FloorY + h + 0.05f;
        if (top > d.CeilingY - 0.05f) return false;
        if (shell is null) return true;
        foreach (var z in new[] { rect.Z0, rect.Z1, (rect.Z0 + rect.Z1) / 2 })
        {
            var sec = Skin.Inset(Skin.SectionAt(shell, z), inset + 0.05f);
            foreach (var y in new[] { d.FloorY + 0.05f, top })
            {
                if (Skin.SpanAt(sec, y) is not { } span || rect.X0 < span.a || rect.X1 > span.b) return false;
            }
            foreach (var x in new[] { rect.X0, rect.X1 })
                if (!(Skin.TopAt(sec, x) > top)) return false;
        }
        return true;
    }

    /// <summary>One piece of furniture as its own node, built in the frame of the wall it stands against.</summary>
    private static void Build(Ship model, string parent, RoomInfo room, Piece p, Rect rect, Vector3 facing, float fy, int n)
    {
        var m = InteriorGenerator.Add(model, $"{room.Id}_{p.Kind}_{n}", "furniture", parent);
        var c = new Vector3((rect.X0 + rect.X1) / 2, fy, (rect.Z0 + rect.Z1) / 2);
        m.Pivot = c;
        m.Extras = new()
        {
            ["kind"] = p.Kind, ["room"] = room.Id, ["deck"] = room.Deck,
            ["width"] = p.W, ["depth"] = p.D, ["height"] = p.H, ["facing"] = InteriorGenerator.A(facing),
        };
        // Local frame: u along the wall, f into the room, back of the piece at the wall (f = 0), floor at y = 0.
        var u = new Vector3(MathF.Abs(facing.Z), 0, MathF.Abs(facing.X));
        var back = c - facing * (p.D / 2);
        void B(string mat, float u0, float u1, float f0, float f1, float y0, float y1)
        {
            var a = back + u * u0 + facing * f0 + new Vector3(0, y0, 0);
            var b = back + u * u1 + facing * f1 + new Vector3(0, y1, 0);
            Shapes.BoxMinMax(m.Mesh, mat, Vector3.Min(a, b), Vector3.Max(a, b));
        }
        var (hw, D, H) = (p.W / 2, p.D, p.H);
        switch (p.Kind)
        {
            case "bunk":
                foreach (var s in new[] { -1f, 1f })
                    foreach (var ff in new[] { 0.02f, D - 0.06f })
                        B(Frame, s * hw - (s > 0 ? 0.06f : 0), s * hw + (s < 0 ? 0.06f : 0), ff, ff + 0.05f, 0, H);
                foreach (var y in new[] { 0.3f, 1.15f })
                {
                    B(Frame, -hw, hw, 0, D, y, y + 0.08f);
                    B(Soft, -hw + 0.08f, hw - 0.08f, 0.05f, D - 0.05f, y + 0.08f, y + 0.24f);
                    B(Top, hw - 0.5f, hw - 0.1f, 0.12f, D - 0.12f, y + 0.24f, y + 0.34f); // pillow
                }
                B(Frame, -hw, hw, D - 0.06f, D - 0.02f, 1.55f, 1.6f); // a rail on the top bunk
                break;
            case "bed":
                B(Frame, -hw, hw, 0, D, 0, 0.35f);
                B(Soft, -hw + 0.05f, hw - 0.05f, 0.05f, D - 0.05f, 0.35f, 0.5f);
                B(Top, hw - 0.5f, hw - 0.1f, 0.15f, D - 0.15f, 0.5f, 0.6f);
                B(Frame, hw - 0.05f, hw, 0, D, 0, 0.6f); // headboard
                break;
            case "med_bed":
                B(Frame, -hw + 0.1f, -hw + 0.25f, 0.3f, D - 0.3f, 0, 0.6f);
                B(Frame, hw - 0.25f, hw - 0.1f, 0.3f, D - 0.3f, 0, 0.6f);
                B(Frame, -hw, hw, 0.05f, D, 0.6f, 0.7f);
                B(Soft, -hw + 0.05f, hw - 0.05f, 0.1f, D - 0.05f, 0.7f, 0.82f);
                B(Frame, hw - 0.1f, hw, 0, 0.12f, 0.7f, 0.9f);
                B(Roles.Light, hw - 0.45f, hw - 0.1f, 0, 0.04f, 1.3f, 1.6f); // a monitor on the wall
                break;
            case "locker":
                B(Frame, -hw, hw, 0, D, 0, H);
                B(Top, -0.01f, 0.01f, D, D + 0.01f, 0.2f, H - 0.2f); // the gap between the doors
                B(Roles.Light, -hw + 0.08f, -hw + 0.18f, D, D + 0.01f, 1.55f, 1.6f);
                break;
            case "console":
                B(Frame, -hw, hw, 0.1f, D, 0, 0.75f);
                B(Top, -hw, hw, 0.05f, D, 0.75f, 0.9f);
                B(Frame, -hw + 0.05f, hw - 0.05f, 0, 0.12f, 0.9f, H);
                B(Roles.Light, -hw + 0.12f, hw - 0.12f, 0.12f, 0.14f, 0.98f, H - 0.07f); // the screen
                B(Roles.Light, -hw + 0.15f, hw - 0.15f, 0.3f, D - 0.1f, 0.9f, 0.91f);    // a lit desk
                break;
            case "desk":
                B(Top, -hw, hw, 0, 0.8f, 0.72f, 0.78f);
                B(Frame, -hw, -hw + 0.06f, 0.05f, 0.75f, 0, 0.72f);
                B(Frame, hw - 0.06f, hw, 0.05f, 0.75f, 0, 0.72f);
                B(Soft, -0.25f, 0.25f, 0.85f, 1.3f, 0.4f, 0.48f); // chair
                B(Frame, -0.04f, 0.04f, 1.03f, 1.11f, 0, 0.4f);
                B(Soft, -0.25f, 0.25f, 1.25f, 1.33f, 0.48f, 0.8f);
                break;
            case "mess_table":
                B(Top, -0.45f, 0.45f, 0, D, 0.72f, 0.77f);
                B(Frame, -0.08f, 0.08f, 0.2f, D - 0.2f, 0, 0.72f);
                foreach (var s in new[] { -1f, 1f })
                {
                    B(Soft, s > 0 ? 0.6f : -0.95f, s > 0 ? 0.95f : -0.6f, 0.1f, D - 0.1f, 0.4f, 0.46f);
                    B(Frame, s * 0.775f - 0.05f, s * 0.775f + 0.05f, 0.3f, D - 0.3f, 0, 0.4f);
                }
                break;
            case "counter":
                B(Frame, -hw, hw, 0, D - 0.05f, 0, 0.88f);
                B(Top, -hw, hw, 0, D, 0.88f, 0.95f);
                B(Frame, -hw, hw, 0, 0.05f, 0.95f, 1.6f);       // splashback
                B(Roles.Light, -hw + 0.1f, hw - 0.1f, 0.05f, 0.07f, 1.5f, 1.55f);
                B(Top, -hw + 0.3f, -hw + 0.8f, 0.1f, 0.5f, 0.95f, 1.05f);
                break;
            case "crates":
                B(Frame, -hw, hw - 0.05f, 0, D - 0.05f, 0, 0.62f);
                B(Roles.Hazard, -hw, hw - 0.05f, D - 0.05f, D - 0.04f, 0.5f, 0.56f);
                B(Frame, -hw + 0.1f, hw - 0.2f, 0.05f, D - 0.2f, 0.62f, H);
                break;
            case "workbench":
                B(Frame, -hw, hw, 0.05f, D, 0, 0.88f);
                B(Top, -hw, hw, 0, D, 0.88f, 0.95f);
                B(Frame, -hw, hw, 0, 0.04f, 0.95f, H);           // tool board
                for (var k = 0; k < 4; k++) B(Top, -hw + 0.3f + k * 0.4f, -hw + 0.4f + k * 0.4f, 0.04f, 0.1f, 1.25f, 1.5f);
                B(Roles.Light, -hw + 0.1f, hw - 0.1f, 0.04f, 0.06f, H - 0.08f, H - 0.03f);
                break;
            case "rack":
                foreach (var s in new[] { -1f, 1f }) B(Frame, s > 0 ? hw - 0.05f : -hw, s > 0 ? hw : -hw + 0.05f, 0, D, 0, H);
                foreach (var y in new[] { 0.05f, 0.7f, 1.35f, H - 0.05f }) B(Frame, -hw, hw, 0, D, y, y + 0.04f);
                for (var k = 0; k < 6; k++) B(Top, -hw + 0.15f + k * 0.24f, -hw + 0.2f + k * 0.24f, 0.1f, 0.4f, 0.75f, 1.3f);
                B(Roles.Hazard, -hw, hw, D - 0.01f, D, 0.05f, 0.09f);
                break;
            case "machine":
                B(Frame, -hw, hw, 0, D, 0, 0.3f);
                B(Top, -hw + 0.1f, hw - 0.1f, 0.1f, D - 0.1f, 0.3f, H - 0.3f);
                B(Frame, -hw + 0.2f, hw - 0.2f, 0.2f, D - 0.2f, H - 0.3f, H);
                B(Roles.Light, -hw + 0.3f, hw - 0.3f, D - 0.1f, D - 0.08f, 1.2f, 1.6f);
                B(Roles.Hazard, -hw, hw, D - 0.01f, D, 0.22f, 0.3f);
                break;
            case "sofa":
                B(Soft, -hw, hw, 0, D, 0, 0.45f);
                B(Soft, -hw, hw, 0, 0.25f, 0.45f, H);
                B(Soft, -hw, -hw + 0.15f, 0.25f, D, 0.45f, 0.62f);
                B(Soft, hw - 0.15f, hw, 0.25f, D, 0.45f, 0.62f);
                break;
            case "shelf":
                foreach (var s in new[] { -1f, 1f }) B(Frame, s > 0 ? hw - 0.04f : -hw, s > 0 ? hw : -hw + 0.04f, 0, D, 0, H);
                foreach (var y in new[] { 0.05f, 0.55f, 1.05f, 1.55f, H - 0.04f })
                {
                    B(Frame, -hw, hw, 0, D, y, y + 0.03f);
                    if (y < H - 0.1f) B(Top, -hw + 0.1f, -hw + 0.1f + (p.W - 0.3f) * (0.4f + 0.15f * ((int)(y * 10) % 4)), 0.05f, D - 0.08f, y + 0.03f, y + 0.33f);
                }
                break;
            case "bench":
                B(Frame, -hw, hw, 0, D, 0.38f, 0.45f);
                B(Frame, -hw + 0.1f, -hw + 0.2f, 0, D, 0, 0.38f);
                B(Frame, hw - 0.2f, hw - 0.1f, 0, D, 0, 0.38f);
                break;
        }
    }
}

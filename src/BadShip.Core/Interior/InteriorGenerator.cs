using System.Numerics;
using BadShip.Generation;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Interior;

public sealed record DeckInfo(int Index, float FloorY, float CeilingY, float HalfWidth, float Z0, float Z1, bool Narrow);
public sealed record RoomInfo(string Id, string Kind, int Deck, float[] Min, float[] Max);
public sealed record DoorInfo(string Id, string Kind, string A, string B, int Deck, float[] Center, float[] Normal, float Width, float Height);
public sealed record StairInfo(string Id, int FromDeck, int ToDeck, string Room, float[] Min, float[] Max);

/// <summary>A ship's interior: the walkable geometry (as a <see cref="Ship"/> of modules for the GLB writer) and the room graph.</summary>
public sealed class ShipInterior
{
    public required Ship Model { get; init; }
    public List<DeckInfo> Decks { get; } = [];
    public List<RoomInfo> Rooms { get; } = [];
    public List<DoorInfo> Doors { get; } = [];
    public List<StairInfo> Stairs { get; } = [];

    /// <summary>The hull cross-section the decks were fitted into (for the inside-the-hull check).</summary>
    public Profile Hull { get; init; }
}

/// <summary>
/// Walkable interiors that fit inside the hull. The longest constant section of the main hull is filled with a stack
/// of decks (3.4 m apart, 3.1 m clear), each as wide as the hull allows at both its floor and its ceiling, snapped to a
/// half-metre grid. A deck has a central corridor (or, on a freighter's lowest deck, a cargo hold) with rooms on both
/// sides; the bridge spans the front of the top deck and engineering the back of the lowest; a stairwell at the same
/// place on every deck carries steep ship's stairs between them; airlocks open in the hull wall of the middle deck.
/// Narrow hulls (small craft) get one deck of rooms in a row. Rooms come from the ship type (crew quarters, mess,
/// medbay, labs, armoury, passenger cabins, wards...). No furniture: rooms, walls, doors, stairs and lights.
/// Doors are separate nodes (type "door") with their rooms, size and slide axis in the extras, so an engine can turn
/// each into a working door; rooms are empty nodes (type "room") at their centres with their box in the extras.
/// Coordinates are the ship's, so the interior lines up with the exterior.
/// </summary>
public static class InteriorGenerator
{
    public const float DeckPitch = 3.4f, Slab = 0.3f, Clear = DeckPitch - Slab, Wall = 0.2f, DoorW = 1.2f, DoorH = 2.2f, HalfCorridor = 1f;
    public const string Floor = "interior_floor", WallMat = "interior_wall", Trim = "interior_trim", DoorMat = "interior_door";

    public static ShipInterior? Build(Ship ship)
    {
        var p = ship.Plan;
        if (p.Family == "fighter") return null; // a cockpit, not an interior
        var hull = ship.Find("hull_main") ?? ship.Modules.Where(m => m.Tier == ModuleTier.Primary && m.Lofts.Count > 0).OrderByDescending(m => m.Bounds.Size.Z).FirstOrDefault();
        if (hull is null || hull.Lofts.Count == 0) return null;
        var loft = hull.Lofts[0];
        // The longest segment whose cross-section stays the same.
        var best = -1;
        for (var s = 0; s < loft.Segments; s++)
        {
            if (loft.IsStep(s)) continue;
            var a = loft.Profiles[s];
            var b = loft.Profiles[s + 1];
            if (MathF.Abs(a.WBottom - b.WBottom) > 0.02f * a.WBottom || MathF.Abs(a.H - b.H) > 0.02f * a.H || MathF.Abs(a.Cy - b.Cy) > 0.05f) continue;
            if (best < 0 || loft.Zs[s + 1] - loft.Zs[s] > loft.Zs[best + 1] - loft.Zs[best]) best = s;
        }
        if (best < 0) return null;
        var P = loft.Profiles[best];
        var z0 = MathF.Ceiling(loft.Zs[best] + 1.5f);
        var z1 = MathF.Floor(loft.Zs[best + 1] - 1.5f);
        if (z1 - z0 < 8) return null;

        var r = Rng.Stream(ship.Recipe.Seed, "interior");
        var yLo = P.Bottom + MathF.Min(1.2f, P.H * 0.08f);
        var yHi = P.Top - MathF.Min(1f, P.H * 0.06f);
        var usable = yHi - yLo;
        if (usable < Clear + Slab) return null;
        var n = Math.Clamp((int)((usable - Slab) / DeckPitch), 1, 6);
        var stack = n * DeckPitch;
        var floor0 = yLo + (usable - stack) / 2 + Slab;

        var model = new Ship { Recipe = ship.Recipe, Plan = ship.Plan };
        model.Materials = new Dictionary<string, MaterialDef>(ship.Materials);
        model.Materials[Floor] = new MaterialDef(Floor, Palettes.Linear("#3b4046"), 0.85f, 0.3f, Vector3.Zero, 1);
        model.Materials[WallMat] = new MaterialDef(WallMat, Palettes.Linear("#8d939a"), 0.7f, 0.2f, Vector3.Zero, 1);
        model.Materials[Trim] = new MaterialDef(Trim, Palettes.Linear("#c7902a"), 0.6f, 0.3f, Vector3.Zero, 1);
        model.Materials[DoorMat] = new MaterialDef(DoorMat, Palettes.Linear("#56616b"), 0.5f, 0.5f, Vector3.Zero, 1);
        foreach (var (zone, hex) in new[] { ("command", "#3f5670"), ("crew", "#4d5747"), ("work", "#6a5434"), ("cargo", "#56493a"), ("medical", "#2f6b66") })
            model.Materials["zone_" + zone] = new MaterialDef("zone_" + zone, Palettes.Linear(hex), 0.8f, 0.2f, Vector3.Zero, 1);
        var result = new ShipInterior { Model = model, Hull = P };
        var root = Add(model, "interior", "interior", null);

        // Decks that are wide enough; narrow hulls get one deck of rooms in a row.
        for (var d = 0; d < n; d++)
        {
            var fy = floor0 + d * DeckPitch;
            var hw = MathF.Min(P.HalfWidthAt(fy - Slab), P.HalfWidthAt(fy + Clear)) - 0.6f;
            hw = MathF.Floor(hw * 2) / 2;
            if (hw < 1.5f) continue;
            var narrow = hw < 3.5f;
            if (narrow && result.Decks.Count > 0) continue;
            result.Decks.Add(new DeckInfo(result.Decks.Count, fy, fy + Clear, hw, z0, z1, narrow));
            if (narrow) break;
        }
        // A stack needs room for a proper stairwell on every deck (a flight, a walkway beside it and the corridor):
        // decks narrower than that at the bottom or top of a tapering hull are left out, keeping the longest run of
        // consecutive decks.
        if (result.Decks.Count > 1)
        {
            var best2 = new List<DeckInfo>();
            var run = new List<DeckInfo>();
            foreach (var d in result.Decks)
            {
                if (d.HalfWidth >= 4.5f && (run.Count == 0 || MathF.Abs(d.FloorY - run[^1].FloorY - DeckPitch) < 0.01f)) run.Add(d);
                else run = d.HalfWidth >= 4.5f ? [d] : [];
                if (run.Count > best2.Count) best2 = [.. run];
            }
            if (best2.Count == 0) best2 = [result.Decks.OrderByDescending(d => d.HalfWidth).First()];
            result.Decks.Clear();
            for (var i = 0; i < best2.Count; i++) result.Decks.Add(best2[i] with { Index = i });
        }
        if (result.Decks.Count == 0) return null;
        var plan = new Layout(ship, result, r);
        plan.Rooms();
        plan.Geometry(model, root);
        return result;
    }

    internal static Module Add(Ship model, string id, string type, string? parent)
    {
        var unique = id;
        for (var i = 2; model.Modules.Any(m => m.Id == unique); i++) unique = $"{id}_{i}";
        var m = new Module { Id = unique, Type = type, Tier = ModuleTier.Secondary, ParentId = parent };
        model.Modules.Add(m);
        return m;
    }

    internal static float[] A(Vector3 v) => [MathF.Round(v.X, 3), MathF.Round(v.Y, 3), MathF.Round(v.Z, 3)];

    /// <summary>Room planning and geometry for one ship.</summary>
    private sealed class Layout(Ship ship, ShipInterior result, Rng r)
    {
        private readonly Dictionary<string, (Aabb box, int deck)> _rooms = [];
        // Per deck: the hub (corridor or hold) that side rooms open onto.
        private readonly Dictionary<int, string> _hub = [];
        // Per deck: the stretch of the hub between the full-width rooms (where side rooms and the airlock go).
        private readonly Dictionary<int, (float a, float b)> _span = [];
        private int _door;
        private float _stairZ0;
        private int _airlockDeck;
        private readonly List<float> _cross = [];
        // The stairwell: a landing at the foot, a 3.4 m flight, and a landing at the top.
        private const float StairLen = 7f;
        private const float StairFoot = 2f, StairRun = 3.4f;

        private string Kind => ship.Plan.Archetype;

        private void Room(string id, string kind, int deck, float x0, float x1, float z0, float z1)
        {
            var d = result.Decks[deck];
            var box = new Aabb(new Vector3(x0, d.FloorY, z0), new Vector3(x1, d.CeilingY, z1));
            _rooms[id] = (box, deck);
            result.Rooms.Add(new RoomInfo(id, kind, deck, A(box.Min), A(box.Max)));
        }

        private void Door(string kind, string a, string b, int deck, Vector3 center, Vector3 normal, float width = DoorW)
        {
            var id = kind == "opening" ? $"opening_{_door++}" : $"door_{_door++}";
            result.Doors.Add(new DoorInfo(id, kind, a, b, deck, A(center), A(normal), width, kind == "opening" ? Clear - 0.4f : DoorH));
        }

        public void Rooms()
        {
            var decks = result.Decks;
            var top = decks.Count - 1;
            var mid = decks.Count / 2;
            var dz0 = decks[0].Z0;
            var dz1 = decks[0].Z1;
            var len = dz1 - dz0;
            if (decks[0].Narrow) { NarrowDeck(decks[0]); return; }

            var bridgeLen = Num.Clamp(MathF.Round(len * 0.15f), 5, 14);
            var engLen = Num.Clamp(MathF.Round(len * 0.2f), 6, 20);
            // Keep room between the bridge and engineering: six metres of corridor on a single deck, and on a stack the
            // stairwell (which is on every deck) must fit between engineering below and the bridge on top.
            var need = decks.Count == 1 ? 6f : StairLen + 4;
            if (len - bridgeLen - engLen < need)
            {
                bridgeLen = MathF.Max(3, MathF.Floor((len - need) / 2));
                engLen = MathF.Max(3, MathF.Floor((len - need) / 2));
            }
            _stairZ0 = Num.Clamp(MathF.Round(dz0 + len * 0.45f), dz0 + engLen + 0.5f, dz1 - bridgeLen - StairLen - 0.5f);
            var hold = Kind is "freighter" or "industrial" or "raider" && decks.Count >= 3;
            var program = new Program(Kind, r);
            // The free stretch of every deck (between engineering and the bridge), then the airlock's deck: the longest
            // stretch that is not the cargo hold, the middle deck on a tie.
            foreach (var d in decks)
                _span[d.Index] = (d.Z0 + (d.Index == 0 ? engLen : 0), d.Z1 - (d.Index == top ? bridgeLen : 0));
            _airlockDeck = decks.Where(d => !(hold && d.Index == 0))
                .OrderByDescending(d => _span[d.Index].b - _span[d.Index].a).ThenBy(d => Math.Abs(d.Index - mid)).First().Index;
            // Cross-corridors every 24–32 m, at the same places on every deck, clear of the stairwell and the airlock.
            var spacing = r.Range(24f, 32f);
            var lo = decks.Max(d => _span[d.Index].a);
            var hi = decks.Min(d => _span[d.Index].b);
            for (var z = lo + spacing * 0.6f; z < hi - 6; z += spacing)
            {
                var zc = MathF.Round(z);
                if (zc + 2 > _stairZ0 - 1 && zc < _stairZ0 + StairLen + 1) continue;
                _cross.Add(zc);
            }
            foreach (var d in decks)
            {
                var hw = d.HalfWidth;
                var za = d.Z0;
                var zb = d.Z1;
                if (d.Index == top)
                {
                    Room($"bridge", "bridge", d.Index, -hw, hw, zb - bridgeLen, zb);
                    zb -= bridgeLen;
                }
                if (d.Index == 0)
                {
                    Room($"engineering", "engineering", d.Index, -hw, hw, za, za + engLen);
                    za += engLen;
                }
                // Stairwell at the same place on every deck (left side).
                if (decks.Count > 1) Room($"stairs_{d.Index}", "stairwell", d.Index, -hw, -HalfCorridor, _stairZ0, _stairZ0 + StairLen);
                var hubId = d.Index == 0 && hold ? "cargo_hold" : $"corridor_{d.Index}";
                if (hubId == "cargo_hold")
                {
                    // The hold fills the deck around the stairwell; the stairwell opens straight into it.
                    Room(hubId, "cargo_hold", d.Index, -hw, hw, za, zb);
                    _hub[d.Index] = hubId;
                    if (decks.Count > 1) Door("interior", $"stairs_{d.Index}", hubId, d.Index, new Vector3(-HalfCorridor, d.FloorY, _stairZ0 + StairLen / 2), Vector3.UnitX);
                }
                else
                {
                    Room(hubId, "corridor", d.Index, -HalfCorridor, HalfCorridor, za, zb);
                    _hub[d.Index] = hubId;
                    foreach (var s in new[] { 1f, -1f })
                        SideRooms(d, s, za, zb, program);
                }
                // Full-width rooms open onto the hub through their end walls.
                if (d.Index == top) Door("interior", "bridge", hubId, d.Index, new Vector3(0, d.FloorY, zb), -Vector3.UnitZ);
                if (d.Index == 0) Door("interior", "engineering", hubId, d.Index, new Vector3(0, d.FloorY, za), Vector3.UnitZ);
            }
            // Stairs between consecutive decks, from each stairwell up into the next.
            for (var i = 0; i + 1 < decks.Count; i++)
            {
                var d = decks[i];
                // Against the hull wall of the narrower of the two decks (faceted hulls change width deck by deck), so
                // the flight lands inside both, leaving a walkway beside it from the door to both landings.
                var hw = MathF.Min(d.HalfWidth, decks[i + 1].HalfWidth);
                var w = Num.Clamp((hw - HalfCorridor) * 0.45f, 1.4f, 1.8f);
                var min = new Vector3(-hw + 0.15f, d.FloorY, _stairZ0 + StairFoot);
                var max = new Vector3(-hw + 0.15f + w, decks[i + 1].FloorY, _stairZ0 + StairFoot + StairRun);
                result.Stairs.Add(new StairInfo($"stair_{i}", i, i + 1, $"stairs_{i}", A(min), A(max)));
            }
            // Airlocks in the hull wall of the middle deck.
            Airlock(decks[_airlockDeck]);
        }

        /// <summary>Rooms along one side strip of a deck, between z0 and z1, skipping the stairwell and the airlock.</summary>
        private void SideRooms(DeckInfo d, float side, float z0, float z1, Program program)
        {
            var x0 = side > 0 ? HalfCorridor : -d.HalfWidth;
            var x1 = side > 0 ? d.HalfWidth : -HalfCorridor;
            var gaps = new List<(float a, float b)>();
            if (side < 0 && result.Decks.Count > 1) gaps.Add((_stairZ0, _stairZ0 + StairLen));
            if (side > 0 && d.Index == _airlockDeck) gaps.Add((AirlockZ(d), AirlockZ(d) + 3));
            foreach (var cz in _cross)
            {
                if (cz < z0 || cz + 2 > z1) continue;
                if (gaps.Any(g => cz + 2 > g.a && cz < g.b)) continue;
                gaps.Add((cz, cz + 2));
                var id = $"cross_{d.Index}_{cz:0}_{(side > 0 ? "p" : "s")}";
                Room(id, "corridor", d.Index, x0, x1, cz, cz + 2);
                Door("opening", id, _hub[d.Index], d.Index, new Vector3(side * HalfCorridor, d.FloorY, cz + 1), new Vector3(side, 0, 0), 2f);
            }
            var cursor = z0;
            var spans = new List<(float a, float b)>();
            foreach (var (ga, gb) in gaps.OrderBy(g => g.a))
            {
                // Gaps are clipped to this strip: one beyond its ends must not stretch the rooms.
                var a = Num.Clamp(ga, z0, z1);
                var b = Num.Clamp(gb, z0, z1);
                if (a > cursor) spans.Add((cursor, a));
                cursor = MathF.Max(cursor, b);
            }
            if (z1 > cursor) spans.Add((cursor, z1));
            foreach (var (a, b) in spans)
            {
                var z = a;
                while (b - z >= 2.5f)
                {
                    var (kind, lmin, lmax) = program.Next(d.Index, result.Decks.Count);
                    var l = MathF.Round(r.Range(lmin, lmax));
                    if (b - (z + l) < 2.5f) l = b - z;
                    var id = $"{kind}_{_rooms.Count}";
                    Room(id, kind, d.Index, x0, x1, z, z + l);
                    Door("interior", id, _hub[d.Index], d.Index, new Vector3(side * HalfCorridor, d.FloorY, z + MathF.Min(l / 2, MathF.Max(1, l / 2))), new Vector3(side, 0, 0));
                    z += l;
                }
            }
            if (side < 0 && result.Decks.Count > 1)
                Door("interior", $"stairs_{d.Index}", _hub[d.Index], d.Index, new Vector3(-HalfCorridor, d.FloorY, _stairZ0 + StairLen / 2), -Vector3.UnitX);
        }

        private float AirlockZ(DeckInfo d)
        {
            var (a, b) = _span.TryGetValue(d.Index, out var sp) ? sp : (d.Z0, d.Z1);
            var z = MathF.Round(a + (b - a) * 0.68f);
            // Clear of the stairwell, and inside the free stretch.
            if (z < _stairZ0 + StairLen + 0.5f && z + 3 > _stairZ0 - 0.5f) z = _stairZ0 + StairLen + 1;
            return Num.Clamp(z, a, b - 3);
        }

        private void Airlock(DeckInfo d)
        {
            if (d.Narrow) return;
            var z = AirlockZ(d);
            Room("airlock", "airlock", d.Index, HalfCorridor, d.HalfWidth, z, z + 3);
            Door("interior", "airlock", _hub[d.Index], d.Index, new Vector3(HalfCorridor, d.FloorY, z + 1.5f), Vector3.UnitX);
            Door("airlock_outer", "airlock", "exterior", d.Index, new Vector3(d.HalfWidth, d.FloorY, z + 1.5f), Vector3.UnitX);
        }

        /// <summary>A narrow hull: one row of rooms from the back to the front, each opening into the next.</summary>
        private void NarrowDeck(DeckInfo d)
        {
            var hw = d.HalfWidth;
            var len = d.Z1 - d.Z0;
            string[] kinds = Kind switch
            {
                "utility" => ship.Plan.Payload switch
                {
                    "shuttle" => ["engine_room", "passenger_cabin", "passenger_cabin", "cockpit"],
                    "mule" => ["engine_room", "cargo_hold", "cockpit"],
                    _ => ["engine_room", "cargo_hold", "crew_quarters", "cockpit"],
                },
                _ => ["engineering", "cargo_hold", "crew_quarters", "bridge"],
            };
            var parts = kinds.Length;
            var z = d.Z0;
            string? prev = null;
            for (var i = 0; i < parts; i++)
            {
                var l = i == parts - 1 ? d.Z1 - z : MathF.Max(2.5f, MathF.Round(len / parts));
                if (z + l > d.Z1) l = d.Z1 - z;
                if (l < 2) break;
                var id = $"{kinds[i]}_{i}";
                Room(id, kinds[i], d.Index, -hw, hw, z, z + l);
                if (prev != null) Door("interior", prev, id, d.Index, new Vector3(0, d.FloorY, z), Vector3.UnitZ);
                if (i == 1) Door("airlock_outer", id, "exterior", d.Index, new Vector3(hw, d.FloorY, z + l / 2), Vector3.UnitX);
                prev = id;
                z += l;
            }
        }

        // ------------------------------------------------------------------ geometry

        public void Geometry(Ship model, Module root)
        {
            foreach (var d in result.Decks)
            {
                var deck = Add(model, $"deck_{d.Index}", "deck", root.Id);
                deck.Extras = new() { ["deck"] = d.Index, ["floor_y"] = d.FloorY };
                var hw = d.HalfWidth;
                // Floor (with a hole where the stairs from the deck below arrive) and, over the top deck, a roof.
                // The floor above a flight is open over its whole length, so nobody bumps their head on the way up.
                var holes = result.Stairs.Where(s => s.ToDeck == d.Index).Select(s => (s.Min[0] - 0.05f, s.Max[0] + 0.05f, s.Min[2] - 0.1f, s.Max[2])).ToList();
                SlabWithHoles(deck.Mesh, Floor, -hw - Wall, hw + Wall, d.Z0 - Wall, d.Z1 + Wall, d.FloorY - Slab, d.FloorY, holes);
                if (d.Index == result.Decks.Count - 1 || result.Decks.Count == 1)
                {
                    var roof = Add(model, $"ceiling_{d.Index}", "ceiling", deck.Id);
                    Shapes.BoxMinMax(roof.Mesh, WallMat, new Vector3(-hw - Wall, d.CeilingY, d.Z0 - Wall), new Vector3(hw + Wall, d.CeilingY + Slab, d.Z1 + Wall));
                }
                // Walls: build every room's outline once, with door openings cut where doors are.
                var walls = Add(model, $"walls_{d.Index}", "walls", deck.Id);
                var doors = result.Doors.Where(x => x.Deck == d.Index).ToList();
                // Every room edge goes onto a wall line; overlapping pieces on a line merge into one run, so shared
                // walls are built once.
                var lines = new Dictionary<(char axis, float at), List<(float a, float b)>>();
                void Line(char axis, float at, float a, float b)
                {
                    var key = (axis, MathF.Round(at * 100) / 100);
                    if (!lines.TryGetValue(key, out var list)) lines[key] = list = [];
                    list.Add((a, b));
                }
                foreach (var room in result.Rooms.Where(x => x.Deck == d.Index))
                {
                    var (mn, mx) = (room.Min, room.Max);
                    Line('z', mn[0], mn[2], mx[2]);
                    Line('z', mx[0], mn[2], mx[2]);
                    Line('x', mn[2], mn[0], mx[0]);
                    Line('x', mx[2], mn[0], mx[0]);
                }
                foreach (var ((axis, at), pieces) in lines.OrderBy(kv => kv.Key.axis).ThenBy(kv => kv.Key.at))
                {
                    var merged = new List<(float a, float b)>();
                    foreach (var (a, b) in pieces.OrderBy(x => x.a))
                    {
                        if (merged.Count > 0 && a <= merged[^1].b + 0.01f) merged[^1] = (merged[^1].a, MathF.Max(merged[^1].b, b));
                        else merged.Add((a, b));
                    }
                    foreach (var (a, b) in merged)
                    {
                        if (axis == 'z') WallAlongZ(walls.Mesh, at, a, b, d, doors);
                        else WallAlongX(walls.Mesh, at, a, b, d, doors);
                    }
                }
                // Rooms: empty nodes at their centres, holding their ceiling lights.
                foreach (var room in result.Rooms.Where(x => x.Deck == d.Index))
                {
                    var box = new Aabb(new Vector3(room.Min[0], room.Min[1], room.Min[2]), new Vector3(room.Max[0], room.Max[1], room.Max[2]));
                    var m = Add(model, $"room_{room.Id}", "room", deck.Id);
                    m.Pivot = new Vector3(box.Center.X, d.FloorY, box.Center.Z);
                    m.Extras = new() { ["kind"] = room.Kind, ["deck"] = d.Index, ["min"] = room.Min, ["max"] = room.Max };
                    Lights(m.Mesh, box, room.Kind);
                    if (Zone(room.Kind) is { } zone && box.Size.X > 1.2f && box.Size.Z > 1.2f)
                        Shapes.BoxMinMax(m.Mesh, "zone_" + zone, new Vector3(box.Min.X + 0.35f, d.FloorY, box.Min.Z + 0.35f), new Vector3(box.Max.X - 0.35f, d.FloorY + 0.012f, box.Max.Z - 0.35f));
                    if (room.Kind == "corridor")
                        foreach (var s in new[] { -1f, 1f })
                            Shapes.BoxMinMax(m.Mesh, Trim, new Vector3(s * 0.75f - 0.04f, d.FloorY, box.Min.Z + 0.2f), new Vector3(s * 0.75f + 0.04f, d.FloorY + 0.01f, box.Max.Z - 0.2f));
                    model.Markers.Add(new Marker($"room_{room.Id}", "room", m.Id, m.Pivot.Value, Vector3.UnitZ, MathF.Min(box.Size.X, box.Size.Z) / 2, 0));
                }
                // Door leaves: one node per door, centred in its opening.
                foreach (var door in doors.Where(x => x.Kind != "opening"))
                {
                    var c = new Vector3(door.Center[0], door.Center[1], door.Center[2]);
                    var nrm = new Vector3(door.Normal[0], door.Normal[1], door.Normal[2]);
                    var alongX = MathF.Abs(nrm.Z) > 0.5f; // the door spans X when its wall runs along X
                    var m = Add(model, door.Id, "door", deck.Id);
                    var leafC = c + new Vector3(0, DoorH / 2, 0);
                    m.Pivot = leafC;
                    var size = alongX ? new Vector3(DoorW - 0.04f, DoorH - 0.02f, 0.12f) : new Vector3(0.12f, DoorH - 0.02f, DoorW - 0.04f);
                    Shapes.Box(m.Mesh, door.Kind == "airlock_outer" ? Roles.Hazard : DoorMat, leafC, size);
                    // A light strip over the door.
                    Shapes.Box(m.Mesh, Roles.Light, c + new Vector3(0, DoorH + 0.15f, 0) + nrm * 0.12f, alongX ? new Vector3(DoorW * 0.6f, 0.08f, 0.04f) : new Vector3(0.04f, 0.08f, DoorW * 0.6f));
                    m.Extras = new()
                    {
                        ["kind"] = door.Kind, ["rooms"] = new[] { door.A, door.B }, ["width"] = DoorW, ["height"] = DoorH,
                        ["slide_axis"] = alongX ? "x" : "z", ["opens"] = "slide",
                    };
                    model.Markers.Add(new Marker(door.Id, door.Kind == "airlock_outer" ? "airlock" : "door", m.Id, c, nrm, DoorW / 2, 0));
                }
                // Stairs from this deck up.
                foreach (var st in result.Stairs.Where(s => s.FromDeck == d.Index))
                {
                    var m = Add(model, st.Id, "stairs", deck.Id);
                    m.Extras = new() { ["from_deck"] = st.FromDeck, ["to_deck"] = st.ToDeck };
                    var steps = (int)MathF.Ceiling(DeckPitch / 0.25f);
                    var run = (st.Max[2] - st.Min[2]) / steps;
                    var rise = (st.Max[1] - st.Min[1]) / steps;
                    for (var k = 0; k < steps; k++)
                        Shapes.BoxMinMax(m.Mesh, k % 4 == 3 ? Trim : Floor,
                            new Vector3(st.Min[0], st.Min[1] + k * rise, st.Min[2] + k * run),
                            new Vector3(st.Max[0], st.Min[1] + (k + 1) * rise, st.Min[2] + (k + 1) * run));
                    // A rail on the open side: its own node, so an engine can give it collision without treating it as floor.
                    var rail = Add(model, st.Id + "_rail", "railing", m.Id);
                    Shapes.Beam(rail.Mesh, Trim, new Vector3(st.Max[0], st.Min[1] + 1f, st.Min[2]), new Vector3(st.Max[0], st.Max[1] + 1f, st.Max[2]), 0.06f);
                }
            }
            model.Markers.Add(new Marker("spawn_0", "spawn", "interior", SpawnPoint(), Vector3.UnitZ, 0.5f, 0));
            model.Finish();
        }

        private Vector3 SpawnPoint()
        {
            var room = result.Rooms.FirstOrDefault(x => x.Kind == "corridor") ?? result.Rooms[0];
            return new Vector3((room.Min[0] + room.Max[0]) / 2, room.Min[1] + 0.05f, (room.Min[2] + room.Max[2]) / 2);
        }

        private static void Lights(Mesh m, Aabb box, string kind)
        {
            var y = box.Max.Y - 0.06f;
            if (box.Size.Z >= box.Size.X)
            {
                var n = Math.Max(1, (int)(box.Size.Z / 4.5f));
                for (var i = 0; i < n; i++)
                {
                    var z = box.Min.Z + (i + 0.5f) * box.Size.Z / n;
                    Shapes.Box(m, Roles.Light, new Vector3(box.Center.X, y, z), new Vector3(MathF.Min(0.4f, box.Size.X * 0.3f), 0.06f, MathF.Min(1.6f, box.Size.Z / n * 0.5f)));
                }
            }
            else Shapes.Box(m, Roles.Light, new Vector3(box.Center.X, y, box.Center.Z), new Vector3(MathF.Min(2f, box.Size.X * 0.4f), 0.06f, 0.4f));
        }

        private static void SlabWithHoles(Mesh m, string mat, float x0, float x1, float z0, float z1, float y0, float y1, List<(float x0, float x1, float z0, float z1)> holes)
        {
            if (holes.Count == 0) { Shapes.BoxMinMax(m, mat, new Vector3(x0, y0, z0), new Vector3(x1, y1, z1)); return; }
            // One hole is enough here (one stair arrives per deck): four pieces round it.
            var h = holes[0];
            Shapes.BoxMinMax(m, mat, new Vector3(x0, y0, z0), new Vector3(x1, y1, h.z0));
            Shapes.BoxMinMax(m, mat, new Vector3(x0, y0, h.z1), new Vector3(x1, y1, z1));
            if (h.x0 > x0) Shapes.BoxMinMax(m, mat, new Vector3(x0, y0, h.z0), new Vector3(h.x0, y1, h.z1));
            if (h.x1 < x1) Shapes.BoxMinMax(m, mat, new Vector3(h.x1, y0, h.z0), new Vector3(x1, y1, h.z1));
        }

        /// <summary>A wall at x = <paramref name="x"/> running along Z from z0 to z1, with openings for the doors in it.</summary>
        private static void WallAlongZ(Mesh m, float x, float z0, float z1, DeckInfo d, List<DoorInfo> doors)
        {
            var openings = doors.Where(o => MathF.Abs(o.Center[0] - x) < 0.05f && MathF.Abs(o.Normal[0]) > 0.5f && o.Center[2] > z0 && o.Center[2] < z1)
                .Select(o => (c: o.Center[2], w: o.Width, h: o.Height, frame: o.Kind != "opening")).OrderBy(v => v.c).ToList();
            Segments(z0, z1, openings, (a, b, lintel) =>
            {
                var y0 = lintel > 0 ? d.FloorY + lintel : d.FloorY;
                Shapes.BoxMinMax(m, WallMat, new Vector3(x - Wall / 2, y0, a), new Vector3(x + Wall / 2, d.CeilingY, b));
            }, (c) => Frame(m, new Vector3(x, d.FloorY, c), alongX: false));
        }

        /// <summary>A wall at z = <paramref name="z"/> running along X from x0 to x1, with openings for the doors in it.</summary>
        private static void WallAlongX(Mesh m, float z, float x0, float x1, DeckInfo d, List<DoorInfo> doors)
        {
            var openings = doors.Where(o => MathF.Abs(o.Center[2] - z) < 0.05f && MathF.Abs(o.Normal[2]) > 0.5f && o.Center[0] > x0 && o.Center[0] < x1)
                .Select(o => (c: o.Center[0], w: o.Width, h: o.Height, frame: o.Kind != "opening")).OrderBy(v => v.c).ToList();
            Segments(x0, x1, openings, (a, b, lintel) =>
            {
                var y0 = lintel > 0 ? d.FloorY + lintel : d.FloorY;
                Shapes.BoxMinMax(m, WallMat, new Vector3(a, y0, z - Wall / 2), new Vector3(b, d.CeilingY, z + Wall / 2));
            }, (c) => Frame(m, new Vector3(c, d.FloorY, z), alongX: true));
        }

        /// <summary>Wall pieces round the openings (a lintel over each), and a frame round each real door.</summary>
        private static void Segments(float a, float b, List<(float c, float w, float h, bool frame)> openings, Action<float, float, float> piece, Action<float> frame)
        {
            var cursor = a;
            foreach (var (c, w, h, framed) in openings)
            {
                var o0 = c - w / 2;
                var o1 = c + w / 2;
                if (o0 > cursor + 0.01f) piece(cursor, o0, 0);
                piece(MathF.Max(o0, a), MathF.Min(o1, b), h);
                if (framed) frame(c);
                cursor = MathF.Max(cursor, o1);
            }
            if (b > cursor + 0.01f) piece(cursor, b, 0);
        }

        /// <summary>The floor tint of a room kind: command, crew, work, cargo or medical (corridors keep the bare floor).</summary>
        private static string? Zone(string kind) => kind switch
        {
            "bridge" or "comms" or "briefing" or "captain_cabin" or "observation" or "cockpit" => "command",
            "crew_quarters" or "passenger_cabin" or "mess" or "galley" or "lounge" => "crew",
            "engineering" or "engine_room" or "reactor" or "workshop" or "life_support" or "armoury" or "brig" or "lab" => "work",
            "cargo_hold" or "storage" => "cargo",
            "medbay" => "medical",
            "airlock" => "work",
            _ => null,
        };

        private static void Frame(Mesh m, Vector3 c, bool alongX)
        {
            var t = 0.08f;
            var depth = Wall + 0.08f;
            Vector3 S(float along, float h, float thick) => alongX ? new Vector3(along, h, thick) : new Vector3(thick, h, along);
            Vector3 O(float along) => alongX ? new Vector3(along, 0, 0) : new Vector3(0, 0, along);
            Shapes.Box(m, Trim, c + O(-DoorW / 2 - t / 2) + new Vector3(0, DoorH / 2, 0), S(t, DoorH, depth));
            Shapes.Box(m, Trim, c + O(DoorW / 2 + t / 2) + new Vector3(0, DoorH / 2, 0), S(t, DoorH, depth));
            Shapes.Box(m, Trim, c + new Vector3(0, DoorH + t / 2, 0), S(DoorW + t * 2, t, depth));
        }
    }

    /// <summary>Which rooms a ship type needs, deck by deck: command up top, crew in the middle, machinery and stores low.</summary>
    private sealed class Program(string archetype, Rng r)
    {
        private bool _mess, _med;

        public (string kind, float lmin, float lmax) Next(int deck, int decks)
        {
            var low = deck == 0 && decks > 1;
            var high = deck == decks - 1 && decks > 1;
            if (!_mess && !low) { _mess = true; return ("mess", 8, 12); }
            if (!_med && !low) { _med = true; return archetype == "civilian" ? ("medbay", 8, 12) : ("medbay", 6, 9); }
            (string, double)[] pool = archetype switch
            {
                "research" or "ring" => [("lab", 3), ("crew_quarters", 3), ("storage", 1), ("life_support", 1), ("observation", 1)],
                "patrol" or "warship" or "citadel" => [("crew_quarters", 4), ("armoury", 1.5), ("brig", 0.7), ("briefing", 1), ("storage", 1), ("life_support", 1)],
                "civilian" => [("passenger_cabin", 5), ("lounge", 1.5), ("galley", 1), ("crew_quarters", 1.5), ("storage", 1)],
                "raider" => [("crew_quarters", 3), ("armoury", 1.5), ("storage", 2), ("brig", 1), ("workshop", 1)],
                "industrial" => [("crew_quarters", 3), ("workshop", 2), ("storage", 2), ("life_support", 1)],
                _ => [("crew_quarters", 3), ("storage", 2), ("galley", 1), ("workshop", 1), ("life_support", 1)],
            };
            if (low) pool = [("storage", 3), ("workshop", 2), ("life_support", 2), ("reactor", 1.5)];
            if (high) pool = [.. pool, ("captain_cabin", 1), ("comms", 1.5), ("briefing", 1)];
            var kind = r.Weighted(pool);
            return kind switch
            {
                "crew_quarters" or "passenger_cabin" or "brig" => (kind, 4, 6),
                "captain_cabin" => (kind, 6, 8),
                "lounge" or "lab" or "workshop" or "storage" => (kind, 7, 12),
                _ => (kind, 5, 8),
            };
        }
    }
}

using System.Numerics;
using BadShip.Generation;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Interior;

public sealed record DeckInfo(int Index, float FloorY, float CeilingY, float HalfWidth, float Z0, float Z1, bool Narrow);
public sealed record RoomInfo(string Id, string Kind, int Deck, float[] Min, float[] Max);
public sealed record DoorInfo(string Id, string Kind, string A, string B, int Deck, float[] Center, float[] Normal, float Width, float Height);
/// <summary>A flight of stairs from a room on one deck up to the next; <c>ToRoom</c> names the room it arrives in when that is
/// not the stairwell above (a grand staircase up to a hall's gallery).</summary>
public sealed record StairInfo(string Id, int FromDeck, int ToDeck, string Room, float[] Min, float[] Max, string? ToRoom = null);

/// <summary>A window in the hull wall of a room: side +1 (port) or −1 (starboard), along Z from z0 to z1, sill to head y0–y1.</summary>
public sealed record WindowInfo(string Id, string Room, int Deck, int Side, float Z0, float Z1, float Y0, float Y1, float WallX);

/// <summary>A ship's interior: the walkable geometry (as a <see cref="Ship"/> of modules for the GLB writer) and the room graph.</summary>
public sealed class ShipInterior
{
    public required Ship Model { get; init; }
    public List<DeckInfo> Decks { get; } = [];
    public List<RoomInfo> Rooms { get; } = [];
    public List<DoorInfo> Doors { get; } = [];
    public List<StairInfo> Stairs { get; } = [];
    public List<WindowInfo> Windows { get; } = [];

    /// <summary>The hull cross-section the decks were fitted into (for the inside-the-hull check).</summary>
    public Profile Hull { get; init; }
}

/// <summary>
/// Walkable interiors that fit inside the hull. The longest constant section of the main hull is filled with a stack
/// of decks (3.4 m apart, 3.1 m clear), each as wide as the hull allows at both its floor and its ceiling, snapped to a
/// half-metre grid. A deck has a central corridor (or, on a freighter's lowest deck, a cargo hold) with rooms on both
/// sides; the bridge spans the front of the top deck and engineering the back of the lowest; a stairwell at the same
/// place on every deck carries steep ship's stairs between them; airlocks open in the hull wall of the middle deck.
/// Bigger ships get more complex layouts: a hall two decks tall (an atrium, hangar or cargo bay) with a gallery round a
/// void on the deck above and a grand staircase up to it; a ring corridor round a central room (a lounge, lab or
/// operations room) with rooms outside the ring; and suites, where a deep room opens into a second one behind it
/// (medbay and ward, lab and clean room, the captain's office and cabin).
/// Narrow hulls (small craft) get one deck of rooms in a row. Rooms come from the ship type (crew quarters, mess,
/// medbay, labs, armoury, passenger cabins, wards...). No furniture: rooms, walls, doors, stairs and lights.
/// Doors are separate nodes (type "door") with their rooms, size and slide axis in the extras, so an engine can turn
/// each into a working door; rooms are empty nodes (type "room") at their centres with their box in the extras.
/// Coordinates are the ship's, so the interior lines up with the exterior.
/// </summary>
public static class InteriorGenerator
{
    public const float DeckPitch = 3.4f, Slab = 0.3f, Clear = DeckPitch - Slab, Wall = 0.2f, DoorW = 1.4f, DoorH = 2.2f, HalfCorridor = 1f;
    public const string Floor = "interior_floor", WallMat = "interior_wall", Trim = "interior_trim", DoorMat = "interior_door";

    public const float WindowW = 1.4f, Sill = 1.0f, WindowH = 1.2f;

    /// <summary>
    /// Builds the interior. With <paramref name="acceptWindow"/>, rooms along the hull walls get windows: each
    /// candidate is offered to the callback (which checks the hull can take an opening there, and reserves it) and
    /// only accepted ones are cut into the interior's outer walls.
    /// </summary>
    public static ShipInterior? Build(Ship ship, Func<WindowInfo, bool>? acceptWindow = null)
    {
        var p = ship.Plan;
        if (p.Family == "fighter") return null; // a cockpit, not an interior
        var hull = Hull(ship);
        if (hull is null || hull.Lofts.Count == 0) return null;
        var loft = hull.Lofts[0];
        var best = BodySegment(loft);
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
        foreach (var (zone, hex) in new[] { ("command", "#3f5670"), ("crew", "#4d5747"), ("work", "#6a5434"), ("cargo", "#56493a"), ("medical", "#2f6b66"), ("plaza", "#5b4a6e") })
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
        if (acceptWindow != null) plan.Windows(acceptWindow);
        plan.Geometry(model, root);
        return result;
    }

    /// <summary>The hull the interior goes into, and the longest loft segment whose cross-section stays the same.</summary>
    public static Module? Hull(Ship ship) => ship.Find("hull_main") ?? ship.Modules.Where(m => m.Tier == ModuleTier.Primary && m.Lofts.Count > 0).OrderByDescending(m => m.Bounds.Size.Z).FirstOrDefault();

    public static int BodySegment(Loft loft)
    {
        var best = -1;
        for (var s = 0; s < loft.Segments; s++)
        {
            if (loft.IsStep(s)) continue;
            var a = loft.Profiles[s];
            var b = loft.Profiles[s + 1];
            if (MathF.Abs(a.WBottom - b.WBottom) > 0.02f * a.WBottom || MathF.Abs(a.H - b.H) > 0.02f * a.H || MathF.Abs(a.Cy - b.Cy) > 0.05f) continue;
            if (best < 0 || loft.Zs[s + 1] - loft.Zs[s] > loft.Zs[best + 1] - loft.Zs[best]) best = s;
        }
        return best;
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
        private List<WindowInfo> _windows => result.Windows;
        // Per deck: the hub (corridor or hold) that side rooms open onto.
        private readonly Dictionary<int, string> _hub = [];
        // Per deck: the stretch of the hub between the full-width rooms (where side rooms and the airlock go).
        private readonly Dictionary<int, (float a, float b)> _span = [];
        private int _door;
        private float _stairZ0;
        // A deck's stairwell runs from its outer wall to the corridor: as wide as the deck allows, at most 6 m.
        private static float StairX(DeckInfo d) => -HalfCorridor - MathF.Min(d.HalfWidth - HalfCorridor, 6f);
        private int _airlockDeck;
        private readonly List<float> _cross = [];
        // Per deck: the corridor runs (a corridor is cut into runs by halls and rings), and the special sections.
        private readonly Dictionary<int, List<(float a, float b, string id)>> _runs = [];
        private readonly Dictionary<int, List<Section>> _sections = [];
        // Per deck: voids in the floor over a hall below (the gallery walks round them).
        private readonly Dictionary<int, List<(float x0, float x1, float z0, float z1)>> _voids = [];
        private const float Gallery = 1.6f, GrandRun = 6f;

        /// <summary>A special stretch of a deck: a ring corridor round a central room, or a hall (on the lower deck)
        /// and its gallery (on the deck above).</summary>
        private sealed record Section(string Mode, float A, float B, float C, string Kind);
        // The stairwell: a landing at the foot, a 3.4 m flight, and a landing at the top.
        private const float StairLen = 7f;
        private const float StairFoot = 2f, StairRun = 3.4f;

        private string Kind => ship.Plan.Archetype;

        private void Room(string id, string kind, int deck, float x0, float x1, float z0, float z1, float? top = null)
        {
            var d = result.Decks[deck];
            var box = new Aabb(new Vector3(x0, d.FloorY, z0), new Vector3(x1, top ?? d.CeilingY, z1));
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
            PlanSections(hold);
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
                if (decks.Count > 1)
                {
                    var sx = StairX(d);
                    Room($"stairs_{d.Index}", "stairwell", d.Index, sx, -HalfCorridor, _stairZ0, _stairZ0 + StairLen);
                    // Beyond a capped stairwell, a store opening off the foot landing.
                    if (sx - -hw >= 2.5f && !(hold && d.Index == 0))
                    {
                        var store = $"storage_{_rooms.Count}";
                        Room(store, "storage", d.Index, -hw, sx, _stairZ0, _stairZ0 + StairLen);
                        Door("interior", store, $"stairs_{d.Index}", d.Index, new Vector3(sx, d.FloorY, _stairZ0 + 1), Vector3.UnitX);
                    }
                }
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
                    // The corridor, in runs between the special sections.
                    var sections = _sections.GetValueOrDefault(d.Index) ?? [];
                    var runs = _runs[d.Index] = [];
                    var cursor = za;
                    foreach (var sec in sections.OrderBy(x => x.A))
                    {
                        runs.Add((cursor, sec.A, runs.Count == 0 ? hubId : $"{hubId}_{runs.Count}"));
                        cursor = sec.B;
                    }
                    runs.Add((cursor, zb, runs.Count == 0 ? hubId : $"{hubId}_{runs.Count}"));
                    _hub[d.Index] = hubId;
                    foreach (var (a, b, id) in runs)
                    {
                        Room(id, "corridor", d.Index, -HalfCorridor, HalfCorridor, a, b);
                        var run = id;
                        foreach (var s in new[] { 1f, -1f })
                            SideRooms(d, s, a, b, program, HalfCorridor, _ => run);
                    }
                    foreach (var sec in sections) BuildSection(d, sec, program);
                }
                // Full-width rooms open onto the hub through their end walls.
                if (d.Index == top) Door("interior", "bridge", HubAt(d.Index, zb - 0.01f), d.Index, new Vector3(0, d.FloorY, zb), -Vector3.UnitZ);
                if (d.Index == 0) Door("interior", "engineering", HubAt(d.Index, za + 0.01f), d.Index, new Vector3(0, d.FloorY, za), Vector3.UnitZ);
            }
            // Stairs between consecutive decks, from each stairwell up into the next.
            for (var i = 0; i + 1 < decks.Count; i++)
            {
                var d = decks[i];
                // Against the outer wall of the narrower of the two stairwells, so the flight lands inside both, leaving a
                // walkway beside it from the door to both landings. (On a tapering hull the flights step sideways deck by
                // deck; stacking them exactly in a narrow shaft trips up navigation-mesh builders.)
                var hw = -MathF.Max(StairX(d), StairX(decks[i + 1]));
                var w = Num.Clamp((hw - HalfCorridor) * 0.45f, 1.4f, 1.8f);
                // Flush against the wall: no gap beside the flight where the floor below shows through.
                var min = new Vector3(-hw + Wall / 2, d.FloorY, _stairZ0 + StairFoot);
                var max = new Vector3(-hw + Wall / 2 + w, decks[i + 1].FloorY, _stairZ0 + StairFoot + StairRun);
                result.Stairs.Add(new StairInfo($"stair_{i}", i, i + 1, $"stairs_{i}", A(min), A(max)));
            }
            // Airlocks in the hull wall of the middle deck.
            Airlock(decks[_airlockDeck]);
        }

        /// <summary>Rooms along one side strip of a deck, between z0 and z1, skipping the stairwell and the airlock.</summary>
        private void SideRooms(DeckInfo d, float side, float z0, float z1, Program program, float inner, Func<float, string> hub)
        {
            var x0 = side > 0 ? inner : -d.HalfWidth;
            var x1 = side > 0 ? d.HalfWidth : -inner;
            if (x1 - x0 < 2.5f) return;
            var main = inner == HalfCorridor;
            var gaps = new List<(float a, float b)>();
            if (side < 0 && result.Decks.Count > 1) gaps.Add((_stairZ0, _stairZ0 + StairLen));
            if (side > 0 && d.Index == _airlockDeck) gaps.Add((AirlockZ(d), AirlockZ(d) + 3));
            foreach (var cz in _cross)
            {
                if (!main || cz < z0 || cz + 2 > z1) continue;
                if (gaps.Any(g => cz + 2 > g.a && cz < g.b)) continue;
                gaps.Add((cz, cz + 2));
                var id = $"cross_{d.Index}_{cz:0}_{(side > 0 ? "p" : "s")}";
                Room(id, "corridor", d.Index, x0, x1, cz, cz + 2);
                Door("opening", id, hub(cz + 1), d.Index, new Vector3(side * HalfCorridor, d.FloorY, cz + 1), new Vector3(side, 0, 0), 2f);
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
                    var zc = z + l / 2;
                    // Special rooms nearly always get their second room; cabins and quarters only now and then.
                    var common = kind is "passenger_cabin" or "crew_quarters" or "workshop" or "armoury";
                    var suite = x1 - x0 >= 8 && r.Chance(common ? 0.35 : 0.85) ? Suite(kind) : null;
                    if (suite is { } pair)
                    {
                        // A suite: the front room opens onto the corridor, the back one (against the hull) through it.
                        var depth = MathF.Round((x1 - x0) * r.Range(0.42f, 0.55f) * 2) / 2;
                        var xm = side > 0 ? x0 + depth : x1 - depth;
                        var front = $"{pair.front}_{_rooms.Count}";
                        Room(front, pair.front, d.Index, side > 0 ? x0 : xm, side > 0 ? xm : x1, z, z + l);
                        var back = $"{pair.back}_{_rooms.Count}";
                        Room(back, pair.back, d.Index, side > 0 ? xm : x0, side > 0 ? x1 : xm, z, z + l);
                        Door("interior", front, hub(zc), d.Index, new Vector3(side * inner, d.FloorY, zc), new Vector3(side, 0, 0));
                        Door("interior", back, front, d.Index, new Vector3(xm, d.FloorY, zc), new Vector3(side, 0, 0));
                    }
                    else
                    {
                        var id = $"{kind}_{_rooms.Count}";
                        Room(id, kind, d.Index, x0, x1, z, z + l);
                        Door("interior", id, hub(zc), d.Index, new Vector3(side * inner, d.FloorY, zc), new Vector3(side, 0, 0));
                    }
                    z += l;
                }
            }
            if (main && side < 0 && result.Decks.Count > 1 && z0 <= _stairZ0 && _stairZ0 + StairLen <= z1)
                Door("interior", $"stairs_{d.Index}", hub(_stairZ0 + StairLen / 2), d.Index, new Vector3(-HalfCorridor, d.FloorY, _stairZ0 + StairLen / 2), -Vector3.UnitX);
        }

        /// <summary>Deep rooms that become a pair: the front one on the corridor and a second one behind it.</summary>
        private static (string front, string back)? Suite(string kind) => kind switch
        {
            "medbay" => ("medbay", "ward"),
            "lab" => ("lab", "clean_room"),
            "captain_cabin" => ("captain_office", "captain_cabin"),
            "mess" => ("mess", "galley"),
            "workshop" => ("workshop", "storage"),
            "armoury" => ("armoury", "storage"),
            "passenger_cabin" => ("suite_lounge", "passenger_cabin"),
            "crew_quarters" => ("crew_quarters", "bunk_room"),
            "briefing" => ("briefing", "comms"),
            _ => null,
        };

        /// <summary>The corridor run (or hold) a point along a deck opens onto.</summary>
        private string HubAt(int deck, float z)
        {
            if (!_runs.TryGetValue(deck, out var runs)) return _hub[deck];
            foreach (var (a, b, id) in runs)
                if (z >= a - 0.01f && z <= b + 0.01f) return id;
            return runs[0].id;
        }

        /// <summary>
        /// Picks the special sections: at most one hall (two decks tall, on a pair of wide decks) and up to two ring
        /// corridors on other wide decks, each in a stretch of the deck clear of the stairwell, the airlock and the
        /// cross-corridors, with some corridor left at both ends.
        /// </summary>
        private void PlanSections(bool hold)
        {
            var decks = result.Decks;
            if (decks.Count < 2 || Kind == "utility") return;
            List<(float a, float b)> Free(DeckInfo d)
            {
                var (sa, sb) = _span[d.Index];
                var blocked = new List<(float a, float b)> { (_stairZ0 - 1, _stairZ0 + StairLen + 1) };
                if (d.Index == _airlockDeck) blocked.Add((AirlockZ(d) - 1, AirlockZ(d) + 4));
                foreach (var cz in _cross) blocked.Add((cz - 1, cz + 3));
                var free = new List<(float a, float b)>();
                var cursor = sa + 3;
                foreach (var (a, b) in blocked.OrderBy(x => x.a))
                {
                    if (a > cursor) free.Add((cursor, MathF.Min(a, sb - 3)));
                    cursor = MathF.Max(cursor, b);
                }
                if (sb - 3 > cursor) free.Add((cursor, sb - 3));
                return free.Where(x => x.b - x.a > 0).ToList();
            }
            static List<(float a, float b)> Both(List<(float a, float b)> p, List<(float a, float b)> q) =>
                [.. from x in p from y in q let a = MathF.Max(x.a, y.a) let b = MathF.Min(x.b, y.b) where b > a select (a, b)];
            (float a, float b)? Fit(List<(float a, float b)> free, float min, float max)
            {
                var best = free.Where(x => x.b - x.a >= min).OrderByDescending(x => x.b - x.a).Cast<(float a, float b)?>().FirstOrDefault();
                if (best is not { } f) return null;
                var len = MathF.Floor(MathF.Min(f.b - f.a, r.Range(min, max)));
                var a = MathF.Round(f.a + r.Range(0, f.b - f.a - len));
                return (a, a + len);
            }
            bool Wide(DeckInfo d, float hw) => !(hold && d.Index == 0) && !d.Narrow && d.HalfWidth >= hw;

            var used = new HashSet<int>();
            // A hall two decks tall: low in working ships (hangar, cargo bay), mid-ship in liners and labs.
            var low = Kind is "warship" or "patrol" or "citadel" or "freighter" or "industrial" or "raider";
            var pairs = Enumerable.Range(0, decks.Count - 1).Where(i => Wide(decks[i], 7.5f) && Wide(decks[i + 1], 7.5f))
                .OrderBy(i => low ? i : Math.Abs(i + 0.5f - (decks.Count - 1) / 2f)).ToList();
            if (pairs.Count > 0 && r.Chance(0.8))
            {
                var i = pairs[0];
                if (Fit(Both(Free(decks[i]), Free(decks[i + 1])), 14, 22) is { } h)
                {
                    var c = Num.Clamp(MathF.Round(MathF.Min(decks[i].HalfWidth, decks[i + 1].HalfWidth) * 0.5f), 4, MathF.Min(decks[i].HalfWidth, decks[i + 1].HalfWidth) - 3.5f);
                    var kind = Kind switch
                    {
                        "civilian" or "research" or "ring" => "atrium",
                        "warship" or "patrol" or "citadel" => "hangar",
                        "freighter" or "industrial" or "raider" => "cargo_bay",
                        _ => "hall",
                    };
                    _sections[i] = [new Section("hall", h.a, h.b, c, kind)];
                    _sections[i + 1] = [new Section("gallery", h.a, h.b, c, "gallery")];
                    used.Add(i);
                    used.Add(i + 1);
                }
            }
            // Ring corridors round a central room.
            var rings = 0;
            foreach (var d in decks.Where(d => !used.Contains(d.Index) && Wide(d, 8)).OrderBy(_ => r.NextDouble()))
            {
                if (rings >= 2 || !r.Chance(0.7)) continue;
                if (Fit(Free(d), 14, 24) is not { } g) continue;
                var c = Num.Clamp(MathF.Floor(d.HalfWidth * 0.35f), 2.5f, d.HalfWidth - 5.5f);
                var kind = Kind switch
                {
                    "civilian" => "lounge",
                    "research" or "ring" => "lab",
                    "warship" or "patrol" or "citadel" => "operations",
                    _ => "commons",
                };
                _sections[d.Index] = [new Section("ring", g.a, g.b, c, kind)];
                rings++;
            }
        }

        /// <summary>Builds one special section's rooms, its openings to the corridor runs on both ends, and the rooms
        /// along its sides.</summary>
        private void BuildSection(DeckInfo d, Section sec, Program program)
        {
            var (a, b, c) = (sec.A, sec.B, sec.C);
            var fy = d.FloorY;
            var before = HubAt(d.Index, a - 0.01f);
            var after = HubAt(d.Index, b + 0.01f);
            if (sec.Mode == "ring")
            {
                var aft = $"ring_{d.Index}_aft";
                var fwd = $"ring_{d.Index}_fwd";
                var port = $"ring_{d.Index}_port";
                var stbd = $"ring_{d.Index}_stbd";
                Room(aft, "corridor", d.Index, -c - 2, c + 2, a, a + 2);
                Room(fwd, "corridor", d.Index, -c - 2, c + 2, b - 2, b);
                Room(stbd, "corridor", d.Index, -c - 2, -c, a + 2, b - 2);
                Room(port, "corridor", d.Index, c, c + 2, a + 2, b - 2);
                var centre = $"{sec.Kind}_{_rooms.Count}";
                Room(centre, sec.Kind, d.Index, -c, c, a + 2, b - 2);
                Door("opening", before, aft, d.Index, new Vector3(0, fy, a), Vector3.UnitZ, 2f);
                Door("opening", fwd, after, d.Index, new Vector3(0, fy, b), Vector3.UnitZ, 2f);
                foreach (var (lane, x) in new[] { (stbd, -c - 1), (port, c + 1) })
                {
                    Door("opening", aft, lane, d.Index, new Vector3(x, fy, a + 2), Vector3.UnitZ, 2f);
                    Door("opening", lane, fwd, d.Index, new Vector3(x, fy, b - 2), Vector3.UnitZ, 2f);
                }
                var mid = MathF.Round((a + b) / 2 * 2) / 2;
                Door("interior", centre, port, d.Index, new Vector3(c, fy, mid), Vector3.UnitX);
                Door("interior", centre, stbd, d.Index, new Vector3(-c, fy, mid), -Vector3.UnitX);
                Door("interior", centre, aft, d.Index, new Vector3(0, fy, a + 2), -Vector3.UnitZ);
                foreach (var side in new[] { 1f, -1f })
                {
                    var lane = side > 0 ? port : stbd;
                    SideRooms(d, side, a, b, program, c + 2, z => z < a + 2 ? aft : z > b - 2 ? fwd : lane);
                }
                return;
            }
            // A hall on the lower deck, as tall as two decks; on the deck above, a gallery round the void over it.
            var id = sec.Mode == "hall" ? $"{sec.Kind}_{_rooms.Count}" : $"gallery_{d.Index}";
            if (sec.Mode == "hall")
                Room(id, sec.Kind, d.Index, -c, c, a, b, result.Decks[d.Index + 1].CeilingY);
            else
            {
                Room(id, "gallery", d.Index, -c, c, a, b);
                (_voids.TryGetValue(d.Index, out var v) ? v : _voids[d.Index] = []).Add((-c + Gallery, c - Gallery, a + Gallery, b - Gallery));
                // The grand staircase rises from the hall below to the forward walkway.
                var hall = result.Rooms.Last(x => x.Deck == d.Index - 1 && x.Kind == _sections[d.Index - 1][0].Kind);
                var below = result.Decks[d.Index - 1];
                var half = Num.Clamp(MathF.Round(c * 0.3f * 4) / 4, 1f, 1.75f);
                result.Stairs.Add(new StairInfo($"grand_stair_{below.Index}", below.Index, d.Index, hall.Id,
                    A(new Vector3(-half, below.FloorY, b - Gallery - GrandRun)), A(new Vector3(half, fy, b - Gallery)), id));
            }
            Door("opening", before, id, d.Index, new Vector3(0, fy, a), Vector3.UnitZ, 2f);
            Door("opening", id, after, d.Index, new Vector3(0, fy, b), Vector3.UnitZ, 2f);
            foreach (var side in new[] { 1f, -1f })
                SideRooms(d, side, a, b, program, c, _ => id);
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
            Door("interior", "airlock", HubAt(d.Index, z + 1.5f), d.Index, new Vector3(HalfCorridor, d.FloorY, z + 1.5f), Vector3.UnitX);
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

        /// <summary>Window candidates along the hull walls of rooms where people are (not stores, machinery or stairs).</summary>
        public void Windows(Func<WindowInfo, bool> accept)
        {
            string[] blind = ["storage", "reactor", "life_support", "cargo_hold", "engineering", "engine_room", "stairwell", "airlock", "armoury", "brig"];
            var n = 0;
            foreach (var room in result.Rooms)
            {
                if (blind.Contains(room.Kind)) continue;
                var d = result.Decks[room.Deck];
                foreach (var side in new[] { 1, -1 })
                {
                    var wallX = side * d.HalfWidth;
                    if (MathF.Abs((side > 0 ? room.Max[0] : room.Min[0]) - wallX) > 0.05f) continue;
                    var len = room.Max[2] - room.Min[2];
                    var count = Math.Max(1, (int)((len - 0.6f) / 3.4f));
                    for (var k = 0; k < count; k++)
                    {
                        var zc = room.Min[2] + (k + 0.5f) * len / count;
                        if (len < WindowW + 0.8f) break;
                        // Keep clear of doors in the same wall (the outer airlock door).
                        if (result.Doors.Any(o => o.Deck == room.Deck && MathF.Abs(o.Center[0] - wallX) < 0.05f && MathF.Abs(o.Center[2] - zc) < (o.Width + WindowW) / 2 + 0.3f)) continue;
                        var w = new WindowInfo($"window_{n}", room.Id, room.Deck, side, zc - WindowW / 2, zc + WindowW / 2, d.FloorY + Sill, d.FloorY + Sill + WindowH, wallX + side * Wall / 2);
                        if (accept(w)) { result.Windows.Add(w); n++; }
                    }
                }
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
                // The hole is exactly the flight's footprint (running under the wall on the hull side): slivers of
                // the floor below showing beside a landing make navigation meshes join the two decks wrongly.
                var holes = result.Stairs.Where(s => s.ToDeck == d.Index && s.ToRoom is null).Select(s => (s.Min[0] - Wall / 2, s.Max[0], s.Min[2], s.Max[2])).ToList();
                var voids = _voids.GetValueOrDefault(d.Index) ?? [];
                holes.AddRange(voids);
                SlabWithHoles(deck.Mesh, Floor, -hw - Wall, hw + Wall, d.Z0 - Wall, d.Z1 + Wall, d.FloorY - Slab, d.FloorY, holes);
                if (d.Index == result.Decks.Count - 1 || result.Decks.Count == 1)
                {
                    var roof = Add(model, $"ceiling_{d.Index}", "ceiling", deck.Id);
                    Shapes.BoxMinMax(roof.Mesh, WallMat, new Vector3(-hw - Wall, d.CeilingY, d.Z0 - Wall), new Vector3(hw + Wall, d.CeilingY + Slab, d.Z1 + Wall));
                }
                // Railings round the voids over the halls, open where the grand staircase arrives.
                if (voids.Count > 0)
                {
                    var rail = Add(model, $"gallery_rail_{d.Index}", "railing", deck.Id);
                    foreach (var v in voids)
                    {
                        var y = d.FloorY + 1f;
                        void Run(Vector3 p, Vector3 q)
                        {
                            Shapes.Beam(rail.Mesh, Trim, p + new Vector3(0, 1f, 0), q + new Vector3(0, 1f, 0), 0.05f);
                            var n = Math.Max(1, (int)(Vector3.Distance(p, q) / 1.5f));
                            for (var k = 0; k <= n; k++)
                            {
                                var at = Vector3.Lerp(p, q, k / (float)n);
                                Shapes.Beam(rail.Mesh, Trim, at, at + new Vector3(0, 1f, 0), 0.035f);
                            }
                        }
                        var fy = d.FloorY;
                        Run(new Vector3(v.x0, fy, v.z0), new Vector3(v.x1, fy, v.z0));
                        Run(new Vector3(v.x0, fy, v.z0), new Vector3(v.x0, fy, v.z1));
                        Run(new Vector3(v.x1, fy, v.z0), new Vector3(v.x1, fy, v.z1));
                        var gap = (result.Stairs.FirstOrDefault(x => x.ToDeck == d.Index && x.ToRoom != null)?.Max[0] ?? 1f) + 0.05f;
                        Run(new Vector3(v.x0, fy, v.z1), new Vector3(-gap, fy, v.z1));
                        Run(new Vector3(gap, fy, v.z1), new Vector3(v.x1, fy, v.z1));
                    }
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
                    var tall = room.Max[1] > d.CeilingY + 0.5f;
                    // A gallery's middle is the void, and a hall's forward half holds the grand staircase: their
                    // markers stand on the aft walkway and the aft floor.
                    m.Pivot = room.Kind == "gallery" ? new Vector3(0, d.FloorY, box.Min.Z + Gallery / 2)
                        : tall ? new Vector3(0, d.FloorY, box.Min.Z + 2.5f)
                        : new Vector3(box.Center.X, d.FloorY, box.Center.Z);
                    m.Extras = new() { ["kind"] = room.Kind, ["deck"] = d.Index, ["min"] = room.Min, ["max"] = room.Max };
                    if (tall) m.Extras["decks"] = 2;
                    if (room.Kind == "gallery")
                        foreach (var s in new[] { -1f, 1f })
                            Lights(m.Mesh, new Aabb(new Vector3(s * (box.Size.X / 2 - Gallery / 2) - 0.3f, box.Min.Y, box.Min.Z), new Vector3(s * (box.Size.X / 2 - Gallery / 2) + 0.3f, box.Max.Y, box.Max.Z)), room.Kind);
                    else Lights(m.Mesh, box, room.Kind);
                    if (room.Kind != "gallery" && Zone(room.Kind) is { } zone && box.Size.X > 1.2f && box.Size.Z > 1.2f)
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
                    foreach (var x in st.ToRoom is null ? new[] { st.Max[0] } : new[] { st.Min[0], st.Max[0] })
                        Shapes.Beam(rail.Mesh, Trim, new Vector3(x, st.Min[1] + 1f, st.Min[2]), new Vector3(x, st.Max[1] + 1f, st.Max[2]), 0.06f);
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
            // Bands along Z between the holes' ends; in each band, the floor between the holes crossing it.
            var cuts = new SortedSet<float> { z0, z1 };
            foreach (var h in holes) { cuts.Add(Num.Clamp(h.z0, z0, z1)); cuts.Add(Num.Clamp(h.z1, z0, z1)); }
            var zs = cuts.ToList();
            for (var i = 0; i + 1 < zs.Count; i++)
            {
                var (za, zb) = (zs[i], zs[i + 1]);
                if (zb - za < 0.001f) continue;
                var cursor = x0;
                foreach (var h in holes.Where(h => h.z0 < zb - 0.001f && h.z1 > za + 0.001f).OrderBy(h => h.x0))
                {
                    if (h.x0 > cursor) Shapes.BoxMinMax(m, mat, new Vector3(cursor, y0, za), new Vector3(h.x0, y1, zb));
                    cursor = MathF.Max(cursor, h.x1);
                }
                if (x1 > cursor) Shapes.BoxMinMax(m, mat, new Vector3(cursor, y0, za), new Vector3(x1, y1, zb));
            }
        }

        /// <summary>A wall at x = <paramref name="x"/> running along Z from z0 to z1, with openings for the doors in it.</summary>
        private void WallAlongZ(Mesh m, float x, float z0, float z1, DeckInfo d, List<DoorInfo> doors)
        {
            var openings = doors.Where(o => MathF.Abs(o.Center[0] - x) < 0.05f && MathF.Abs(o.Normal[0]) > 0.5f && o.Center[2] > z0 && o.Center[2] < z1)
                .Select(o => (c: o.Center[2], w: o.Width, lo: 0f, hi: o.Height, frame: o.Kind != "opening")).ToList();
            // Windows in the hull walls: an opening between sill and head.
            foreach (var win in _windows.Where(v => v.Deck == d.Index && MathF.Abs(v.Side * d.HalfWidth - x) < 0.05f && v.Z0 > z0 - 0.01f && v.Z1 < z1 + 0.01f))
                openings.Add(((win.Z0 + win.Z1) / 2, win.Z1 - win.Z0, win.Y0 - d.FloorY, win.Y1 - d.FloorY, false));
            openings.Sort((p, q) => p.c.CompareTo(q.c));
            Segments(z0, z1, openings, (a, b, y0, y1) =>
                Shapes.BoxMinMax(m, WallMat, new Vector3(x - Wall / 2, d.FloorY + y0, a), new Vector3(x + Wall / 2, d.FloorY + y1, b)),
                d.CeilingY - d.FloorY, (c) => Frame(m, new Vector3(x, d.FloorY, c), alongX: false));
        }

        /// <summary>A wall at z = <paramref name="z"/> running along X from x0 to x1, with openings for the doors in it.</summary>
        private void WallAlongX(Mesh m, float z, float x0, float x1, DeckInfo d, List<DoorInfo> doors)
        {
            var openings = doors.Where(o => MathF.Abs(o.Center[2] - z) < 0.05f && MathF.Abs(o.Normal[2]) > 0.5f && o.Center[0] > x0 && o.Center[0] < x1)
                .Select(o => (c: o.Center[0], w: o.Width, lo: 0f, hi: o.Height, frame: o.Kind != "opening")).OrderBy(v => v.c).ToList();
            Segments(x0, x1, openings, (a, b, y0, y1) =>
                Shapes.BoxMinMax(m, WallMat, new Vector3(a, d.FloorY + y0, z - Wall / 2), new Vector3(b, d.FloorY + y1, z + Wall / 2)),
                d.CeilingY - d.FloorY, (c) => Frame(m, new Vector3(c, d.FloorY, z), alongX: true));
        }

        /// <summary>
        /// Wall pieces round the openings: full height between them, and over each opening the wall below its bottom
        /// (a window's sill) and above its top (the lintel); a frame round each real door.
        /// </summary>
        private static void Segments(float a, float b, List<(float c, float w, float lo, float hi, bool frame)> openings, Action<float, float, float, float> piece, float height, Action<float> frame)
        {
            var cursor = a;
            foreach (var (c, w, lo, hi, framed) in openings)
            {
                var o0 = MathF.Max(c - w / 2, cursor);
                var o1 = MathF.Min(c + w / 2, b);
                if (o1 <= o0) continue;
                if (o0 > cursor + 0.01f) piece(cursor, o0, 0, height);
                if (lo > 0.01f) piece(o0, o1, 0, lo);
                if (hi < height - 0.01f) piece(o0, o1, hi, height);
                if (framed) frame(c);
                cursor = o1;
            }
            if (b > cursor + 0.01f) piece(cursor, b, 0, height);
        }

        /// <summary>The floor tint of a room kind: command, crew, work, cargo or medical (corridors keep the bare floor).</summary>
        private static string? Zone(string kind) => kind switch
        {
            "bridge" or "comms" or "briefing" or "captain_cabin" or "observation" or "cockpit" => "command",
            "crew_quarters" or "passenger_cabin" or "mess" or "galley" or "lounge" => "crew",
            "engineering" or "engine_room" or "reactor" or "workshop" or "life_support" or "armoury" or "brig" or "lab" => "work",
            "cargo_hold" or "storage" => "cargo",
            "medbay" or "ward" or "clean_room" => "medical",
            "atrium" or "hall" or "commons" or "suite_lounge" => "plaza",
            "hangar" or "cargo_bay" => "cargo",
            "operations" or "captain_office" => "command",
            "bunk_room" => "crew",
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

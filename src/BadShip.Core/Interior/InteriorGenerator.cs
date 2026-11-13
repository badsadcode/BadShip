using System.Numerics;
using BadShip.Generation;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Interior;

/// <summary>A deck: <c>Part</c> is "hull" for the main stack, "superstructure" for a deck inside a block on top of the
/// hull, "keel" for one inside the keel under it.</summary>
/// <c>Cx</c> is the deck's centre across the ship (blocks may sit off the centreline). A <c>Shaped</c> deck follows its
/// block's shape (slanted upper corners, a ceiling that comes down under sloped ends) instead of being a box.
public sealed record DeckInfo(int Index, float FloorY, float CeilingY, float HalfWidth, float Z0, float Z1, bool Narrow, string Part = "hull", float Cx = 0, bool Shaped = false);
public sealed record RoomInfo(string Id, string Kind, int Deck, float[] Min, float[] Max);
public sealed record DoorInfo(string Id, string Kind, string A, string B, int Deck, float[] Center, float[] Normal, float Width, float Height);
/// <summary>A flight of stairs from a room on one deck up to the next; <c>ToRoom</c> names the room it arrives in when that is
/// not the stairwell above (a grand staircase up to a hall's gallery).</summary>
public sealed record StairInfo(string Id, int FromDeck, int ToDeck, string Room, float[] Min, float[] Max, string? ToRoom = null);

/// <summary>A window in the hull wall of a room: side +1 (port) or −1 (starboard), along Z from z0 to z1, sill to head y0–y1.</summary>
/// Windows in a shaped deck are cut where the block already had glass: <c>Module</c> names the block, and S, F, U0..V1 the
/// pane on its loft (segment, face and rectangle), which may be on a chamfer or a sloped front (Side 0: overhead).
public sealed record WindowInfo(string Id, string Room, int Deck, int Side, float Z0, float Z1, float Y0, float Y1, float WallX,
    string? Module = null, int S = -1, int F = -1, float U0 = 0, float U1 = 0, float V0 = 0, float V1 = 0, bool Painted = false);

/// <summary>
/// A ladder or an elevator: the decks it stops at (bottom up), the room it opens into at each stop, its shaft footprint
/// (x0, x1, z0, z1), a point on the floor at each stop where people get on and off, and (elevators) the door at each stop.
/// </summary>
public sealed record LiftInfo(string Id, string Kind, int[] Decks, string[] Rooms, float[] Shaft, float[][] Access, string[] Doors);

/// <summary>A ship's interior: the walkable geometry (as a <see cref="Ship"/> of modules for the GLB writer) and the room graph.</summary>
public sealed class ShipInterior
{
    public required Ship Model { get; init; }
    public List<DeckInfo> Decks { get; } = [];
    public List<RoomInfo> Rooms { get; } = [];
    public List<DoorInfo> Doors { get; } = [];
    public List<StairInfo> Stairs { get; } = [];
    public List<WindowInfo> Windows { get; } = [];
    public List<LiftInfo> Lifts { get; } = [];

    /// <summary>Shaped decks and the block loft each one follows (with the block's module id).</summary>
    public Dictionary<int, (Loft Loft, string Module)> DeckShells { get; } = [];

    /// <summary>The cross-section each deck outside the main hull was fitted into (superstructure and keel decks).</summary>
    public Dictionary<int, Profile> DeckHulls { get; } = [];

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
/// Decks also go into the blocks on top of the hull and into the keel under it, joined to the main decks by stairs or,
/// where it is tight, by a ladder through a hatch; ships with three or more decks get an elevator through the main
/// stack, with a car and a door at every stop that opens only when the car is there.
/// Narrow hulls (small craft) get one deck of rooms in a row. Rooms come from the ship type (crew quarters, mess,
/// medbay, labs, armoury, passenger cabins, wards...): rooms, walls, doors, stairs and lights, and furniture against the walls (see Furniture).
/// Doors are separate nodes (type "door") with their rooms, size and slide axis in the extras, so an engine can turn
/// each into a working door; rooms are empty nodes (type "room") at their centres with their box in the extras.
/// Coordinates are the ship's, so the interior lines up with the exterior.
/// </summary>
public static class InteriorGenerator
{
    public const float DeckPitch = 3.4f, Slab = 0.3f, Clear = DeckPitch - Slab, Wall = 0.2f, DoorW = 1.4f, DoorH = 2.2f, HalfCorridor = 1f;
    public const string Floor = "interior_floor", WallMat = "interior_wall", Trim = "interior_trim", DoorMat = "interior_door";
    /// <summary>The inner skin of a shaped deck: seen from inside, and from outside when looking in with the roof off.</summary>
    public const string SkinMat = "interior_skin";

    public const float WindowW = 1.4f, Sill = 1.0f, WindowH = 1.2f;

    /// <summary>
    /// Builds the interior. With <paramref name="acceptWindow"/>, rooms along the hull walls get windows: each
    /// candidate is offered to the callback (which checks the hull can take an opening there, and reserves it) and
    /// only accepted ones are cut into the interior's outer walls.
    /// </summary>
    public static ShipInterior? Build(Ship ship, Func<WindowInfo, WindowInfo?>? acceptWindow = null)
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
        model.Materials[SkinMat] = new MaterialDef(SkinMat, Palettes.Linear("#9aa1a8"), 0.7f, 0.2f, Vector3.Zero, 1) { DoubleSided = true };
        foreach (var (zone, hex) in new[] { ("command", "#3f5670"), ("crew", "#4d5747"), ("work", "#6a5434"), ("cargo", "#56493a"), ("medical", "#2f6b66"), ("plaza", "#5b4a6e") })
            model.Materials["zone_" + zone] = new MaterialDef("zone_" + zone, Palettes.Linear(hex), 0.8f, 0.2f, Vector3.Zero, 1);
        var result = new ShipInterior { Model = model, Hull = P };
        var root = Add(model, "interior", "interior", null);

        // Decks that are wide enough; narrow hulls get one deck of rooms in a row.
        for (var d = 0; d < n; d++)
        {
            var fy = floor0 + d * DeckPitch;
            // Hull decks follow the hull's shape (see Skin): only the band people walk in has to fit.
            var walkHw = float.MaxValue;
            for (var y = fy - Slab; y <= fy + Walk + 0.01f; y += 0.25f) walkHw = MathF.Min(walkHw, P.HalfWidthAt(y));
            var hw = walkHw - 0.6f;
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
        // Each deck reaches on into the hull's nose and tail as far as its walking band still fits.
        for (var i = 0; i < result.Decks.Count; i++)
        {
            var d = result.Decks[i];
            bool Fits(float z) => BoxInsideLoft(loft, z, -d.HalfWidth - 0.6f, d.HalfWidth + 0.6f, d.FloorY - Slab, d.FloorY + Walk);
            var (e0, e1) = (d.Z0, d.Z1);
            while (e1 + 0.5f <= loft.Zs[^1] - 0.5f && Fits(e1 + 0.5f)) e1 += 0.5f;
            while (e0 - 0.5f >= loft.Zs[0] + 0.5f && Fits(e0 - 0.5f)) e0 -= 0.5f;
            result.Decks[i] = d with { Z0 = MathF.Floor(e0), Z1 = MathF.Floor(e1), Shaped = true };
        }
        var plan = new Layout(ship, result, r, Annexes(ship, result));
        plan.HullShell(loft, hull.Id);
        plan.Rooms();
        plan.AddAnnexes();
        if (acceptWindow != null) plan.Windows(acceptWindow);
        plan.SortDecks();
        plan.ShapedWindows();
        plan.Geometry(model, root);
        return result;
    }

    /// <summary>A deck that fits inside a superstructure block or the keel, before it is joined to the main decks.</summary>
    private sealed record AnnexSpec(string Part, Profile P, float FloorY, float Ceiling, float HalfWidth, float Z0, float Z1, float Cx = 0, bool Bridge = false, Loft? Shell = null, string? ShellModule = null);

    /// <summary>The height a shaped deck must keep clear everywhere people walk: a door's height and a little.</summary>
    private const float Walk = DoorH + 0.15f;

    /// <summary>
    /// Decks outside the main hull: one in every centred superstructure block on top of the hull (stacked tiers each get
    /// theirs, above the one below) and one in the keel, each in the block's longest constant section, with its floor
    /// and ceiling clear of the decks next to it.
    /// </summary>
    private static List<AnnexSpec> Annexes(Ship ship, ShipInterior result)
    {
        var list = new List<AnnexSpec>();
        var shapes = new Dictionary<int, AnnexSpec?>();
        var decks = result.Decks;
        var blocks = ship.Modules.Where(m => m.Tier == ModuleTier.Primary && m.Lofts.Count > 0 && (m.Type is "bridge_block" or "superstructure" or "keel" || m.Type == "gondola" && ship.Recipe.Interior))
            .Select(m => (m, s: BodySegment(m.Lofts[0]))).Where(x => x.s >= 0)
            .Select(x => (x.m, loft: x.m.Lofts[0], P: x.m.Lofts[0].Profiles[x.s], za: x.m.Lofts[0].Zs[x.s], zb: x.m.Lofts[0].Zs[x.s + 1]))
            .Where(x => MathF.Abs(x.P.Cx) < 3f).OrderBy(x => x.P.Bottom).ToList();
        foreach (var (m, blockLoft, q, za, zb) in blocks)
        {
            var z0 = MathF.Ceiling(za + 0.6f);
            var z1 = MathF.Floor(zb - 0.6f);
            if (z1 - z0 < 6) continue;
            // The deck's clear height may drop from 3.1 m to 2.5 m (still over the doors) to fit a smaller block.
            AnnexSpec? spec = null;
            for (var clear = Clear; clear > 2.45f && spec is null; clear -= 0.1f)
            {
                float fy;
                if (m.Type == "gondola")
                {
                    // A pod hanging under the hull: the deck goes where the pod is widest (it tapers at both ends).
                    // Only with an interior recipe, which also builds the access trunk the ladder climbs through.
                    if (list.Any(a => a.Part == "gondola")) break;
                    var best = (fy: 0f, hw: 0f);
                    for (var y = q.Bottom + Slab + 0.25f; y + clear <= q.Top - 0.2f; y += 0.1f)
                    {
                        var w = float.MaxValue;
                        for (var yy = y - Slab; yy <= y + clear + 0.01f; yy += 0.25f) w = MathF.Min(w, q.HalfWidthAt(yy));
                        w = MathF.Min(w, q.HalfWidthAt(y + clear));
                        if (w > best.hw + 0.01f) best = (y, w);
                    }
                    if (best.hw <= 0) continue;
                    var ghw = MathF.Floor((best.hw - 0.4f) * 2) / 2;
                    if (ghw < 1.5f) continue;
                    spec = new AnnexSpec("gondola", q, best.fy, best.fy + clear, ghw, z0, z1);
                    continue;
                }
                if (m.Type == "keel")
                {
                    if (list.Any(a => a.Part == "keel")) break;
                    // As high as it goes (the keel narrows downward): right under the lowest deck, and with its ceiling
                    // inside the keel, so the deck can follow the keel's shape (a ladder climbs on into the hull).
                    fy = MathF.Min(decks[0].FloorY - 2 * Slab - clear, q.Top - 0.35f - clear);
                    if (fy - Slab < q.Bottom + 0.25f) continue;
                }
                else
                {
                    // On top of whatever deck is below it in this stretch: the hull's top deck or a lower tier's deck.
                    var under = list.Where(a => a.Part == "superstructure" && a.Z1 > z0 + 4 && a.Z0 < z1 - 4).Select(a => a.Ceiling)
                        .Append(decks[^1].CeilingY).Max();
                    // The block's bottom is sunk into the hull, so its floor may sit right on it.
                    fy = MathF.Max(q.Bottom + Slab, under + 2 * Slab);
                    if (fy + clear > q.Top - 0.2f) continue;
                }
                // Blocks have thinner walls than the hull: 0.4 m from the deck's edge to the outside. A keel deck reaches
                // up into the bottom of the hull, so above the keel's top the hull's own width counts.
                var narrowest = float.MaxValue;
                for (var y = MathF.Max(fy - Slab, q.Bottom + 0.01f); y <= fy + clear + 0.01f; y += 0.25f)
                    narrowest = MathF.Min(narrowest, m.Type == "keel" ? KeelHalfWidth(q, result.Hull, y) : q.HalfWidthAt(y));
                narrowest = MathF.Min(narrowest, m.Type == "keel" ? KeelHalfWidth(q, result.Hull, fy + clear) : q.HalfWidthAt(fy + clear));
                var hw = MathF.Floor((narrowest - 0.4f) * 2) / 2;
                if (hw < 1.5f) continue;
                spec = new AnnexSpec(m.Type == "keel" ? "keel" : "superstructure", q, fy, fy + clear, hw, z0, z1, q.Cx, m.Type == "bridge_block");
            }
            if (spec is null) continue;
            // Past the constant section the block tapers (a sloped or overhanging front, a rounded back): the deck
            // reaches on into it as far as it still fits, so a bridge gets right up to its front windows.
            bool Fits(float z) => BoxInsideLoft(blockLoft, z, spec.Cx - spec.HalfWidth - 0.3f, spec.Cx + spec.HalfWidth + 0.3f,
                MathF.Max(spec.FloorY - Slab, q.Bottom + 0.01f), spec.Ceiling + Slab + 0.05f);
            if (spec.Part != "keel")
            {
                var (e0, e1) = (spec.Z0, spec.Z1);
                while (e1 + 0.5f <= blockLoft.Zs[^1] - 0.3f && Fits(e1 + 0.5f)) e1 += 0.5f;
                while (e0 - 0.5f >= blockLoft.Zs[0] + 0.3f && Fits(e0 - 0.5f)) e0 -= 0.5f;
                spec = spec with { Z0 = e0, Z1 = e1 };
            }
            list.Add(spec);
            if (spec.Part is "superstructure" or "gondola" or "keel") shapes[list.Count - 1] = Shaped(spec, blockLoft, q) is { } sh ? sh with { ShellModule = m.Id } : null;
        }
        // A block deck with nothing stacked on it follows the block's shape: only the band people walk in (floor to a
        // little over door height) has to fit, so it is wider and reaches further into the tapered ends.
        for (var i = 0; i < list.Count; i++)
        {
            if (!shapes.TryGetValue(i, out var shaped) || shaped is null) continue;
            var a = list[i];
            list[i] = shaped;
        }
        return list;
    }

    /// <summary>The shaped version of a block deck: as wide as the walking band allows, reaching as far into the block's
    /// ends as that band still fits.</summary>
    private static AnnexSpec? Shaped(AnnexSpec spec, Loft loft, Profile q)
    {
        var lo = MathF.Max(spec.FloorY - Slab, q.Bottom + 0.01f);
        var narrowest = float.MaxValue;
        for (var y = lo; y <= spec.FloorY + Walk + 0.01f; y += 0.25f) narrowest = MathF.Min(narrowest, q.HalfWidthAt(y));
        var hw = MathF.Floor((narrowest - 0.45f) * 2) / 2;
        if (hw < spec.HalfWidth) hw = spec.HalfWidth;
        bool Fits(float z) => BoxInsideLoft(loft, z, spec.Cx - hw - 0.35f, spec.Cx + hw + 0.35f, lo, spec.FloorY + Walk);
        var (e0, e1) = (spec.Z0, spec.Z1);
        while (e1 + 0.5f <= loft.Zs[^1] - 0.3f && Fits(e1 + 0.5f)) e1 += 0.5f;
        while (e0 - 0.5f >= loft.Zs[0] + 0.3f && Fits(e0 - 0.5f)) e0 -= 0.5f;
        return spec with { HalfWidth = hw, Z0 = e0, Z1 = e1, Shell = loft };
    }

    /// <summary>Whether an axis-aligned box across the ship (x0–x1, y0–y1) at station z lies inside a loft's section there.</summary>
    public static bool BoxInsideLoft(Loft loft, float z, float x0, float x1, float y0, float y1)
    {
        if (z < loft.Zs[0] || z > loft.Zs[^1]) return false;
        var s = 0;
        while (s + 1 < loft.Segments && loft.Zs[s + 1] < z) s++;
        var t = loft.Zs[s + 1] - loft.Zs[s] < 1e-4f ? 1 : (z - loft.Zs[s]) / (loft.Zs[s + 1] - loft.Zs[s]);
        var ring = loft.Rings[s].Zip(loft.Rings[s + 1], (a, b) => Vector3.Lerp(a, b, t)).Select(v => new Vector2(v.X, v.Y)).ToArray();
        // A convex section: every corner on the inner side of every edge (either winding).
        float sign = 0;
        foreach (var p in new[] { new Vector2(x0, y0), new Vector2(x1, y0), new Vector2(x1, y1), new Vector2(x0, y1) })
            for (var i = 0; i < ring.Length; i++)
            {
                var a = ring[i];
                var b = ring[(i + 1) % ring.Length];
                if (Vector2.DistanceSquared(a, b) < 1e-8f) continue;
                var cr = (b.X - a.X) * (p.Y - a.Y) - (b.Y - a.Y) * (p.X - a.X);
                if (MathF.Abs(cr) < 1e-6f) continue;
                if (sign == 0) sign = MathF.Sign(cr);
                else if (MathF.Sign(cr) != sign) return false;
            }
        return true;
    }

    /// <summary>The half-width inside a keel and the hull it hangs under: the keel's below its top, the hull's above.</summary>
    public static float KeelHalfWidth(Profile keel, Profile hull, float y) => y <= keel.Top ? keel.HalfWidthAt(y) : hull.HalfWidthAt(y);

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
    private sealed class Layout(Ship ship, ShipInterior result, Rng r, List<AnnexSpec> annexSpecs)
    {
        // Links from annex decks to the deck they join: stairs or a ladder, with the stretch [Z, Z + Len] they take on
        // both decks. Decks are given as indices into the main stack, or (−1 − k) for the k-th annex.
        private sealed record Link(int Annex, int Other, bool Up, string Kind, float Z, float Len, float X0, float W, float Run, bool Section = true, float Foot = float.NaN);
        // A ladder takes 3.6 m of deck: its rungs from z + 1.3 to z + 2.3, with room to step on and off either side.
        private const float LadderLen = 3.6f, LadderFrom = 1.3f;
        // Where engineering ends on the lowest deck and the bridge starts on the top one (ladders may stand in them).
        private float _engEnd = float.NaN, _bridgeStart = float.NaN;
        private readonly List<Link> _links = [];
        private readonly Dictionary<int, int> _annexDeck = [];
        // Shaped decks and the block loft each one follows.
        private readonly Dictionary<int, Loft> _shells = [];
        private readonly Dictionary<int, string> _shellModules = [];
        // Shaped decks whose bridge opens forward into a low nook under a sloped front: where the nook ends.
        private readonly Dictionary<int, float> _nook = [];
        private const float SkinInset = 0.3f;
        // The elevator: its stretch along the starboard... port strip [z, z + 3.2] and shaft x range, on every main deck.
        private float? _liftZ;
        private float _liftX0, _liftX1;
        private const float LiftLen = 3.2f, LiftW = 2.4f;
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
            // The stretch every deck has (decks reach into the nose and tail by different amounts).
            var dz0 = decks.Max(d => d.Z0);
            var dz1 = decks.Min(d => d.Z1);
            var len = dz1 - dz0;
            if (decks[0].Narrow)
            {
                // One row of rooms; links to decks outside the hull land inside those rooms.
                NarrowDeck(decks[0]);
                _span[0] = (decks[0].Z0, decks[0].Z1);
                PlanLinks(false);
                return;
            }

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
            _engEnd = dz0 + engLen;
            _bridgeStart = dz1 - bridgeLen;
            _stairZ0 = Num.Clamp(MathF.Round(dz0 + len * 0.45f), dz0 + engLen + 0.5f, dz1 - bridgeLen - StairLen - 0.5f);
            var hold = Kind is "freighter" or "industrial" or "raider" && decks.Count >= 3;
            var program = new Program(Kind, r);
            // The free stretch of every deck (between engineering and the bridge), then the airlock's deck: the longest
            // stretch that is not the cargo hold, the middle deck on a tie.
            foreach (var d in decks)
                _span[d.Index] = (d.Z0 + (d.Index == 0 ? engLen : 0), d.Z1 - (d.Index == top ? bridgeLen : ObservationLen(d)));
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
            PlanLinks(hold);
            PlanLift(hold);
            PlanSections(hold);
            foreach (var d in decks)
            {
                var hw = d.HalfWidth;
                var za = d.Z0;
                var zb = d.Z1;
                // Below the bridge, a deck with glass in the nose ahead of it ends in an observation room looking out.
                var obsLen = d.Index == top ? 0 : ObservationLen(d);
                if (obsLen > 0)
                {
                    Room($"observation_{d.Index}", "observation", d.Index, -hw, hw, zb - obsLen, zb);
                    zb -= obsLen;
                }
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
                if (_liftZ is { } ez) LiftRooms(d, ez, hubId);
                // Full-width rooms open onto the hub through their end walls.
                if (d.Index == top) Door("interior", "bridge", HubAt(d.Index, zb - 0.01f), d.Index, new Vector3(0, d.FloorY, zb), -Vector3.UnitZ);
                if (obsLen > 0) Door("interior", $"observation_{d.Index}", HubAt(d.Index, zb - 0.01f), d.Index, new Vector3(0, d.FloorY, zb), -Vector3.UnitZ);
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
                // Where the stairwell has room for two flights and the walkway, every other flight takes the inner
                // lane, so no flight stands right over the one below (as in a real stair tower).
                var lane = i % 2 == 1 && hw - HalfCorridor >= 2 * w + 1.6f ? w + 0.3f : 0f;
                // The outer lane is flush against the wall: no gap beside the flight where the floor below shows.
                var min = new Vector3(-hw + Wall / 2 + lane, d.FloorY, _stairZ0 + StairFoot);
                var max = new Vector3(-hw + Wall / 2 + lane + w, decks[i + 1].FloorY, _stairZ0 + StairFoot + StairRun);
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
            var main = inner == HalfCorridor && d.Part == "hull";
            var gaps = new List<(float a, float b)>();
            if (main && side < 0 && result.Decks.Count(x => x.Part == "hull") > 1) gaps.Add((_stairZ0, _stairZ0 + StairLen));
            if (main && side > 0 && d.Index == _airlockDeck) gaps.Add((AirlockZ(d), AirlockZ(d) + 3));
            if (main && side > 0 && _liftZ is { } lz) gaps.Add((lz, lz + LiftLen));
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
            if (main && side < 0 && result.Decks.Count(x => x.Part == "hull") > 1 && z0 <= _stairZ0 && _stairZ0 + StairLen <= z1)
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
                    AddSection(_sections, i, new Section("hall", h.a, h.b, c, kind));
                    AddSection(_sections, i + 1, new Section("gallery", h.a, h.b, c, "gallery"));
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
                AddSection(_sections, d.Index, new Section("ring", g.a, g.b, c, kind));
                rings++;
            }
        }

        /// <summary>
        /// The stretches of a main deck still free for a special section, a riser or the elevator: inside the free span
        /// with three metres of corridor at both ends, clear of the stairwell, the airlock, the cross-corridors, the
        /// sections already placed and the elevator.
        /// </summary>
        private List<(float a, float b)> Free(DeckInfo d, float margin = 3)
        {
            var (sa, sb) = _span[d.Index];
            var blocked = new List<(float a, float b)>();
            if (result.Decks.Count > 1) blocked.Add((_stairZ0 - 1, _stairZ0 + StairLen + 1));
            if (d.Index == _airlockDeck) blocked.Add((AirlockZ(d) - 1, AirlockZ(d) + 4));
            foreach (var cz in _cross) blocked.Add((cz - 1, cz + 3));
            foreach (var sec in _sections.GetValueOrDefault(d.Index) ?? []) blocked.Add((sec.A - 1, sec.B + 1));
            if (_liftZ is { } lz) blocked.Add((lz - 1, lz + LiftLen + 1));
            var free = new List<(float a, float b)>();
            var cursor = sa + margin;
            foreach (var (a, b) in blocked.OrderBy(x => x.a))
            {
                if (a > cursor) free.Add((cursor, MathF.Min(a, sb - margin)));
                cursor = MathF.Max(cursor, b);
            }
            if (sb - margin > cursor) free.Add((cursor, sb - margin));
            return free.Where(x => x.b - x.a > 0).ToList();
        }

        private static List<(float a, float b)> Both(List<(float a, float b)> p, List<(float a, float b)> q) =>
            [.. from x in p from y in q let a = MathF.Max(x.a, y.a) let b = MathF.Min(x.b, y.b) where b > a select (a, b)];

        private static void AddSection(Dictionary<int, List<Section>> map, int deck, Section sec)
        {
            if (!map.TryGetValue(deck, out var list)) map[deck] = list = [];
            list.Add(sec);
        }

        /// <summary>
        /// Joins each annex deck to the deck below it (superstructure) or above it (keel): stairs where both decks are
        /// wide enough and the rise is a storey or so, otherwise a ladder through a hatch. The link takes the first stretch
        /// both decks have free; on a main deck that stretch becomes a stair hall or ladder room across the deck.
        /// </summary>
        private void PlanLinks(bool hold)
        {
            var main = result.Decks;
            var top = main.Count - 1;
            // Free stretches on each annex, as links claim them.
            var annexFree = annexSpecs.Select(a => new List<(float a, float b)> { (a.Z0 + 0.5f, a.Z1 - 0.5f - (a.Bridge ? BridgeLen(a) : 0)) }).ToList();
            void Claim(List<(float a, float b)> free, float a, float b)
            {
                var copy = free.ToList();
                free.Clear();
                foreach (var (x, y) in copy)
                {
                    if (b <= x || a >= y) { free.Add((x, y)); continue; }
                    if (a - 0.5f > x) free.Add((x, a - 0.5f));
                    if (b + 0.5f < y) free.Add((b + 0.5f, y));
                }
            }
            for (var k = 0; k < annexSpecs.Count; k++)
            {
                var a = annexSpecs[k];
                // The deck it joins: the keel joins the lowest deck; a superstructure block the highest deck under it.
                int other;
                float otherFloor, otherHw, otherCx = 0;
                bool otherNarrow;
                if (a.Part is "keel" or "gondola") { other = 0; otherFloor = main[0].FloorY; otherHw = main[0].HalfWidth; otherNarrow = false; }
                else
                {
                    var lower = Enumerable.Range(0, k).Where(j => annexSpecs[j].Part == "superstructure" && _annexDeck.ContainsKey(j)
                        && annexSpecs[j].FloorY < a.FloorY - 1 && annexSpecs[j].Z1 > a.Z0 + 4 && annexSpecs[j].Z0 < a.Z1 - 4).Cast<int?>().LastOrDefault();
                    if (lower is { } j) { other = -1 - j; otherFloor = annexSpecs[j].FloorY; otherHw = annexSpecs[j].HalfWidth; otherCx = annexSpecs[j].Cx; otherNarrow = otherHw < 3.5f; }
                    else { other = top; otherFloor = main[top].FloorY; otherHw = main[top].HalfWidth; otherNarrow = false; }
                }
                var rise = MathF.Abs(a.FloorY - otherFloor);
                // Where the two decks overlap across the ship (blocks may sit off the centreline): the link stands
                // against the overlap's starboard edge.
                var left = MathF.Max(a.Cx - a.HalfWidth, otherCx - otherHw);
                var right = MathF.Min(a.Cx + a.HalfWidth, otherCx + otherHw);
                var hw = (right - left) / 2;
                if (hw < 1.2f) continue;
                // Where it may go on the other deck.
                List<(float a, float b)> there;
                if (other < 0) there = annexFree[-1 - other];
                else if (main[other].Narrow)
                    there = [.. result.Rooms.Where(q => q.Deck == other).Select(q => (q.Min[2] + 0.4f, q.Max[2] - 0.4f))];
                else if (hold && other == 0) there = Free(main[0], 1);
                else there = Free(main[other], 1.5f);
                var here = annexFree[k];
                Link? link = null;
                // A gondola hangs clear of the hull: always a ladder, up an access trunk through the gap.
                foreach (var kind in a.Part != "gondola" && hw >= 2.5f && !otherNarrow && rise <= 6.2f ? new[] { "stairs", "ladder" } : new[] { "ladder" })
                {
                    var run = kind == "stairs" ? MathF.Ceiling(MathF.Max(StairRun, rise) * 10) / 10 : 1f;
                    var len = kind == "stairs" ? StairFoot + run + 1.6f : LadderLen;
                    var section = true;
                    // Inside a room (a narrow deck's rooms, or engineering or the bridge, which need no hall of their own)
                    // only the ladder itself has to fit, 0.6 m clear of the room's ends, anywhere along its landing.
                    if (other >= 0 && kind == "ladder")
                    {
                        var rooms = new List<(float a, float b)>();
                        if (main[other].Narrow) rooms.AddRange(result.Rooms.Where(q => q.Deck == other).Select(q => (q.Min[2], q.Max[2])));
                        else
                        {
                            if (other == 0 && !float.IsNaN(_engEnd)) rooms.Add((main[0].Z0, _engEnd));
                            if (other == top && !float.IsNaN(_bridgeStart)) rooms.Add((_bridgeStart, main[top].Z1));
                        }
                        var sectioned = main[other].Narrow ? null : Both(here, there).Where(x => x.b - x.a >= len).OrderBy(x => x.a).Cast<(float a, float b)?>().FirstOrDefault();
                        if (sectioned is null)
                        {
                            (float z, float foot)? pick = null;
                            foreach (var (ha, hb) in here)
                                foreach (var (ra, rb) in rooms)
                                    for (var zz = MathF.Ceiling(ha * 2) / 2; zz + len <= hb + 0.001f && pick is null; zz += 0.5f)
                                    {
                                        // Clear of the room's end walls and the doors in them.
                                        var foot = MathF.Max(zz + 0.3f, ra + 0.6f);
                                        if (foot + 1 <= MathF.Min(zz + len - 0.3f, rb - 0.6f)) pick = (zz, foot);
                                    }
                            if (pick is not { } p) continue;
                            link = new Link(k, other, a.Part == "superstructure", kind, p.z, len, left + Wall / 2, 1f, run, false, p.foot);
                            break;
                        }
                    }
                    var fit = Both(here, there).Where(x => x.b - x.a >= len).OrderBy(x => x.a).Cast<(float a, float b)?>().FirstOrDefault();
                    if (fit is not { } f) continue;
                    var z = MathF.Ceiling(f.a * 2) / 2;
                    if (z + len > f.b) z = f.a;
                    var w = kind == "stairs" ? Num.Clamp((hw - 1) * 0.6f, 1.4f, 1.8f) : 1f;
                    link = new Link(k, other, a.Part == "superstructure", kind, z, len, left + Wall / 2, w, run, section);
                    break;
                }
                if (link is null) continue;
                _links.Add(link);
                _annexDeck[k] = -1;
                Claim(here, link.Z, link.Z + link.Len);
                if (other < 0) Claim(there, link.Z, link.Z + link.Len);
                else if (link.Section && !(hold && other == 0) && !main[other].Narrow) AddSection(_sections, other, new Section("riser", link.Z, link.Z + link.Len, 0, link.Kind == "stairs" ? "stair_hall" : "ladder_room"));
            }
        }

        /// <summary>An elevator through every main deck on ships with three or more, beside the corridor on the port
        /// strip, in a stretch free on every deck.</summary>
        private void PlanLift(bool hold)
        {
            var decks = result.Decks;
            if (decks.Count < 3 || Kind == "utility" || decks.Any(d => d.Narrow || d.HalfWidth < 5)) return;
            if (decks.Count == 3 && !r.Chance(0.5)) return;
            var free = Free(decks[0], 1.5f);
            foreach (var d in decks.Skip(1)) free = Both(free, Free(d, 1.5f));
            var fit = free.Where(x => x.b - x.a >= LiftLen).OrderBy(x => MathF.Abs(x.a - (_stairZ0 + StairLen))).Cast<(float a, float b)?>().FirstOrDefault();
            if (fit is not { } f) return;
            _liftZ = MathF.Ceiling(f.a * 2) / 2;
            if (_liftZ + LiftLen > f.b) _liftZ = f.a;
            _liftX1 = decks.Min(d => d.HalfWidth);
            _liftX0 = _liftX1 - LiftW;
        }

        /// <summary>The elevator on one deck: a lobby off the corridor (on the hold deck, the hold itself) and the shaft
        /// against the hull, with the elevator door between them.</summary>
        private void LiftRooms(DeckInfo d, float ez, string hubId)
        {
            var fy = d.FloorY;
            var shaft = $"elevator_{d.Index}";
            Room(shaft, "elevator", d.Index, _liftX0, _liftX1, ez, ez + LiftLen);
            var lobby = hubId;
            if (hubId != "cargo_hold")
            {
                lobby = $"elevator_lobby_{d.Index}";
                Room(lobby, "elevator_lobby", d.Index, HalfCorridor, _liftX0, ez, ez + LiftLen);
                Door("opening", HubAt(d.Index, ez + LiftLen / 2), lobby, d.Index, new Vector3(HalfCorridor, fy, ez + LiftLen / 2), Vector3.UnitX, 2f);
            }
            Door("elevator", lobby, shaft, d.Index, new Vector3(_liftX0, fy, ez + LiftLen / 2), Vector3.UnitX);
        }

        /// <summary>Adds the annex decks with their rooms, and the stairs, ladders and elevator that join the decks.</summary>
        public void AddAnnexes()
        {
            var mainCount = result.Decks.Count;
            // Annex decks, in plan order, each with the stretches its links take.
            var landings = new Dictionary<int, List<(float a, float b, string kind)>>();
            foreach (var l in _links)
            {
                (landings.TryGetValue(l.Annex, out var x) ? x : landings[l.Annex] = []).Add((l.Z, l.Z + l.Len, l.Kind));
                if (l.Other < 0) (landings.TryGetValue(-1 - l.Other, out var y) ? y : landings[-1 - l.Other] = []).Add((l.Z, l.Z + l.Len, l.Kind));
            }
            foreach (var k in landings.Keys.OrderBy(k => k))
            {
                var a = annexSpecs[k];
                var idx = result.Decks.Count;
                _annexDeck[k] = idx;
                result.Decks.Add(new DeckInfo(idx, a.FloorY, a.Ceiling, a.HalfWidth, a.Z0, a.Z1, a.HalfWidth < 3.5f, a.Part, a.Cx, a.Shell != null));
                if (a.Shell != null) { _shells[idx] = a.Shell; _shellModules[idx] = a.ShellModule!; }
                result.DeckHulls[idx] = a.P;
                // Rooms are laid out about the centreline, then moved across to the block's centre.
                var nRooms = result.Rooms.Count;
                var nDoors = result.Doors.Count;
                AnnexRooms(result.Decks[idx], landings[k], a.Bridge ? BridgeLen(a) : 0);
                if (MathF.Abs(a.Cx) > 0.001f)
                {
                    for (var i = nRooms; i < result.Rooms.Count; i++)
                    {
                        var q = result.Rooms[i];
                        result.Rooms[i] = q with { Min = [q.Min[0] + a.Cx, q.Min[1], q.Min[2]], Max = [q.Max[0] + a.Cx, q.Max[1], q.Max[2]] };
                    }
                    for (var i = nDoors; i < result.Doors.Count; i++)
                    {
                        var o = result.Doors[i];
                        result.Doors[i] = o with { Center = [o.Center[0] + a.Cx, o.Center[1], o.Center[2]] };
                    }
                }
            }
            int DeckOf(int i) => i >= 0 ? i : _annexDeck[-1 - i];
            var n = 0;
            foreach (var l in _links)
            {
                var annex = result.Decks[_annexDeck[l.Annex]];
                var other = result.Decks[DeckOf(l.Other)];
                var (lo, hi) = l.Up ? (other, annex) : (annex, other);
                string RoomAt(DeckInfo d, float x, float z) =>
                    result.Rooms.Where(q => q.Deck == d.Index && q.Kind != "stairwell" && x > q.Min[0] && x < q.Max[0] && z > q.Min[2] && z < q.Max[2])
                        .OrderBy(q => (q.Max[0] - q.Min[0]) * (q.Max[2] - q.Min[2])).First().Id;
                var cx = l.X0 + l.W / 2;
                if (l.Kind == "stairs")
                {
                    var min = new Vector3(l.X0, lo.FloorY, l.Z + StairFoot);
                    var max = new Vector3(l.X0 + l.W, hi.FloorY, l.Z + StairFoot + l.Run);
                    result.Stairs.Add(new StairInfo($"stair_{lo.Part}_{n++}", lo.Index, hi.Index, RoomAt(lo, cx, l.Z + 0.5f), A(min), A(max), RoomAt(hi, cx, l.Z + l.Len - 0.5f)));
                }
                else
                {
                    var foot = float.IsNaN(l.Foot) ? l.Z + LadderFrom : l.Foot;
                    var (z0, z1) = (foot, foot + 1f);
                    var zc = (z0 + z1) / 2;
                    result.Lifts.Add(new LiftInfo($"ladder_{n++}", "ladder", [lo.Index, hi.Index], [RoomAt(lo, cx, zc), RoomAt(hi, l.X0 + 1.6f, zc)],
                        [l.X0, l.X0 + l.W, z0, z1], [A(new Vector3(l.X0 + l.W + 0.55f, lo.FloorY, zc)), A(new Vector3(l.X0 + l.W + 0.55f, hi.FloorY, zc))], []));
                }
            }
            if (_liftZ is { } ez)
            {
                var main = result.Decks.Take(mainCount).ToList();
                result.Lifts.Add(new LiftInfo("elevator", "elevator", [.. main.Select(d => d.Index)], [.. main.Select(d => $"elevator_{d.Index}")],
                    [_liftX0, _liftX1, ez, ez + LiftLen],
                    [.. main.Select(d => A(new Vector3(_liftX0 - 0.8f, d.FloorY, ez + LiftLen / 2)))],
                    [.. main.Select(d => result.Doors.First(o => o.Kind == "elevator" && o.Deck == d.Index).Id)]));
            }
        }

        /// <summary>
        /// The rooms of an annex deck: a landing across the deck wherever a link arrives, and between them either a
        /// corridor with rooms on both sides (wide decks) or rooms in a row opening into each other (narrow ones).
        /// Superstructure decks hold command and crew rooms; keel decks stores and workshops.
        /// </summary>
        /// <summary>
        /// How long an observation room at the front of a hull deck is: 5 m when the hull has glass ahead of the deck
        /// at its height (prow glass, a forward window band), 0 otherwise.
        /// </summary>
        private float ObservationLen(DeckInfo d)
        {
            if (!_shells.TryGetValue(d.Index, out var loft) || d.Narrow) return 0;
            foreach (var p in loft.Panes)
            {
                var a = loft.FacePoint(p.S, p.F, p.U0, p.V0);
                var b = loft.FacePoint(p.S, p.F, p.U1, p.V1);
                if (MathF.Min(a.Z, b.Z) < d.Z1 - 0.5f) continue;
                if (MathF.Max(a.Y, b.Y) < d.FloorY + 0.4f || MathF.Min(a.Y, b.Y) > d.CeilingY + 0.5f) continue;
                return d.Z1 - d.Z0 > 30 ? 5f : 0;
            }
            return 0;
        }

        /// <summary>Every hull deck follows the hull's own shape.</summary>
        public void HullShell(Loft loft, string module)
        {
            foreach (var d in result.Decks)
            {
                _shells[d.Index] = loft;
                _shellModules[d.Index] = module;
            }
        }

        /// <summary>A bridge block's deck ends in the bridge, across the deck at the front, when there is room for it.</summary>
        private static float BridgeLen(AnnexSpec a) => a.Z1 - a.Z0 >= 9 ? Num.Clamp(MathF.Round((a.Z1 - a.Z0) * 0.3f), 3.5f, 6f) : 0;

        private void AnnexRooms(DeckInfo d, List<(float a, float b, string kind)> landings, float bridgeLen = 0)
        {
            var hw = d.HalfWidth;
            var fy = d.FloorY;
            var program = new Program(Kind, r, d.Part);
            // Landings, stretched to the deck's end where the leftover would be too short for a room.
            var marks = landings.OrderBy(x => x.a).Select(x => (a: x.a, b: x.b, x.kind)).ToList();
            if (bridgeLen > 0 && marks[^1].b <= d.Z1 - bridgeLen + 0.01f) marks.Add((d.Z1 - bridgeLen, d.Z1, "bridge"));
            if (marks[0].a - d.Z0 < 2.5f) marks[0] = (d.Z0, marks[0].b, marks[0].kind);
            if (d.Z1 - marks[^1].b < 2.5f) marks[^1] = (marks[^1].a, d.Z1, marks[^1].kind);
            // Landings closer than a room's length meet (the earlier one reaches to the next), with an opening between.
            for (var i = 0; i + 1 < marks.Count; i++)
                if (marks[i + 1].a - marks[i].b < 2.5f) marks[i] = (marks[i].a, marks[i + 1].a, marks[i].kind);
            var ids = new List<(float a, float b, string id)>();
            foreach (var (a, b, kind) in marks)
            {
                var kindName = kind == "stairs" ? "stair_hall" : kind == "bridge" ? "bridge" : "ladder_room";
                var id = $"{kindName}_{d.Part}{d.Index}_{ids.Count}";
                Room(id, kindName, d.Index, -hw, hw, a, b);
                if (ids.Count > 0 && MathF.Abs(ids[^1].b - a) < 0.01f)
                {
                    if (kind == "bridge") Door("interior", ids[^1].id, id, d.Index, new Vector3(0, fy, a), Vector3.UnitZ);
                    else Door("opening", ids[^1].id, id, d.Index, new Vector3(0, fy, a), Vector3.UnitZ, 2f);
                }
                ids.Add((a, b, id));
            }
            // The stretches between landings, each touching at least one of them; slivers join the landing before.
            var segs = new List<(float a, float b, string? before, string? after)>();
            var cursor = d.Z0;
            string? prev = null;
            foreach (var (a, b, id) in ids)
            {
                if (a - cursor >= 2.5f) segs.Add((cursor, a, prev, id));
                cursor = b;
                prev = id;
            }
            if (d.Z1 - cursor >= 2.5f) segs.Add((cursor, d.Z1, prev, null));
            var n = 0;
            foreach (var (a, b, before, after) in segs)
            {
                if (!d.Narrow)
                {
                    var run = $"corridor_{d.Part}{d.Index}_{n++}";
                    Room(run, "corridor", d.Index, -HalfCorridor, HalfCorridor, a, b);
                    if (before != null) Door("opening", before, run, d.Index, new Vector3(0, fy, a), Vector3.UnitZ, 2f);
                    if (after != null)
                    {
                        if (after.StartsWith("bridge_")) Door("interior", run, after, d.Index, new Vector3(0, fy, b), Vector3.UnitZ);
                        else Door("opening", run, after, d.Index, new Vector3(0, fy, b), Vector3.UnitZ, 2f);
                    }
                    foreach (var side in new[] { 1f, -1f })
                        SideRooms(d, side, a, b, program, HalfCorridor, _ => run);
                    continue;
                }
                // Rooms in a row, each opening into the next, the end ones into the landings.
                var z = a;
                var last = before;
                var first = true;
                while (b - z >= 2.5f)
                {
                    var (kind, lmin, lmax) = program.Next(0, 1);
                    var l = MathF.Round(r.Range(MathF.Min(lmin, 5), MathF.Min(lmax, 8)));
                    if (b - (z + l) < 2.5f) l = b - z;
                    var id = $"{kind}_{_rooms.Count}";
                    Room(id, kind, d.Index, -hw, hw, z, z + l);
                    if (last != null) Door("interior", last, id, d.Index, new Vector3(0, fy, z), Vector3.UnitZ);
                    last = id;
                    first = false;
                    z += l;
                }
                if (!first && after != null && (before == null || last != before)) Door("interior", last!, after, d.Index, new Vector3(0, fy, b), Vector3.UnitZ);
            }
        }

        /// <summary>
        /// Real windows for shaped decks, cut where their block already has glass. A bridge at the front of a shaped deck
        /// opens forward into a nook under the block's sloped front (too low to walk in, room to look out), so the
        /// windscreen over it becomes a window too. A pane counts when it lies over the deck between its floor and the
        /// skin: side panes and chamfer panes beside a room, sloped ones over the bridge and its nook.
        /// </summary>
        private List<(float, float, float, float)> capCutsOf(DeckInfo d) =>
            [.. result.Windows.Where(w => w.Deck == d.Index && w.Module != null && w.F == 8).Select(w => (w.U0, w.U1, w.V0, w.V1))];

        public void ShapedWindows()
        {
            foreach (var (deckIndex, loft) in _shells)
            {
                var d = result.Decks[deckIndex];
                result.DeckShells[deckIndex] = (loft, _shellModules[deckIndex]);
                var hw = d.HalfWidth;
                var bridge = result.Rooms.FirstOrDefault(q => q.Deck == d.Index && q.Kind is "bridge" or "observation" && MathF.Abs(q.Max[2] - d.Z1) < 0.01f && q.Max[0] - q.Min[0] > 2 * d.HalfWidth - 0.1f);
                var end = d.Z1;
                if (bridge != null)
                {
                    // As far as a floor with a metre of headroom over its middle still fits.
                    var z = d.Z1;
                    while (z + 0.5f <= loft.Zs[^1] - 0.3f && BoxInsideLoft(loft, z + 0.5f, d.Cx - hw * 0.5f, d.Cx + hw * 0.5f, d.FloorY + 0.02f, d.FloorY + 1f)) z += 0.5f;
                    if (z > d.Z1 + 0.6f) { _nook[d.Index] = z; end = z; }
                }
                foreach (var p0 in loft.Panes)
                {
                    // The part of the pane the deck can see: between its floor (plus a sill) and just over its
                    // ceiling, and along its length. Glass partly below the floor or past the deck's end is cut to that.
                    var (zs0, zs1) = (loft.Zs[p0.S], loft.Zs[p0.S + 1]);
                    if (zs1 - zs0 < 1e-3f) continue;
                    float UOf(float z) => (z - zs0) / (zs1 - zs0);
                    var u0 = MathF.Max(p0.U0, UOf(d.Z0 + 0.15f));
                    var u1 = MathF.Min(p0.U1, UOf(end - 0.15f));
                    if (u1 - u0 < 1e-3f) continue;
                    var um = (u0 + u1) / 2;
                    float ya = loft.FacePoint(p0.S, p0.F, um, 0).Y, yb = loft.FacePoint(p0.S, p0.F, um, 1).Y;
                    float v0 = p0.V0, v1 = p0.V1;
                    if (MathF.Abs(yb - ya) > 1e-3f)
                    {
                        float VOf(float y) => (y - ya) / (yb - ya);
                        var (vLo, vHi) = (VOf(d.FloorY + 0.4f), VOf(d.CeilingY + SkinInset + 0.25f));
                        if (vLo > vHi) (vLo, vHi) = (vHi, vLo);
                        v0 = MathF.Max(v0, vLo);
                        v1 = MathF.Min(v1, vHi);
                    }
                    else if (ya < d.FloorY + 0.4f || ya > d.CeilingY + SkinInset + 0.25f) continue;
                    if (v1 - v0 < 1e-3f) continue;
                    var p = (p0.S, p0.F, U0: u0, U1: u1, V0: v0, V1: v1);
                    var corners = new[] { loft.FacePoint(p.S, p.F, p.U0, p.V0), loft.FacePoint(p.S, p.F, p.U1, p.V0), loft.FacePoint(p.S, p.F, p.U1, p.V1), loft.FacePoint(p.S, p.F, p.U0, p.V1) };
                    // Big enough to be a window.
                    if (Vector3.Distance(corners[0], corners[1]) < 0.5f || Vector3.Distance(corners[0], corners[3]) < 0.4f) continue;
                    float zMin = corners.Min(c => c.Z), zMax = corners.Max(c => c.Z), yMin = corners.Min(c => c.Y), yMax = corners.Max(c => c.Y);
                    var n = loft.FaceNormal(p.S, p.F);
                    var centre = corners.Aggregate(Vector3.Zero, (a, c) => a + c) / 4;
                    var side = MathF.Abs(n.Y) > 0.6f ? 0 : MathF.Sign(n.X);
                    var room = result.Rooms.Where(q => q.Deck == d.Index && centre.Z >= q.Min[2] - 0.01f && centre.Z <= q.Max[2] + 0.01f)
                        .OrderBy(q => side == 0 ? MathF.Abs((q.Min[0] + q.Max[0]) / 2 - centre.X) : MathF.Abs((side > 0 ? q.Max[0] : q.Min[0]) - centre.X)).FirstOrDefault()
                        ?? bridge;
                    // Glass over a store or machinery stays painted, as on the hull.
                    if (room is null || Blind.Contains(room.Kind)) continue;
                    // One deck per pane: a pane already cut for the deck below stays with it.
                    if (result.Windows.Any(w => w.Module == _shellModules[deckIndex] && w.S == p.S && w.F == p.F && w.U0 < p.U1 && w.U1 > p.U0 && w.V0 < p.V1 && w.V1 > p.V0)) continue;
                    result.Windows.Add(new WindowInfo($"window_{result.Windows.Count}", room.Id, d.Index, side, zMin, zMax, yMin, yMax, centre.X,
                        _shellModules[deckIndex], p.S, p.F, p.U0, p.U1, p.V0, p.V1, Painted: true));
                }
                // Glass across a flat front (the end cap), when the deck reaches the front: seen through the front
                // wall of the room there. (F = 8 marks the cap; U is x and V is y on it.)
                var capZ = loft.Zs[^1];
                if (end >= capZ - 1.3f)
                    foreach (var cp in loft.CapPanes)
                    {
                        float y0 = MathF.Max(cp.Y0, d.FloorY + 0.4f), y1 = MathF.Min(cp.Y1, d.CeilingY - 0.1f);
                        float x0 = MathF.Max(cp.X0, d.Cx - hw + 0.2f), x1 = MathF.Min(cp.X1, d.Cx + hw - 0.2f);
                        if (y1 - y0 < 0.4f || x1 - x0 < 0.4f) continue;
                        var front = result.Rooms.Where(q => q.Deck == d.Index && q.Max[2] >= end - 0.01f - (_nook.ContainsKey(d.Index) ? end - d.Z1 : 0)
                                && (x0 + x1) / 2 >= q.Min[0] - 0.01f && (x0 + x1) / 2 <= q.Max[0] + 0.01f).OrderByDescending(q => q.Max[2]).FirstOrDefault();
                        if (front is null || Blind.Contains(front.Kind)) continue;
                        result.Windows.Add(new WindowInfo($"window_{result.Windows.Count}", front.Id, d.Index, 0, capZ, capZ, y0, y1, end,
                            _shellModules[deckIndex], -1, 8, x0, x1, y0, y1, Painted: true));
                    }
            }
        }

        /// <summary>
        /// Numbers the decks from the bottom up (the keel deck first, superstructure decks last), and renumbers every
        /// room, door, stair, window and lift to match.
        /// </summary>
        public void SortDecks()
        {
            var order = result.Decks.OrderBy(d => d.FloorY).ThenBy(d => d.Z0).Select(d => d.Index).ToList();
            var map = order.Select((old, i) => (old, i)).ToDictionary(x => x.old, x => x.i);
            if (map.All(kv => kv.Key == kv.Value)) return;
            var decks = result.Decks.Select(d => d with { Index = map[d.Index] }).OrderBy(d => d.Index).ToList();
            result.Decks.Clear(); result.Decks.AddRange(decks);
            var rooms = result.Rooms.Select(q => q with { Deck = map[q.Deck] }).ToList();
            result.Rooms.Clear(); result.Rooms.AddRange(rooms);
            var doors = result.Doors.Select(q => q with { Deck = map[q.Deck] }).ToList();
            result.Doors.Clear(); result.Doors.AddRange(doors);
            var stairs = result.Stairs.Select(q => q with { FromDeck = map[q.FromDeck], ToDeck = map[q.ToDeck] }).ToList();
            result.Stairs.Clear(); result.Stairs.AddRange(stairs);
            var windows = result.Windows.Select(q => q with { Deck = map[q.Deck] }).ToList();
            result.Windows.Clear(); result.Windows.AddRange(windows);
            var lifts = result.Lifts.Select(q => q with { Decks = [.. q.Decks.Select(x => map[x])] }).ToList();
            result.Lifts.Clear(); result.Lifts.AddRange(lifts);
            var hulls = result.DeckHulls.ToDictionary(kv => map[kv.Key], kv => kv.Value);
            result.DeckHulls.Clear(); foreach (var kv in hulls) result.DeckHulls[kv.Key] = kv.Value;
            var shells = _shells.ToDictionary(kv => map[kv.Key], kv => kv.Value);
            _shells.Clear(); foreach (var kv in shells) _shells[kv.Key] = kv.Value;
            var shellMods = _shellModules.ToDictionary(kv => map[kv.Key], kv => kv.Value);
            _shellModules.Clear(); foreach (var kv in shellMods) _shellModules[kv.Key] = kv.Value;
            var voids = _voids.ToDictionary(kv => map[kv.Key], kv => kv.Value);
            _voids.Clear(); foreach (var kv in voids) _voids[kv.Key] = kv.Value;
        }

        /// <summary>Builds one special section's rooms, its openings to the corridor runs on both ends, and the rooms
        /// along its sides.</summary>
        private void BuildSection(DeckInfo d, Section sec, Program program)
        {
            var (a, b, c) = (sec.A, sec.B, sec.C);
            var fy = d.FloorY;
            var before = HubAt(d.Index, a - 0.01f);
            var after = HubAt(d.Index, b + 0.01f);
            if (sec.Mode == "riser")
            {
                // A room across the deck where stairs or a ladder go up to (or down from) a deck outside the hull.
                var rid = $"{sec.Kind}_{d.Index}_{_rooms.Count}";
                Room(rid, sec.Kind, d.Index, -d.HalfWidth, d.HalfWidth, a, b);
                Door("opening", before, rid, d.Index, new Vector3(0, fy, a), Vector3.UnitZ, 2f);
                Door("opening", rid, after, d.Index, new Vector3(0, fy, b), Vector3.UnitZ, 2f);
                return;
            }
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
                var hall = result.Rooms.Last(x => x.Deck == d.Index - 1 && x.Kind == _sections[d.Index - 1].First(q => q.Mode == "hall").Kind);
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

        /// <summary>Rooms that get no windows: stores, holds, machinery, stairs, lifts, the armoury and the brig.</summary>
        private static readonly string[] Blind = ["storage", "reactor", "life_support", "cargo_hold", "engineering", "engine_room", "stairwell", "airlock", "armoury", "brig", "elevator", "workshop"];

        /// <summary>Window candidates along the hull walls of rooms where people are (not stores, machinery or stairs).</summary>
        public void Windows(Func<WindowInfo, WindowInfo?> accept)
        {
            var blind = Blind;
            var n = 0;
            foreach (var room in result.Rooms)
            {
                if (blind.Contains(room.Kind)) continue;
                var d = result.Decks[room.Deck];
                foreach (var side in new[] { 1, -1 })
                {
                    var wallX = d.Cx + side * d.HalfWidth;
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
                        if (accept(w) is { } got) { result.Windows.Add(got); n++; }
                    }
                }
            }
        }

        // ------------------------------------------------------------------ geometry

        public void Geometry(Ship model, Module root)
        {
            var topHull = result.Decks.Where(x => x.Part == "hull").Max(x => x.Index);
            bool HasRoof(DeckInfo x) => x.Part != "hull" || x.Index == topHull;
            // Every connector between two decks: (lower deck, upper deck, footprint x0, x1, z0, z1). Stairs from the
            // stairwell and the grand staircase are handled on their own (the stairwell's hole runs under the wall,
            // the grand staircase rises through the void).
            var risers = new List<(int lo, int hi, float x0, float x1, float z0, float z1)>();
            foreach (var st in result.Stairs.Where(x => x.ToRoom != null && !x.Id.StartsWith("grand_")))
                risers.Add((st.FromDeck, st.ToDeck, st.Min[0], st.Max[0], st.Min[2], st.Max[2]));
            foreach (var l in result.Lifts)
                for (var i = 0; i + 1 < l.Decks.Length; i++)
                    risers.Add((l.Decks[i], l.Decks[i + 1], l.Shaft[0], l.Shaft[1], l.Shaft[2], l.Shaft[3]));
            float HoleX0(DeckInfo x, float x0) => MathF.Abs(x0 - Wall / 2 - (x.Cx - x.HalfWidth)) < 0.05f || MathF.Abs(x0 - Wall / 2 - StairX(x)) < 0.05f ? x0 - Wall / 2 : x0;
            // Between a deck's roof and the floor of the deck above (on top of the hull, or a gondola's trunk), a
            // shaft round each opening going up.
            void Shafts(DeckInfo d, Module deck)
            {
                foreach (var x in risers.Where(x => x.lo == d.Index))
                {
                    var hi = result.Decks[x.hi];
                    if (hi.FloorY - Slab - (d.CeilingY + Slab) < 0.05f) continue;
                    var shaft = Add(model, $"shaft_{d.Index}_{x.hi}_{x.z0:0}", "walls", deck.Id);
                    var (ya, yb) = (d.CeilingY, hi.FloorY - Slab);
                    Shapes.BoxMinMax(shaft.Mesh, WallMat, new Vector3(x.x0 - Wall, ya, x.z0 - Wall), new Vector3(x.x0, yb, x.z1 + Wall));
                    Shapes.BoxMinMax(shaft.Mesh, WallMat, new Vector3(x.x1, ya, x.z0 - Wall), new Vector3(x.x1 + Wall, yb, x.z1 + Wall));
                    Shapes.BoxMinMax(shaft.Mesh, WallMat, new Vector3(x.x0, ya, x.z0 - Wall), new Vector3(x.x1, yb, x.z0));
                    Shapes.BoxMinMax(shaft.Mesh, WallMat, new Vector3(x.x0, ya, x.z1), new Vector3(x.x1, yb, x.z1 + Wall));
                }
            }
            foreach (var d in result.Decks)
            {
                var deck = Add(model, $"deck_{d.Index}", "deck", root.Id);
                deck.Extras = new() { ["deck"] = d.Index, ["floor_y"] = d.FloorY };
                var hw = d.HalfWidth;
                // Floor (with a hole where the stairs from the deck below arrive) and, over the top deck, a roof.
                // The floor above a flight is open over its whole length, so nobody bumps their head on the way up.
                // The hole is exactly the flight's footprint (running under the wall on the hull side): slivers of
                // the floor below showing beside a landing make navigation meshes join the two decks wrongly.
                var holes = result.Stairs.Where(s => s.ToDeck == d.Index && s.ToRoom is null).Select(s => (HoleX0(d, s.Min[0]), s.Max[0], s.Min[2], s.Max[2])).ToList();
                var voids = _voids.GetValueOrDefault(d.Index) ?? [];
                holes.AddRange(voids);
                holes.AddRange(risers.Where(x => x.hi == d.Index).Select(x => (HoleX0(d, x.x0), x.x1, x.z0, x.z1)));
                SlabWithHoles(deck.Mesh, Floor, d.Cx - hw - Wall, d.Cx + hw + Wall, d.Z0 - Wall, _nook.TryGetValue(d.Index, out var floorEnd) ? floorEnd : d.Z1 + Wall, d.FloorY - Slab, d.FloorY, holes);
                _shells.TryGetValue(d.Index, out var shell);
                if (shell != null)
                {
                    // The block's own shape: skin for outer walls, slanted corners and sloped ends; a flat ceiling
                    // where the block is taller than the deck.
                    var skinWalls = Add(model, $"skin_{d.Index}", "walls", deck.Id);
                    var skinOver = Add(model, $"ceiling_{d.Index}", "ceiling", deck.Id);
                    var cuts = result.Windows.Where(w => w.Deck == d.Index && w.Module != null && w.F < 8)
                        .Select(w => (w.F, shell.Zs[w.S] + (shell.Zs[w.S + 1] - shell.Zs[w.S]) * w.U0, shell.Zs[w.S] + (shell.Zs[w.S + 1] - shell.Zs[w.S]) * w.U1, w.V0, w.V1)).ToList();
                    var skinEnd = _nook.TryGetValue(d.Index, out var nookEnd) ? nookEnd : d.Z1 + Wall;
                    var up = risers.Where(x => x.lo == d.Index).Select(x => (HoleX0(d, x.x0), x.x1, x.z0, x.z1)).ToList();
                    // In a stack of hull decks, the skin of a deck reaches up to the floor of the deck above, which
                    // then shows its underside as this deck's ceiling.
                    var above = result.Decks.FirstOrDefault(x => x.Part == "hull" && d.Part == "hull" && MathF.Abs(x.FloorY - d.CeilingY - Slab) < 0.05f);
                    var below = result.Decks.FirstOrDefault(x => x.Part == "hull" && d.Part == "hull" && MathF.Abs(d.FloorY - x.CeilingY - Slab) < 0.05f);
                    Skin.Build(skinWalls.Mesh, skinOver.Mesh, SkinMat, Floor, shell, SkinInset, d.Z0 - Wall, skinEnd, d.FloorY, d.CeilingY, cuts, up, holes,
                        clipTop: above != null ? above.FloorY - Skin.Underside : null, flatCeiling: above == null, floorUnderside: below != null);
                    Shafts(d, deck);
                    var capCuts = result.Windows.Where(w => w.Deck == d.Index && w.Module != null && w.F == 8).Select(w => (w.U0, w.U1, w.V0, w.V1)).ToList();
                    if (_nook.ContainsKey(d.Index))
                        Skin.WallAcross(skinWalls.Mesh, WallMat, shell, SkinInset, nookEnd, -1e4f, 1e4f, d.FloorY, d.CeilingY, Wall, capCuts);
                }
                else if (HasRoof(d))
                {
                    // Over the top deck of the hull and every deck outside it: a roof, open where a connector goes up.
                    var roof = Add(model, $"ceiling_{d.Index}", "ceiling", deck.Id);
                    var up = risers.Where(x => x.lo == d.Index).Select(x => (HoleX0(d, x.x0), x.x1, x.z0, x.z1)).ToList();
                    SlabWithHoles(roof.Mesh, WallMat, d.Cx - hw - Wall, d.Cx + hw + Wall, d.Z0 - Wall, d.Z1 + Wall, d.CeilingY, d.CeilingY + Slab, up);
                    Shafts(d, deck);
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
                        if (shell != null)
                        {
                            // Outer walls are the skin; the rest follow it at their tops and ends.
                            if (axis == 'z' && MathF.Abs(MathF.Abs(at - d.Cx) - hw) < 0.05f)
                            {
                                // The outer wall is the skin, except round an outer airlock door: a framed wall section there.
                                foreach (var o in doors.Where(o => o.Kind == "airlock_outer" && MathF.Abs(o.Center[0] - at) < 0.05f && o.Center[2] > a && o.Center[2] < b))
                                    WallAlongZ(walls.Mesh, at, MathF.Max(a, o.Center[2] - 1.5f), MathF.Min(b, o.Center[2] + 1.5f), d, doors);
                                continue;
                            }
                            // The bridge's front stays open to the nook and its windscreen.
                            if (axis == 'x' && _nook.ContainsKey(d.Index) && MathF.Abs(at - d.Z1) < 0.05f) continue;
                            if (axis == 'z') WallAlongZ(walls.Mesh, at, a, b, d, doors, (m0, x, za, zb, y0, y1) => Skin.WallAlong(m0, WallMat, shell, SkinInset, x, za, zb, y0, y1, Wall));
                            else WallAlongX(walls.Mesh, at, a, b, d, doors, (m0, z, xa, xb, y0, y1) => Skin.WallAcross(m0, WallMat, shell, SkinInset, z,
                                MathF.Abs(xa - (d.Cx - hw)) < 0.05f ? -1e4f : xa, MathF.Abs(xb - (d.Cx + hw)) < 0.05f ? 1e4f : xb, y0, y1, Wall,
                                // The front wall, with the front glass ahead: openings to look out through.
                                !_nook.ContainsKey(d.Index) && MathF.Abs(z - d.Z1) < 0.05f ? capCutsOf(d) : null));
                            continue;
                        }
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
                    else if (shell != null)
                    {
                        // Under a sloped end the light hangs from the skin, not from the flat ceiling height.
                        var top = Skin.TopAt(Skin.Inset(Skin.SectionAt(shell, box.Center.Z), SkinInset), box.Center.X);
                        var y1 = float.IsNaN(top) ? box.Max.Y : MathF.Min(box.Max.Y, top - 0.05f);
                        Lights(m.Mesh, new Aabb(box.Min, new Vector3(box.Max.X, y1, box.Max.Z)), room.Kind);
                    }
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
                    Shapes.Box(m.Mesh, door.Kind is "airlock_outer" ? Roles.Hazard : DoorMat, leafC, size);
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
                    var steps = (int)MathF.Ceiling((st.Max[1] - st.Min[1]) / 0.25f - 0.01f);
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
            foreach (var l in result.Lifts) LiftGeometry(model, root, l);
            Furniture.Place(model, result, _shells, SkinInset, _voids,
                model.Markers.Where(x => x.Kind == "room").ToDictionary(x => x.Id["room_".Length..], x => x.Position));
            model.Markers.Add(new Marker("spawn_0", "spawn", "interior", SpawnPoint(), Vector3.UnitZ, 0.5f, 0));
            model.Finish();
        }

        /// <summary>
        /// A ladder (rails and rungs against the wall, from the lower floor to a metre over the hatch, with a rail round
        /// the hatch on the upper deck), or an elevator car (a platform with side rails and a light, at the lowest stop).
        /// Both are nodes with what an engine needs in the extras: the stops, where people get on and off, the doors.
        /// </summary>
        private void LiftGeometry(Ship model, Module root, LiftInfo l)
        {
            var (x0, x1, z0, z1) = (l.Shaft[0], l.Shaft[1], l.Shaft[2], l.Shaft[3]);
            var lo = result.Decks[l.Decks[0]];
            var hi = result.Decks[l.Decks[^1]];
            var parent = $"deck_{lo.Index}";
            var m = Add(model, l.Id, l.Kind, parent);
            m.Extras = new()
            {
                ["kind"] = l.Kind, ["decks"] = l.Decks, ["rooms"] = l.Rooms, ["access"] = l.Access,
                ["stops_y"] = l.Decks.Select(i => result.Decks[i].FloorY).ToArray(), ["shaft"] = l.Shaft,
            };
            if (l.Kind == "ladder")
            {
                var px = x0 + 0.18f;
                m.Pivot = new Vector3(px, lo.FloorY, (z0 + z1) / 2);
                foreach (var z in new[] { z0 + 0.22f, z1 - 0.22f })
                    Shapes.Beam(m.Mesh, Trim, new Vector3(px, lo.FloorY, z), new Vector3(px, hi.FloorY + 1.1f, z), 0.04f);
                for (var y = lo.FloorY + 0.3f; y < hi.FloorY + 0.9f; y += 0.3f)
                    Shapes.Beam(m.Mesh, Trim, new Vector3(px, y, z0 + 0.22f), new Vector3(px, y, z1 - 0.22f), 0.025f);
                m.Extras["top"] = l.Access[^1];
                m.Extras["bottom"] = l.Access[0];
                var rail = Add(model, l.Id + "_rail", "railing", $"deck_{hi.Index}");
                foreach (var z in new[] { z0, z1 })
                    Shapes.Beam(rail.Mesh, Trim, new Vector3(x0, hi.FloorY + 1f, z), new Vector3(x1, hi.FloorY + 1f, z), 0.04f);
                foreach (var (x, z) in new[] { (x1, z0), (x1, z1) })
                    Shapes.Beam(rail.Mesh, Trim, new Vector3(x, hi.FloorY, z), new Vector3(x, hi.FloorY + 1f, z), 0.035f);
                model.Markers.Add(new Marker(l.Id, "ladder", m.Id, m.Pivot.Value, Vector3.UnitY, 0.5f, 0));
                return;
            }
            // The car: it starts at the lowest stop; an engine moves it between stops_y.
            var cx = (x0 + x1) / 2;
            var cz = (z0 + z1) / 2;
            m.Pivot = new Vector3(cx, lo.FloorY, cz);
            m.Extras["doors"] = l.Doors;
            var (a0, a1, b0, b1) = (x0 + 0.12f, x1 - 0.12f, z0 + 0.12f, z1 - 0.12f);
            Shapes.BoxMinMax(m.Mesh, Floor, new Vector3(a0, lo.FloorY - 0.2f, b0), new Vector3(a1, lo.FloorY, b1));
            Shapes.BoxMinMax(m.Mesh, Roles.Hazard, new Vector3(a0, lo.FloorY, b0), new Vector3(a0 + 0.12f, lo.FloorY + 0.02f, b1));
            foreach (var z in new[] { b0, b1 - 0.06f })
                Shapes.BoxMinMax(m.Mesh, DoorMat, new Vector3(a0 + 0.3f, lo.FloorY, z), new Vector3(a1, lo.FloorY + 1.05f, z + 0.06f));
            Shapes.BoxMinMax(m.Mesh, DoorMat, new Vector3(a1 - 0.06f, lo.FloorY, b0), new Vector3(a1, lo.FloorY + 1.05f, b1));
            Shapes.Box(m.Mesh, Roles.Light, new Vector3(cx, lo.FloorY + 2.6f, cz), new Vector3(1.2f, 0.06f, 0.6f));
            foreach (var (x, z) in new[] { (a0 + 0.3f, b0), (a0 + 0.3f, b1), (a1, b0), (a1, b1) })
                Shapes.Beam(m.Mesh, DoorMat, new Vector3(x, lo.FloorY, z), new Vector3(x, lo.FloorY + 2.7f, z), 0.04f);
            Shapes.BoxMinMax(m.Mesh, DoorMat, new Vector3(a0 + 0.3f, lo.FloorY + 2.7f, b0), new Vector3(a1, lo.FloorY + 2.76f, b1));
            model.Markers.Add(new Marker(l.Id, "elevator", m.Id, m.Pivot.Value, Vector3.UnitY, 1f, 0));
        }

        private Vector3 SpawnPoint()
        {
            var room = result.Rooms.FirstOrDefault(x => x.Kind == "corridor") ?? result.Rooms[0];
            return new Vector3((room.Min[0] + room.Max[0]) / 2, room.Min[1] + 0.05f, (room.Min[2] + room.Max[2]) / 2);
        }

        private static void Lights(Mesh m, Aabb box, string kind)
        {
            var y = box.Max.Y - 0.06f;
            // A ladder room's hatch may be in the middle of the ceiling: its light goes beside it.
            if (kind == "ladder_room")
            {
                Shapes.Box(m, Roles.Light, new Vector3(box.Max.X - 0.45f, y, box.Center.Z), new Vector3(0.3f, 0.06f, MathF.Min(1.6f, box.Size.Z * 0.5f)));
                return;
            }
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
        private void WallAlongZ(Mesh m, float x, float z0, float z1, DeckInfo d, List<DoorInfo> doors,
            Action<Mesh, float, float, float, float, float>? shaped = null)
        {
            var openings = doors.Where(o => MathF.Abs(o.Center[0] - x) < 0.05f && MathF.Abs(o.Normal[0]) > 0.5f && o.Center[2] > z0 && o.Center[2] < z1)
                .Select(o => (c: o.Center[2], w: o.Width, lo: 0f, hi: o.Height, frame: o.Kind != "opening")).ToList();
            // Windows in the hull walls: an opening between sill and head.
            foreach (var win in _windows.Where(v => v.Deck == d.Index && v.Module == null && MathF.Abs(d.Cx + v.Side * d.HalfWidth - x) < 0.05f && v.Z0 > z0 - 0.01f && v.Z1 < z1 + 0.01f))
                openings.Add(((win.Z0 + win.Z1) / 2, win.Z1 - win.Z0, win.Y0 - d.FloorY, win.Y1 - d.FloorY, false));
            openings.Sort((p, q) => p.c.CompareTo(q.c));
            Segments(z0, z1, openings, (a, b, y0, y1) =>
                {
                    if (shaped != null) shaped(m, x, a, b, d.FloorY + y0, d.FloorY + y1);
                    else Shapes.BoxMinMax(m, WallMat, new Vector3(x - Wall / 2, d.FloorY + y0, a), new Vector3(x + Wall / 2, d.FloorY + y1, b));
                },
                d.CeilingY - d.FloorY, (c) => Frame(m, new Vector3(x, d.FloorY, c), alongX: false));
        }

        /// <summary>A wall at z = <paramref name="z"/> running along X from x0 to x1, with openings for the doors in it.</summary>
        private void WallAlongX(Mesh m, float z, float x0, float x1, DeckInfo d, List<DoorInfo> doors,
            Action<Mesh, float, float, float, float, float>? shaped = null)
        {
            var openings = doors.Where(o => MathF.Abs(o.Center[2] - z) < 0.05f && MathF.Abs(o.Normal[2]) > 0.5f && o.Center[0] > x0 && o.Center[0] < x1)
                .Select(o => (c: o.Center[0], w: o.Width, lo: 0f, hi: o.Height, frame: o.Kind != "opening")).OrderBy(v => v.c).ToList();
            Segments(x0, x1, openings, (a, b, y0, y1) =>
                {
                    if (shaped != null) shaped(m, z, a, b, d.FloorY + y0, d.FloorY + y1);
                    else Shapes.BoxMinMax(m, WallMat, new Vector3(a, d.FloorY + y0, z - Wall / 2), new Vector3(b, d.FloorY + y1, z + Wall / 2));
                },
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
            "elevator_lobby" or "stair_hall" or "ladder_room" => null,
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
    private sealed class Program(string archetype, Rng r, string part = "hull")
    {
        private bool _mess, _med;

        public (string kind, float lmin, float lmax) Next(int deck, int decks)
        {
            // Decks outside the hull: command and crew rooms up in the superstructure, stores and workshops in the keel.
            if (part != "hull")
            {
                (string, double)[] annex = part == "gondola"
                    ? archetype switch
                    {
                        "research" or "ring" => [("observation", 3), ("lab", 2), ("comms", 1)],
                        "freighter" or "industrial" => [("cargo_hold", 3), ("storage", 2), ("workshop", 1)],
                        "civilian" => [("lounge", 3), ("observation", 2), ("galley", 1)],
                        _ => [("observation", 2), ("storage", 2), ("workshop", 1), ("comms", 1)],
                    }
                    : part == "keel"
                    ? [("storage", 3), ("workshop", 2), ("cargo_hold", 1.5), ("life_support", 1), ("armoury", archetype is "warship" or "patrol" or "citadel" or "raider" ? 1.5 : 0.2)]
                    : [("observation", 2), ("comms", 1.5), ("captain_cabin", 1), ("briefing", 1), ("crew_quarters", 2), (archetype == "civilian" ? "lounge" : "lab", archetype is "civilian" or "research" or "ring" ? 2 : 0.3)];
                var pick = r.Weighted(annex);
                return pick is "crew_quarters" or "captain_cabin" ? (pick, 4, 6) : (pick, 4, 8);
            }
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

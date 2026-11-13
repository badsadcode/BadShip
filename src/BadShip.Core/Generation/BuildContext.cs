using System.Numerics;
using BadShip.Geometry;
using BadShip.Model;

namespace BadShip.Generation;

/// <summary>A flat top surface that equipment can stand on, with the footprints already taken.</summary>
public sealed class Deck
{
    public required string ModuleId { get; init; }
    public float Y { get; init; }
    public float X0 { get; init; }
    public float X1 { get; init; }
    public float Z0 { get; init; }
    public float Z1 { get; init; }

    /// <summary>0 for the main hull, higher for superstructure tiers.</summary>
    public int Level { get; init; }

    /// <summary>An underside: things placed here hang down from <see cref="Y"/> instead of standing on it.</summary>
    public bool Underside { get; init; }

    public List<(float x0, float x1, float z0, float z1)> Used { get; } = [];

    public float Width => X1 - X0;
    public float Length => Z1 - Z0;

    public bool Free(float x0, float x1, float z0, float z1, float pad) =>
        x0 >= X0 && x1 <= X1 && z0 >= Z0 && z1 <= Z1 &&
        Used.All(u => x1 + pad <= u.x0 || x0 - pad >= u.x1 || z1 + pad <= u.z0 || z0 - pad >= u.z1);

    public void Take(float x0, float x1, float z0, float z1) => Used.Add((x0, x1, z0, z1));

    /// <summary>Finds a free spot for a w × d footprint (random tries), takes it and returns its centre.</summary>
    public bool TryPlace(Rng r, float w, float d, float pad, out float cx, out float cz, float? centerX = null)
    {
        for (var attempt = 0; attempt < 24; attempt++)
        {
            cx = centerX ?? r.Range(X0 + w / 2, X1 - w / 2);
            cz = r.Range(Z0 + d / 2, Z1 - d / 2);
            if (w > Width || d > Length) break;
            if (!Free(cx - w / 2, cx + w / 2, cz - d / 2, cz + d / 2, pad)) continue;
            Take(cx - w / 2, cx + w / 2, cz - d / 2, cz + d / 2);
            return true;
        }
        cx = cz = 0;
        return false;
    }
}

/// <summary>The underside of a hull mass that ventral structures can hang from.</summary>
public sealed record BellySpan(Module Module, Profile Profile, float Z0, float Z1, bool Main);

/// <summary>A side face of a hull loft where bays, docking collars and windows can go.</summary>
public sealed class SideSurface
{
    public required Module Module { get; init; }
    public required Loft Loft { get; init; }
    public int Segment { get; init; }
    public int Face { get; init; }
    public string Key => $"{Module.Id}:{Segment}:{Face}";
}

/// <summary>A lofted module the detail pass may cover in plates, vents and lights.</summary>
public sealed record LoftPart(Module Module, Loft Loft, string Kind);

/// <summary>Everything a generation run shares: the plan, the ship being built, random streams and placement registries.</summary>
public sealed class BuildContext
{
    public ShipRecipe Recipe { get; }
    public ShipPlan Plan { get; } = new();
    public Ship Ship { get; }

    // Independent random streams per stage (see docs): equipment or detail changes never move the hull.
    public Rng PlanRng { get; }
    public Rng Shape { get; }
    public Rng Equip { get; }
    public Rng Detail { get; }
    public Rng Look { get; }
    public Rng VentralRng { get; }

    /// <summary>0 none, 1 some, 2 full surface detail.</summary>
    public int DetailLevel { get; }

    public List<Deck> Decks { get; } = [];
    public List<BellySpan> Bellies { get; } = [];

    /// <summary>The nacelle length the last engine assembly used (twin hulls match their engines).</summary>
    public float? LastNacelleLength { get; set; }
    public float? LastBayLength { get; set; }

    /// <summary>
    /// How big surface features (plates, vents, breaches) are relative to the usual: 1 for ships, much less for
    /// fighters, so a 12 m craft gets hand-sized panels and holes rather than ship-sized ones.
    /// </summary>
    public float DetailScale => Plan.Family switch
    {
        "utility" => Num.Clamp(Plan.Length / 70f, 0.22f, 0.55f),
        "fighter" => Num.Clamp(Plan.Length / 55f, 0.16f, 0.8f),
        "citadel" => Num.Clamp(Plan.Length / 260f, 1f, 2.6f),
        _ => 1f,
    };

    /// <summary>A colour scheme the ship type prefers when no palette is asked for (industrial for work ships, ...).</summary>
    public string? PreferredPalette { get; set; }

    /// <summary>Fighters: how blocky the cross-section is (0 knife-edged stealth … 1 armoured chamfered block).</summary>
    public float Bulk { get; set; }

    /// <summary>The manufacturer's design language (set by the planner).</summary>
    public DesignLanguage Lang { get; set; } = new();

    /// <summary>An equipment count for this ship's size class: small craft carry fewer items, large ships more.</summary>
    public int Count(int min, int max) => Plan.Size switch
    {
        "small" => Equip.Int((min + 1) / 2, Math.Max(1, (max + 1) / 2)),
        "large" => Equip.Int(min + max / 2, max + max / 2 + 1),
        _ => Equip.Int(min, max),
    };
    public List<SideSurface> Sides { get; } = [];
    public List<LoftPart> Lofts { get; } = [];

    /// <summary>Taken (u0, u1, v0, v1) rectangles per loft face, so windows, bays and plates don't overlap.</summary>
    private readonly Dictionary<string, List<(float u0, float u1, float v0, float v1)>> _surfaceUse = [];

    public BuildContext(ShipRecipe recipe)
    {
        Recipe = recipe;
        Ship = new Ship { Recipe = recipe, Plan = Plan };
        PlanRng = StageRng(recipe, "plan");
        Shape = StageRng(recipe, "silhouette");
        Equip = StageRng(recipe, "equipment");
        Detail = StageRng(recipe, "details");
        Look = StageRng(recipe, "look");
        VentralRng = StageRng(recipe, "ventral");
        DetailLevel = recipe.Detail switch { "low" => 0, "medium" => 1, _ => 2 };
    }

    public static readonly string[] Stages = ["plan", "silhouette", "ventral", "equipment", "livery", "condition", "details", "look"];

    private static Rng StageRng(ShipRecipe recipe, string stage)
    {
        if (recipe.StageSeeds is { } seeds)
        {
            foreach (var key in seeds.Keys)
                if (!Stages.Contains(key)) throw new ArgumentException($"Unknown stage '{key}'. Use one of: {string.Join(", ", Stages)}.");
            if (seeds.TryGetValue(stage, out var s)) return Rng.Stream(s, stage);
        }
        return Rng.Stream(recipe.Seed, stage);
    }

    public Module Add(string id, string type, ModuleTier tier, string? parentId)
    {
        var unique = id;
        for (var i = 2; Ship.Modules.Any(m => m.Id == unique); i++) unique = $"{id}_{i}";
        var m = new Module { Id = unique, Type = type, Tier = tier, ParentId = parentId };
        Ship.Modules.Add(m);
        return m;
    }

    /// <summary>The tertiary child that holds a module's surface detail (so detail can be toggled per module).</summary>
    /// <summary>The tertiary child that holds a module's damage (breaches, scorch, torn plates).</summary>
    public Module DamageOf(Module m)
    {
        var id = m.Id + ".damage";
        return Ship.Find(id) ?? Add(id, "damage", ModuleTier.Tertiary, m.Id);
    }

    /// <summary>The tertiary child that holds a module's livery decals.</summary>
    public Module LiveryOf(Module m)
    {
        var id = m.Id + ".livery";
        return Ship.Find(id) ?? Add(id, "livery", ModuleTier.Tertiary, m.Id);
    }

    public Module DetailsOf(Module m)
    {
        var id = m.Id + ".details";
        return Ship.Find(id) ?? Add(id, "details", ModuleTier.Tertiary, m.Id);
    }

    public (Module module, Loft loft) AddLoft(string id, string type, ModuleTier tier, string? parentId,
        IEnumerable<(float z, Profile p)> rings, string material, string? partKind = "hull", Action<Loft>? setup = null)
    {
        var m = Add(id, type, tier, parentId);
        var loft = new Loft(rings);
        setup?.Invoke(loft);
        loft.Build(m.Mesh, material);
        m.Lofts.Add(loft);
        if (partKind != null) Lofts.Add(new LoftPart(m, loft, partKind));
        m.UpdateBounds();
        return (m, loft);
    }

    public Deck AddDeck(Module m, float y, float x0, float x1, float z0, float z1, int level)
    {
        var d = new Deck { ModuleId = m.Id, Y = y, X0 = x0, X1 = x1, Z0 = z0, Z1 = z1, Level = level };
        if (d.Width > 1.5f && d.Length > 1.5f) Decks.Add(d);
        return d;
    }

    /// <summary>
    /// Registers the flat underside of a hull mass between z0 and z1: an underside deck for hanging equipment, and,
    /// when <paramref name="main"/>, the span the ventral structure (keel, gondola, pods) hangs from.
    /// </summary>
    public Deck AddBelly(Module m, Profile p, float z0, float z1, bool main)
    {
        Bellies.Add(new BellySpan(m, p, z0, z1, main));
        var half = p.BottomFlatHalf;
        var d = new Deck { ModuleId = m.Id, Y = p.Bottom, X0 = p.Cx - half, X1 = p.Cx + half, Z0 = z0, Z1 = z1, Level = 0, Underside = true };
        if (d.Width > 1.2f && d.Length > 2f) Decks.Add(d);
        return d;
    }

    public void AddSides(Module m, Loft loft, int segment, params int[] faces)
    {
        foreach (var f in faces)
        {
            var (along, across) = loft.FaceSize(segment, f);
            if (along > 4 && across > 2.5f) Sides.Add(new SideSurface { Module = m, Loft = loft, Segment = segment, Face = f });
        }
    }

    public static string FaceKey(Module m, int s, int f) => $"{m.Id}:{s}:{f}";

    public bool SurfaceFree(string key, float u0, float u1, float v0, float v1) =>
        !_surfaceUse.TryGetValue(key, out var list) || list.All(r => u1 <= r.u0 || u0 >= r.u1 || v1 <= r.v0 || v0 >= r.v1);

    public bool TryUseSurface(string key, float u0, float u1, float v0, float v1)
    {
        if (!SurfaceFree(key, u0, u1, v0, v1)) return false;
        UseSurface(key, u0, u1, v0, v1);
        return true;
    }

    public void UseSurface(string key, float u0, float u1, float v0, float v1)
    {
        if (!_surfaceUse.TryGetValue(key, out var list)) _surfaceUse[key] = list = [];
        list.Add((u0, u1, v0, v1));
    }

    public Marker AddMarker(string kind, Module m, Vector3 position, Vector3 direction, float radius = 0, float clearance = 0)
    {
        var n = Ship.Markers.Count(x => x.Kind == kind);
        var marker = new Marker($"{kind}_{n}", kind, m.Id, position, direction == Vector3.Zero ? Vector3.UnitY : Vector3.Normalize(direction), radius, clearance);
        Ship.Markers.Add(marker);
        return marker;
    }

    public void AddEnvelope(string kind, Module m, Aabb box)
    {
        var n = Ship.Envelopes.Count(x => x.Kind == kind);
        Ship.Envelopes.Add(new Envelope($"{kind}_{n}", kind, m.Id, box));
    }

    /// <summary>True when the box is clear of every primary and secondary module except those listed.</summary>
    public bool Clear(Aabb box, params string[] except)
    {
        foreach (var m in Ship.Modules)
        {
            if (m.Tier == ModuleTier.Tertiary || except.Contains(m.Id)) continue;
            m.UpdateBounds();
            if (m.Bounds.Penetration(box) > 0.05f) return false;
        }
        return true;
    }
}

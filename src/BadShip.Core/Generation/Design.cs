namespace BadShip.Generation;

/// <summary>
/// A manufacturer's design language: how its yards build ships, whatever the role or size. It biases the planner's
/// choices (hull family, hull form, prow, bridge, engines, tiers, underside) and the shapes themselves (chamfers,
/// facets, taper, engine bay size, superstructure steps, sensors, windows, plating), and adds one signature element.
/// Ships from the same manufacturer share architecture; paint comes from the operating faction.
/// </summary>
public sealed class DesignLanguage
{
    public string Name { get; set; } = "Generic Yards";
    public string Motto { get; set; } = "";

    /// <summary>Multipliers on the role's hull-family weights (barge, spine, twin, block).</summary>
    public Dictionary<string, double> Families { get; set; } = new() { ["barge"] = 1, ["spine"] = 1, ["twin"] = 1, ["block"] = 1 };

    /// <summary>Chance of the faceted hull form (otherwise slab).</summary>
    public double Faceted { get; set; } = 0.45;

    /// <summary>Multipliers on the role's prow-style weights (blunt, stepped, faceted, shovel, wedge).</summary>
    public Dictionary<string, double> Prows { get; set; } = new() { ["blunt"] = 1, ["stepped"] = 1, ["faceted"] = 1, ["shovel"] = 1, ["wedge"] = 1 };

    public Dictionary<string, double> Bridges { get; set; } = new() { ["slope"] = 0.45, ["overhang"] = 0.4, ["blunt"] = 0.15 };
    public Dictionary<string, double> EngineLayouts { get; set; } = new() { ["2x1"] = 0.3, ["3x1"] = 0.2, ["2x2"] = 0.25, ["1x2"] = 0.15, ["4x1"] = 0.1 };

    /// <summary>Chance of boxy engine pods (otherwise round nacelles).</summary>
    public double PodEngines { get; set; } = 0.55;

    /// <summary>Engine bay size relative to the usual (big engines read as a brand).</summary>
    public float EngineScale { get; set; } = 1f;

    /// <summary>Chamfer size and bottom taper relative to the usual: 0.6 is crisp and boxy, 1.5 heavily bevelled.</summary>
    public float Chamfer { get; set; } = 1f;
    public float Taper { get; set; } = 1f;

    /// <summary>Superstructure: extra tiers over the size class's usual count, and length of the first tier relative to the usual.</summary>
    public int TierBias { get; set; }
    public float TierLength { get; set; } = 1f;

    public Dictionary<string, double> Ventral { get; set; } = new() { ["keel"] = 1, ["gondola"] = 1, ["pods"] = 1, ["none"] = 1 };

    /// <summary>Weights for the kind of sensor a ship carries: radome, dish, mast.</summary>
    public Dictionary<string, double> Sensors { get; set; } = new() { ["radome"] = 0.5, ["dish"] = 0.25, ["mast"] = 0.25 };

    /// <summary>clusters (short groups of panes), bands (long ribbons) or slits (narrow armoured slots).</summary>
    public string Windows { get; set; } = "clusters";

    /// <summary>Chance of side modules bolted along the hull, and how dense the plating is.</summary>
    public double SideBlocks { get; set; } = 0.7;
    public float PlateDensity { get; set; } = 1f;
    public double Asymmetry { get; set; } = 0.25;

    /// <summary>A recognisable element: none, dorsal_ridge, accent_bands or engine_collar.</summary>
    public string Signature { get; set; } = "none";

    public DesignLanguage Clone() => (DesignLanguage)MemberwiseClone();
}

/// <summary>The yards. Five named houses with distinct languages, plus procedural ones from a seed ("yard-123").</summary>
public static class Manufacturers
{
    public static readonly Dictionary<string, DesignLanguage> Presets = new()
    {
        ["halvorsen"] = new DesignLanguage
        {
            Name = "Halvorsen Heavy Works", Motto = "Built square, built to last.",
            Families = new() { ["barge"] = 2.2, ["spine"] = 0.8, ["twin"] = 0.6, ["block"] = 1 },
            Faceted = 0.08, Chamfer = 0.65f, Taper = 0.6f,
            Prows = new() { ["blunt"] = 3, ["stepped"] = 2.5, ["faceted"] = 0.2, ["shovel"] = 0.6, ["wedge"] = 0.1 },
            Bridges = new() { ["overhang"] = 0.75, ["blunt"] = 0.25 },
            EngineLayouts = new() { ["2x2"] = 0.6, ["2x1"] = 0.4 }, PodEngines = 1, EngineScale = 1.15f,
            TierBias = 0, TierLength = 1.1f, SideBlocks = 0.95, PlateDensity = 1.2f,
            Sensors = new() { ["mast"] = 0.7, ["radome"] = 0.3 }, Windows = "clusters",
            Ventral = new() { ["keel"] = 1, ["pods"] = 1.5, ["gondola"] = 0.2, ["none"] = 0.6 },
            Signature = "engine_collar", Asymmetry = 0.35,
        },
        ["meridian"] = new DesignLanguage
        {
            Name = "Meridian Survey Yards", Motto = "See further.",
            Families = new() { ["barge"] = 2, ["spine"] = 0.8, ["twin"] = 1, ["block"] = 0.3 },
            Faceted = 0.95, Chamfer = 1.15f, Taper = 1.1f,
            Prows = new() { ["faceted"] = 3, ["wedge"] = 1.5, ["shovel"] = 0.2, ["blunt"] = 0.1, ["stepped"] = 0.2 },
            Bridges = new() { ["slope"] = 0.8, ["overhang"] = 0.2 },
            EngineLayouts = new() { ["3x1"] = 0.6, ["2x1"] = 0.4 }, PodEngines = 0.15, EngineScale = 0.95f,
            TierBias = 1, TierLength = 1.15f, SideBlocks = 0.5, PlateDensity = 0.8f,
            Sensors = new() { ["radome"] = 0.6, ["dish"] = 0.3, ["mast"] = 0.1 }, Windows = "bands",
            Ventral = new() { ["gondola"] = 2, ["keel"] = 1, ["pods"] = 0.2, ["none"] = 0.4 },
            Signature = "dorsal_ridge", Asymmetry = 0.1,
        },
        ["korsak"] = new DesignLanguage
        {
            Name = "Korsak Arsenal", Motto = "Armour first.",
            Families = new() { ["block"] = 2.5, ["barge"] = 1, ["twin"] = 0.4, ["spine"] = 0.2 },
            Faceted = 0.7, Chamfer = 1.4f, Taper = 1.2f,
            Prows = new() { ["wedge"] = 3, ["faceted"] = 1.2, ["shovel"] = 0.3, ["blunt"] = 0.1, ["stepped"] = 0.1 },
            Bridges = new() { ["blunt"] = 0.6, ["slope"] = 0.4 },
            EngineLayouts = new() { ["2x1"] = 0.5, ["3x1"] = 0.5 }, PodEngines = 0.7, EngineScale = 1.05f,
            TierBias = -1, TierLength = 0.85f, SideBlocks = 0.4, PlateDensity = 1.5f,
            Sensors = new() { ["radome"] = 0.5, ["mast"] = 0.5 }, Windows = "slits",
            Ventral = new() { ["keel"] = 2.5, ["gondola"] = 0.4, ["none"] = 0.5, ["pods"] = 0.1 },
            Signature = "accent_bands", Asymmetry = 0.05,
        },
        ["tanaka"] = new DesignLanguage
        {
            Name = "Tanaka-Orbit Logistics", Motto = "Cargo is the ship.",
            Families = new() { ["spine"] = 3, ["twin"] = 1, ["barge"] = 0.6, ["block"] = 0.2 },
            Faceted = 0.3, Chamfer = 0.8f, Taper = 0.8f,
            Prows = new() { ["blunt"] = 2, ["shovel"] = 1.5, ["faceted"] = 0.8, ["stepped"] = 0.6, ["wedge"] = 0.2 },
            Bridges = new() { ["slope"] = 0.5, ["blunt"] = 0.5 },
            EngineLayouts = new() { ["1x2"] = 0.6, ["2x1"] = 0.4 }, PodEngines = 0.85, EngineScale = 1.1f,
            TierBias = -1, TierLength = 0.8f, SideBlocks = 0.3, PlateDensity = 0.7f,
            Sensors = new() { ["mast"] = 0.5, ["dish"] = 0.3, ["radome"] = 0.2 }, Windows = "clusters",
            Ventral = new() { ["pods"] = 2.5, ["none"] = 1, ["keel"] = 0.4, ["gondola"] = 0.3 },
            Signature = "engine_collar", Asymmetry = 0.45,
        },
        ["brightwater"] = new DesignLanguage
        {
            Name = "Brightwater Cooperative", Motto = "Two hulls are better than one.",
            Families = new() { ["twin"] = 3, ["barge"] = 1, ["spine"] = 0.5, ["block"] = 0.5 },
            Faceted = 0.5, Chamfer = 1, Taper = 1,
            Prows = new() { ["shovel"] = 2.5, ["faceted"] = 1.2, ["blunt"] = 0.6, ["wedge"] = 0.4, ["stepped"] = 0.2 },
            Bridges = new() { ["overhang"] = 0.6, ["slope"] = 0.4 },
            EngineLayouts = new() { ["2x1"] = 0.5, ["1x2"] = 0.5 }, PodEngines = 0.4, EngineScale = 1,
            TierBias = 0, TierLength = 1, SideBlocks = 0.6, PlateDensity = 1,
            Sensors = new() { ["dish"] = 0.5, ["radome"] = 0.4, ["mast"] = 0.1 }, Windows = "bands",
            Ventral = new() { ["gondola"] = 1.5, ["pods"] = 1, ["none"] = 1, ["keel"] = 0.2 },
            Signature = "dorsal_ridge", Asymmetry = 0.2,
        },
    };

    public static IEnumerable<string> Names => Presets.Keys;

    /// <summary>A preset by key, or a procedural yard for "yard-&lt;seed&gt;".</summary>
    public static DesignLanguage Get(string key)
    {
        if (Presets.TryGetValue(key, out var preset)) return preset.Clone();
        if (key.StartsWith("yard-") && ulong.TryParse(key[5..], out var seed)) return Procedural(seed);
        throw new ArgumentException($"Unknown manufacturer '{key}'. Use one of: {string.Join(", ", Names)}, yard-<number>, auto.");
    }

    /// <summary>A procedural yard: every choice gets one or two clear favourites, so its ships look related.</summary>
    public static DesignLanguage Procedural(ulong seed)
    {
        var r = Rng.Stream(seed, "manufacturer");
        Dictionary<string, double> Favour(string[] keys, double others = 0.15)
        {
            var d = keys.ToDictionary(k => k, _ => others);
            d[r.Pick(keys)] = 3;
            if (r.Chance(0.6)) d[r.Pick(keys)] += 1.5;
            return d;
        }
        string[] syllables = ["Ar", "Vel", "Kor", "Dun", "Mar", "Tes", "Oro", "Bal", "Rin", "Sto", "Kev", "Hal", "Ny", "Gra"];
        string[] kinds = ["Shipworks", "Yards", "Orbital", "Fabrication", "Drydocks", "Engineering", "Heavy Industries"];
        var name = r.Pick(syllables) + r.Pick(syllables).ToLowerInvariant() + (r.Chance(0.4) ? r.Pick(syllables).ToLowerInvariant() : "");
        var layouts = new[] { "2x1", "3x1", "2x2", "1x2", "4x1" };
        return new DesignLanguage
        {
            Name = $"{name} {r.Pick(kinds)}",
            Families = Favour(["barge", "spine", "twin", "block"], 0.4),
            Faceted = r.Chance(0.5) ? r.Range(0.75f, 0.95f) : r.Range(0.05f, 0.3f),
            Chamfer = r.Range(0.6f, 1.5f),
            Taper = r.Range(0.6f, 1.25f),
            Prows = Favour(["blunt", "stepped", "faceted", "shovel", "wedge"]),
            Bridges = Favour(["slope", "overhang", "blunt"], 0.2),
            EngineLayouts = Favour(layouts, 0.05),
            PodEngines = r.Chance(0.5) ? 0.9 : 0.1,
            EngineScale = r.Range(0.9f, 1.2f),
            TierBias = r.Int(-1, 1),
            TierLength = r.Range(0.8f, 1.2f),
            Ventral = Favour(["keel", "gondola", "pods", "none"], 0.3),
            Sensors = Favour(["radome", "dish", "mast"], 0.3),
            Windows = r.Pick(["clusters", "bands", "slits"]),
            SideBlocks = r.Range(0.2f, 0.95f),
            PlateDensity = r.Range(0.6f, 1.5f),
            Asymmetry = r.Range(0.05f, 0.45f),
            Signature = r.Pick(["dorsal_ridge", "accent_bands", "engine_collar", "none"]),
        };
    }
}

/// <summary>An operator: its paint, the roles and sizes it fields, and the yards it buys from.</summary>
public sealed class Faction
{
    public string Name { get; set; } = "Independent";
    public string HullMain { get; set; } = "";
    public string HullSecondary { get; set; } = "";
    public string Accent { get; set; } = "";
    public Dictionary<string, double> Roles { get; set; } = new() { ["freighter"] = 1, ["research"] = 1, ["patrol"] = 1, ["fighter"] = 0.8, ["industrial"] = 0.6, ["utility"] = 1 };
    public Dictionary<string, double> Sizes { get; set; } = new() { ["small"] = 1, ["medium"] = 2, ["large"] = 1 };
    public Dictionary<string, double> Suppliers { get; set; } = Manufacturers.Presets.Keys.ToDictionary(k => k, _ => 1.0);
}

public static class Factions
{
    public static readonly Dictionary<string, Faction> Presets = new()
    {
        ["haulers"] = new Faction
        {
            Name = "Free Haulers Guild", HullMain = "#4a5058", HullSecondary = "#6b7179", Accent = "#b85a22",
            Roles = new() { ["freighter"] = 4, ["research"] = 0.5, ["patrol"] = 1, ["fighter"] = 0.5, ["industrial"] = 0.8, ["utility"] = 1.5 },
            Sizes = new() { ["small"] = 1.2, ["medium"] = 2, ["large"] = 1.5 },
            Suppliers = new() { ["halvorsen"] = 2, ["tanaka"] = 2, ["brightwater"] = 0.5 },
        },
        ["survey"] = new Faction
        {
            Name = "Oceanic Survey Directorate", HullMain = "#a7a59d", HullSecondary = "#7d7f80", Accent = "#c7902a",
            Roles = new() { ["research"] = 4, ["freighter"] = 1, ["patrol"] = 0.3, ["utility"] = 1.2 },
            Sizes = new() { ["small"] = 1.5, ["medium"] = 2, ["large"] = 1 },
            Suppliers = new() { ["meridian"] = 3, ["brightwater"] = 1.5, ["halvorsen"] = 0.5 },
        },
        ["patrol"] = new Faction
        {
            Name = "Frontier Patrol Command", HullMain = "#363c44", HullSecondary = "#555c66", Accent = "#a8291f",
            Roles = new() { ["patrol"] = 4, ["research"] = 0.6, ["freighter"] = 0.8, ["fighter"] = 2.5, ["citadel"] = 0.4 },
            Sizes = new() { ["small"] = 2, ["medium"] = 2, ["large"] = 0.8 },
            Suppliers = new() { ["korsak"] = 3, ["halvorsen"] = 1, ["meridian"] = 0.4 },
        },
        ["miners"] = new Faction
        {
            Name = "Kessler Belt Mining Union", HullMain = "#6c7378", HullSecondary = "#8b9297", Accent = "#d9b21c",
            Roles = new() { ["freighter"] = 3, ["research"] = 1, ["patrol"] = 0.5, ["fighter"] = 0.3, ["industrial"] = 3, ["utility"] = 1.2 },
            Sizes = new() { ["small"] = 1.5, ["medium"] = 2, ["large"] = 1 },
            Suppliers = new() { ["halvorsen"] = 2, ["tanaka"] = 1, ["korsak"] = 0.5 },
        },
    };

    public static IEnumerable<string> Names => Presets.Keys;

    /// <summary>A preset by key, "independent" (anyone's ships, any paint), or a procedural faction "faction-&lt;seed&gt;".</summary>
    public static Faction Get(string key)
    {
        if (Presets.TryGetValue(key, out var f)) return f;
        if (key == "independent") return new Faction();
        if (key.StartsWith("faction-") && ulong.TryParse(key[8..], out var seed)) return Procedural(seed);
        throw new ArgumentException($"Unknown faction '{key}'. Use one of: {string.Join(", ", Names)}, independent, faction-<number>, auto.");
    }

    public static Faction Procedural(ulong seed)
    {
        var r = Rng.Stream(seed, "faction");
        string[] hulls = ["#4a5058", "#6c7378", "#a7a59d", "#363c44", "#5a5248", "#3f4a52"];
        string[] accents = ["#c7902a", "#d9b21c", "#b85a22", "#a8291f", "#3f7fa8", "#5e8a4a", "#d0d0c8"];
        string[] a = ["Outer", "Free", "United", "Northern", "Second", "Deep", "Red", "Iron", "Ceres", "Titan"];
        string[] b = ["Rim", "Reach", "Belt", "Systems", "Colonies", "Frontier", "Lanes", "Marches"];
        string[] c = ["Authority", "Combine", "Syndicate", "League", "Collective", "Navy", "Company", "Union"];
        var hull = r.Pick(hulls);
        var suppliers = Manufacturers.Presets.Keys.ToDictionary(k => k, _ => 0.2);
        suppliers[r.Pick(suppliers.Keys.ToList())] = 3;
        suppliers[$"yard-{seed}"] = r.Chance(0.6) ? 2.5 : 0;
        return new Faction
        {
            Name = $"{r.Pick(a)} {r.Pick(b)} {r.Pick(c)}",
            HullMain = hull, HullSecondary = Lighten(hull), Accent = r.Pick(accents),
            Roles = new() { ["freighter"] = r.Range(0.3f, 3f), ["research"] = r.Range(0.3f, 3f), ["patrol"] = r.Range(0.3f, 3f), ["fighter"] = r.Range(0.2f, 2.5f) },
            Sizes = new() { ["small"] = r.Range(0.5f, 2f), ["medium"] = 2, ["large"] = r.Range(0.3f, 1.5f) },
            Suppliers = suppliers,
        };
    }

    private static string Lighten(string hex)
    {
        var v = Convert.ToInt32(hex.TrimStart('#'), 16);
        int L(int x) => Math.Min(255, x + 32);
        return $"#{L((v >> 16) & 255):x2}{L((v >> 8) & 255):x2}{L(v & 255):x2}";
    }
}

using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// A faction's fleet doctrine: the design choices its ships share whatever yard built them and whatever their role, so a
/// fleet reads as one navy: hull form, bridge style, pod or nacelle engines, nozzle style, window style, sensor kind and
/// a signature element (dorsal ridge, accent bands or engine collar), plus how it names its ships. The doctrine comes from
/// the faction alone; each ship follows each point with a high chance (on its own stream), so a fleet matches without
/// being clones. A yard's strong habits still win over it (a Halvorsen ship is square and has pods, whoever ordered it),
/// and so do the recipe's overrides. Independents have no doctrine.
/// </summary>
public sealed class Doctrine
{
    public string HullForm { get; init; } = "slab";
    public string BridgeStyle { get; init; } = "slope";
    public string EngineStyle { get; init; } = "pod";
    public string NozzleStyle { get; init; } = "bell";
    public string Windows { get; init; } = "clusters";
    public string Sensor { get; init; } = "radome";
    public string Signature { get; init; } = "none";
    public string[] Names { get; init; } = [];

    /// <summary>How often a ship follows each point of its faction's doctrine.</summary>
    public const double Adherence = 0.85;

    private static readonly Dictionary<string, string[]> Themes = new()
    {
        ["minerals"] = ["Basalt", "Feldspar", "Cobalt", "Gneiss", "Galena", "Mica", "Pyrite", "Hematite", "Obsidian", "Malachite", "Bauxite", "Cinnabar", "Quartzite", "Magnetite", "Dolomite", "Garnet"],
        ["seabirds"] = ["Albatross", "Petrel", "Tern", "Shearwater", "Fulmar", "Gannet", "Kittiwake", "Skua", "Cormorant", "Frigatebird", "Prion", "Noddy", "Booby", "Auk", "Puffin", "Storm Petrel"],
        ["virtues"] = ["Vigilant", "Resolute", "Steadfast", "Valiant", "Dauntless", "Constant", "Faithful", "Intrepid", "Unwavering", "Diligent", "Fearless", "Loyal", "Prudent", "Tenacious", "Gallant", "Stalwart"],
        ["haulage"] = ["Stubborn Ox", "Iron Mule", "Long Haul", "Packhorse", "Steady Hand", "Old Reliable", "Heavy Lifter", "Draught Horse", "Workhorse", "Pack Mule", "Yoke", "Plough", "Dray", "Barrow", "Tow Line", "Hand Cart"],
        ["rogues"] = ["Black Gull", "Cutthroat Grin", "Salt and Spite", "Red Tally", "No Quarter", "Loose Cannon", "Sweet Larceny", "Crooked Smile", "Bilge Rat", "Last Laugh", "Stolen Sky", "Bad Penny", "Sharp Practice", "Fair Game", "Grudge", "Hard Bargain"],
        ["rivers"] = ["Volga", "Danube", "Mekong", "Orinoco", "Zambezi", "Yukon", "Indus", "Rhine", "Amur", "Paraná", "Vistula", "Ganges", "Niger", "Mackenzie", "Murray", "Loire"],
        ["stars"] = ["Antares", "Rigel", "Deneb", "Altair", "Vega", "Arcturus", "Capella", "Procyon", "Aldebaran", "Spica", "Regulus", "Castor", "Pollux", "Mira", "Polaris", "Bellatrix"],
        ["mountains"] = ["Elbrus", "Denali", "Kilimanjaro", "Aconcagua", "Matterhorn", "Olympus", "Kosciuszko", "Ararat", "Etna", "Fuji", "Rainier", "Erebus", "Logan", "Sajama", "Triglav", "Rysy"],
    };

    private static readonly Dictionary<string, string> PresetThemes = new()
    {
        ["haulers"] = "haulage", ["survey"] = "seabirds", ["patrol"] = "virtues", ["miners"] = "minerals", ["raiders"] = "rogues",
    };

    /// <summary>The doctrine of a faction (by its key), or null for independents.</summary>
    public static Doctrine? Of(string faction)
    {
        if (string.IsNullOrEmpty(faction) || faction is "independent" or "auto") return null;
        var r = Rng.Stream(Rng.Fnv1a(faction), "doctrine");
        // A few presets have an obvious character; the rest is drawn from the faction key.
        var military = faction is "patrol";
        var theme = PresetThemes.GetValueOrDefault(faction) ?? r.Pick(["rivers", "stars", "mountains", "virtues"]);
        return new Doctrine
        {
            HullForm = r.Chance(military ? 0.75 : 0.4) ? "faceted" : "slab",
            BridgeStyle = r.Pick(["slope", "overhang", "blunt"]),
            EngineStyle = r.Chance(0.5) ? "pod" : "nacelle",
            NozzleStyle = military ? r.Weighted<string>([("iris", 0.7), ("ring", 0.3)]) : r.Pick(Nozzles.Styles),
            Windows = r.Pick(["clusters", "bands", "slits"]),
            Sensor = r.Pick(["radome", "dish", "mast"]),
            Signature = r.Pick(["dorsal_ridge", "accent_bands", "engine_collar"]),
            Names = Themes[theme],
        };
    }

    /// <summary>
    /// Brings a planned ship into line with its faction's doctrine (point by point, with <see cref="Adherence"/>),
    /// before the recipe's own overrides. Fighters, citadels and utility craft keep their own design vocabulary; they
    /// share the nozzles, windows and names.
    /// </summary>
    public static void Apply(BuildContext c)
    {
        var p = c.Plan;
        if (Of(p.Faction) is not { } d) return;
        c.Doctrine = d;
        var r = Rng.Stream(c.Recipe.Seed, "doctrine");
        bool Follow() => r.Chance(Adherence);
        var own = p.Family is "fighter" or "citadel" or "utility";
        if (!own)
        {
            // A yard's strong habits win (Halvorsen builds square, with pods): the doctrine decides where the yard
            // has no strong preference.
            var lang0 = c.Lang;
            var yardBridge = lang0.Bridges.Values.Sum() is var total && total > 0 && lang0.Bridges.Values.Max() / total >= 0.6;
            if (Follow() && lang0.Faceted is > 0.2 and < 0.8) p.HullForm = d.HullForm;
            if (Follow() && !yardBridge) p.BridgeStyle = d.BridgeStyle;
            // A liner's long nacelles are part of what it is, and twin hulls' engines are laid out for their own style.
            if (Follow() && lang0.PodEngines is > 0.2 and < 0.8 && p.Family != "twin" && !(p.Archetype == "civilian" && p.Payload == "liner"))
                p.EngineStyle = d.EngineStyle;
            if (Follow() && lang0.Signature == "none") p.Signature = d.Signature;
        }
        var lang = c.Lang.Clone();
        if (Follow()) lang.Windows = d.Windows;
        if (Follow()) lang.Sensors = new() { [d.Sensor] = 1 };
        if (!own && p.Signature != c.Lang.Signature) lang.Signature = p.Signature;
        c.Lang = lang;
        if (Follow()) c.SetNozzleStyle(d.NozzleStyle);
    }
}

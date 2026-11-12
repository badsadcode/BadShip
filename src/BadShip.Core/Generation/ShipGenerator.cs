using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// The pipeline: recipe → plan → primary masses (hull family) → engines and superstructure → equipment → surface
/// detail → finished <see cref="Ship"/>. Validation and export are separate steps.
/// </summary>
public static class ShipGenerator
{
    /// <summary>Bumped whenever a change makes the same recipe give a different ship.</summary>
    public const string Version = "0.6.0";

    public static readonly string[] Archetypes = ["freighter", "research", "patrol"];
    public static readonly string[] Families = ["barge", "spine", "twin", "block"];

    public static Ship Generate(ShipRecipe recipe)
    {
        var c = new BuildContext(recipe);
        Planner.Resolve(c);
        switch (c.Plan.Family)
        {
            case "barge": HullFamilies.Barge(c); break;
            case "spine": HullFamilies.Spine(c); break;
            case "twin": HullFamilies.Twin(c); break;
            case "block": HullFamilies.Block(c); break;
            default: throw new ArgumentException($"Unknown family '{c.Plan.Family}'.");
        }
        Signatures.Apply(c);
        Ventral.Build(c);
        Equipment.Place(c);
        Livery.LiveryPass.Apply(c);
        c.Plan.Condition = c.Recipe.Condition is "damaged" or "derelict" ? c.Recipe.Condition
            : string.IsNullOrEmpty(c.Recipe.Condition) || c.Recipe.Condition == "intact" ? "intact"
            : throw new ArgumentException($"Unknown condition '{c.Recipe.Condition}'. Use intact, damaged or derelict.");
        Damage.Apply(c);
        if (c.DetailLevel > 0) Details.Add(c);
        Damage.AfterDetails(c);
        c.Ship.Finish();
        return c.Ship;
    }
}

/// <summary>Turns the recipe's "auto" fields into concrete choices.</summary>
public static class Planner
{
    private static readonly Dictionary<string, (string, double)[]> FamilyWeights = new()
    {
        ["freighter"] = [("barge", 0.3), ("spine", 0.4), ("twin", 0.3)],
        ["research"] = [("barge", 0.5), ("spine", 0.25), ("twin", 0.25)],
        ["patrol"] = [("block", 0.65), ("barge", 0.25), ("twin", 0.1)],
    };

    private static readonly Dictionary<string, (string, double)[]> ProwWeights = new()
    {
        ["freighter"] = [("blunt", 0.3), ("stepped", 0.25), ("faceted", 0.2), ("shovel", 0.15), ("wedge", 0.1)],
        ["research"] = [("faceted", 0.4), ("wedge", 0.25), ("stepped", 0.15), ("shovel", 0.2)],
        ["patrol"] = [("wedge", 0.45), ("faceted", 0.3), ("shovel", 0.25)],
    };

    public static readonly string[] Sizes = ["small", "medium", "large"];

    private static List<(string, double)> W(Dictionary<string, double> d) => d.Where(kv => kv.Value > 0).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => (kv.Key, kv.Value)).ToList();

    private static List<(string, double)> Mul((string, double)[] baseWeights, Dictionary<string, double> bias, Func<string, double>? extra = null) =>
        baseWeights.Select(x => (x.Item1, x.Item2 * bias.GetValueOrDefault(x.Item1, 1) * (extra?.Invoke(x.Item1) ?? 1))).Where(x => x.Item2 > 0).ToList();

    public static void Resolve(BuildContext c)
    {
        var r = c.PlanRng;
        var rec = c.Recipe;
        var p = c.Plan;

        // Who operates it, how big it is, what it does, and which yard built it.
        p.Faction = string.IsNullOrEmpty(rec.Faction) || rec.Faction == "auto"
            ? r.Weighted(Factions.Names.Select(n => (n, 1.0)).Append(("independent", 1.0)).ToList())
            : rec.Faction;
        var faction = Factions.Get(p.Faction);
        p.FactionName = faction.Name;
        p.Size = Choose(rec.Size, Sizes, () => r.Weighted(W(faction.Sizes)), "size");
        p.Archetype = Choose(rec.Archetype, ShipGenerator.Archetypes, () => r.Weighted(W(faction.Roles)), "archetype");
        p.Manufacturer = string.IsNullOrEmpty(rec.Manufacturer) || rec.Manufacturer == "auto" ? r.Weighted(W(faction.Suppliers)) : rec.Manufacturer;
        var lang = Manufacturers.Get(p.Manufacturer);
        c.Lang = lang;
        p.ManufacturerName = lang.Name;
        p.Signature = lang.Signature;

        // Hull family: what the role needs, biased by the yard's habits and limited by size.
        double SizeFit(string f) => (p.Size, f) switch
        {
            ("small", "spine") => 0, ("small", "twin") => 0.4, ("small", "block") => 1.8,
            ("large", "block") => 0.3, ("large", "spine") => 1.5,
            _ => 1,
        };
        p.Family = Choose(rec.Family, ShipGenerator.Families, () => r.Weighted(Mul(FamilyWeights[p.Archetype], lang.Families, SizeFit)), "family");
        // A spine needs room for its payload segments: never a small craft.
        if (p.Family == "spine" && p.Size == "small") p.Size = "medium";

        var (lmin, lmax) = (p.Size, p.Archetype, p.Family) switch
        {
            ("small", _, "block") => (26f, 48f),
            ("small", _, _) => (32f, 62f),
            ("large", _, "block") => (125f, 175f),
            ("large", "patrol", _) => (160f, 230f),
            ("large", "research", _) => (190f, 270f),
            ("large", _, _) => (230f, 340f),
            (_, _, "block") => (70f, 115f),
            (_, "patrol", _) => (90f, 130f),
            (_, "research", _) => (110f, 170f),
            _ => (130f, 210f),
        };
        p.Length = rec.Length ?? r.Range(lmin, lmax);
        var L = p.Length;
        (p.Beam, p.Height) = p.Family switch
        {
            "barge" => (L * r.Range(0.2f, 0.28f), L * r.Range(0.11f, 0.15f)),
            "spine" => (L * r.Range(0.18f, 0.24f), L * r.Range(0.1f, 0.13f)),
            "twin" => (L * r.Range(0.3f, 0.4f), L * r.Range(0.1f, 0.13f)),
            _ => (L * r.Range(0.34f, 0.46f), L * r.Range(0.17f, 0.24f)),
        };
        // Small craft are stubby (crew spaces and engines don't shrink with the hull); big ships are long and lean.
        if (p.Size == "small") { p.Beam *= 1.18f; p.Height *= 1.3f; }
        if (p.Size == "large") { p.Beam *= 0.9f; p.Height *= 0.88f; }

        // Every prow style stays possible (a small floor), so a yard's favourite prow shows up across all its roles.
        var prowBase = new[] { "blunt", "stepped", "faceted", "shovel", "wedge" }
            .Select(st => (st, ProwWeights[p.Archetype].FirstOrDefault(x => x.Item1 == st).Item2 is var w and > 0 ? w : 0.08)).ToArray();
        p.ProwStyle = r.Weighted(Mul(prowBase, lang.Prows));
        p.HullForm = r.Chance(Math.Clamp(lang.Faceted + (p.Archetype == "research" ? 0.1 : 0), 0, 1)) ? "faceted" : "slab";
        if (p.HullForm == "faceted" && p.ProwStyle is "blunt" or "shovel" && r.Chance(0.7)) p.ProwStyle = r.Pick(["faceted", "wedge"]);
        if (p.Family == "twin" && p.ProwStyle == "stepped") p.ProwStyle = "faceted";
        p.BridgeStyle = r.Weighted(W(lang.Bridges));
        var tiers = p.Family switch { "barge" => r.Int(2, 4), _ => r.Int(1, 3) } + lang.TierBias;
        p.Tiers = p.Size switch { "small" => Math.Clamp(tiers - 2, 1, 2), "large" => Math.Clamp(tiers + 1, 2, 5), _ => Math.Clamp(tiers, 1, 4) };
        var layouts = W(lang.EngineLayouts);
        if (p.Size == "small")
        {
            // Small craft: one big engine, or a pair.
            layouts = layouts.Where(l => l.Item1 is "2x1" or "1x2").Append(("1x1", 0.6)).ToList();
        }
        p.EngineLayout = r.Weighted(layouts);
        p.EngineStyle = r.Chance(lang.PodEngines) ? "pod" : "nacelle";
        p.Payload = p.Family != "spine" ? "" : p.Archetype switch
        {
            "freighter" => r.Weighted<string>([("containers", 0.5), ("tanks", 0.3), ("pods", 0.2)]),
            "research" => r.Weighted<string>([("pods", 0.7), ("tanks", 0.3)]),
            _ => "pods",
        };
        p.Asymmetric = r.Chance(lang.Asymmetry + (p.Archetype == "freighter" ? 0.1 : 0));
        (string, double)[] ventralBase = p.Family switch
        {
            "barge" => [("keel", 0.4), ("gondola", 0.25), ("pods", p.Archetype == "freighter" ? 0.3 : 0.1), ("none", 0.15)],
            "block" => [("keel", 0.5), ("gondola", 0.2), ("none", 0.3)],
            "spine" => [("keel", 0.35), ("gondola", 0.15), ("none", 0.5)],
            _ => [("gondola", 0.4), ("pods", 0.25), ("none", 0.35)],
        };
        p.Ventral = r.Weighted(Mul(ventralBase, lang.Ventral));
        ApplyOverrides(rec.Overrides, p);

        // Paint comes from the operator.
        if ((string.IsNullOrEmpty(rec.Palette) || rec.Palette == "auto") && !string.IsNullOrEmpty(faction.HullMain))
        {
            p.Palette = "faction";
            c.Ship.Materials = Palettes.Build(faction.HullMain, faction.HullSecondary, faction.Accent, c.Look);
        }
        else
        {
            p.Palette = Choose(rec.Palette, Palettes.Names, () => c.Look.Pick(Palettes.Names), "palette");
            c.Ship.Materials = Palettes.Build(p.Palette, c.Look, faction.Accent);
        }
    }

    /// <summary>
    /// Pins hand-set choices over the seeded ones. The seeded choices are still drawn first, so pinning one choice
    /// never reshuffles the others.
    /// </summary>
    private static void ApplyOverrides(PlanOverrides? o, ShipPlan p)
    {
        if (o is null) return;
        static string Pick(string? v, string current, string[] allowed, string what) =>
            v is null ? current : allowed.Contains(v) ? v : throw new ArgumentException($"Unknown {what} '{v}'. Use one of: {string.Join(", ", allowed)}.");
        p.HullForm = Pick(o.HullForm, p.HullForm, ["slab", "faceted"], "hull form");
        p.ProwStyle = Pick(o.ProwStyle, p.ProwStyle, ["blunt", "stepped", "faceted", "shovel", "wedge"], "prow style");
        if (p.Family == "twin" && p.ProwStyle == "stepped") p.ProwStyle = "faceted";
        p.BridgeStyle = Pick(o.BridgeStyle, p.BridgeStyle, ["slope", "overhang", "blunt"], "bridge style");
        p.EngineLayout = Pick(o.EngineLayout, p.EngineLayout, ["1x1", "2x1", "3x1", "2x2", "1x2", "4x1"], "engine layout");
        p.EngineStyle = Pick(o.EngineStyle, p.EngineStyle, ["pod", "nacelle"], "engine style");
        if (p.Family == "spine") p.Payload = Pick(o.Payload, p.Payload, ["containers", "tanks", "pods"], "payload");
        if (o.Tiers is { } t) p.Tiers = t is >= 1 and <= 5 ? t : throw new ArgumentException("Tiers must be 1–5.");
        if (o.Asymmetric is { } a) p.Asymmetric = a;
        p.Ventral = Pick(o.Ventral, p.Ventral, ["none", "keel", "gondola", "pods"], "ventral structure");
        if (p.Family == "twin" && p.Ventral == "keel") p.Ventral = "gondola";
        if (o.BeamRatio is { } br) p.Beam = p.Length * (br is >= 0.1f and <= 0.6f ? br : throw new ArgumentException("Beam ratio must be 0.1–0.6."));
        if (o.HeightRatio is { } hr) p.Height = p.Length * (hr is >= 0.05f and <= 0.35f ? hr : throw new ArgumentException("Height ratio must be 0.05–0.35."));
    }

    private static string Choose(string requested, string[] allowed, Func<string> auto, string what)
    {
        if (string.IsNullOrEmpty(requested) || requested == "auto") return auto();
        if (!allowed.Contains(requested)) throw new ArgumentException($"Unknown {what} '{requested}'. Use one of: {string.Join(", ", allowed)}, auto.");
        return requested;
    }
}

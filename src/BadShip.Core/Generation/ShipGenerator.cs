using BadShip.Model;

namespace BadShip.Generation;

/// <summary>
/// The pipeline: recipe → plan → primary masses (hull family) → engines and superstructure → equipment → surface
/// detail → finished <see cref="Ship"/>. Validation and export are separate steps.
/// </summary>
public static class ShipGenerator
{
    /// <summary>Bumped whenever a change makes the same recipe give a different ship.</summary>
    public const string Version = "0.3.0";

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
        Ventral.Build(c);
        Equipment.Place(c);
        if (c.DetailLevel > 0) Details.Add(c);
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

    public static void Resolve(BuildContext c)
    {
        var r = c.PlanRng;
        var rec = c.Recipe;
        var p = c.Plan;

        p.Archetype = Choose(rec.Archetype, ShipGenerator.Archetypes, () => r.Pick(ShipGenerator.Archetypes), "archetype");
        p.Family = Choose(rec.Family, ShipGenerator.Families, () => r.Weighted(FamilyWeights[p.Archetype]), "family");

        var (lmin, lmax) = (p.Archetype, p.Family) switch
        {
            (_, "block") => (70f, 115f),
            ("patrol", _) => (90f, 130f),
            ("research", _) => (110f, 170f),
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

        p.ProwStyle = r.Weighted(ProwWeights[p.Archetype]);
        p.HullForm = r.Chance(p.Archetype == "research" ? 0.7 : p.Family == "block" ? 0.5 : 0.4) ? "faceted" : "slab";
        if (p.HullForm == "faceted" && p.ProwStyle is "blunt" or "shovel" && r.Chance(0.7)) p.ProwStyle = r.Pick(["faceted", "wedge"]);
        if (p.Family == "twin" && p.ProwStyle == "stepped") p.ProwStyle = "faceted";
        p.BridgeStyle = r.Weighted<string>([("slope", 0.45), ("overhang", 0.4), ("blunt", 0.15)]);
        p.Tiers = p.Family switch { "barge" => r.Int(2, 4), "block" => r.Int(1, 3), _ => r.Int(1, 3) };
        p.EngineLayout = r.Weighted<string>([("2x1", 0.3), ("3x1", 0.2), ("2x2", 0.25), ("1x2", 0.15), ("4x1", 0.1)]);
        p.EngineStyle = r.Weighted<string>([("pod", 0.55), ("nacelle", 0.45)]);
        p.Payload = p.Family != "spine" ? "" : p.Archetype switch
        {
            "freighter" => r.Weighted<string>([("containers", 0.5), ("tanks", 0.3), ("pods", 0.2)]),
            "research" => r.Weighted<string>([("pods", 0.7), ("tanks", 0.3)]),
            _ => "pods",
        };
        p.Asymmetric = r.Chance(p.Archetype == "freighter" ? 0.35 : 0.2);
        p.Ventral = p.Family switch
        {
            "barge" => r.Weighted<string>([("keel", 0.4), ("gondola", 0.25), ("pods", p.Archetype == "freighter" ? 0.3 : 0.1), ("none", 0.15)]),
            "block" => r.Weighted<string>([("keel", 0.5), ("gondola", 0.2), ("none", 0.3)]),
            "spine" => r.Weighted<string>([("keel", 0.35), ("gondola", 0.15), ("none", 0.5)]),
            _ => r.Weighted<string>([("gondola", 0.4), ("pods", 0.25), ("none", 0.35)]),
        };
        ApplyOverrides(rec.Overrides, p);
        p.Palette = Choose(rec.Palette, Palettes.Names, () => c.Look.Pick(Palettes.Names), "palette");
        c.Ship.Materials = Palettes.Build(p.Palette, c.Look);
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
        p.EngineLayout = Pick(o.EngineLayout, p.EngineLayout, ["2x1", "3x1", "2x2", "1x2", "4x1"], "engine layout");
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
